import { spawnSync } from 'node:child_process';
import { setTimeout as sleep } from 'node:timers/promises';

// Image updates create active revisions. This restores a verified pause after
// update; it cannot prevent transient activation or replace acquisition fences.
const appProjection = '{id:id,name:name,properties:{provisioningState:properties.provisioningState,latestRevisionName:properties.latestRevisionName,template:{revisionSuffix:properties.template.revisionSuffix,containers:properties.template.containers[].{name:name,image:image}}}}';
const revisionProjection = '{id:id,name:name,properties:{active:properties.active,template:{containers:properties.template.containers[].{name:name,image:image}}}}';

function required(name) {
  const value = process.env[name];
  if (!value || !/^[a-zA-Z0-9._:/@()-]+$/.test(value)) throw Error(`Missing or invalid ${name}`);
  return value;
}

async function deploy() {
  const name = required('INGESTION_APP_NAME');
  const group = required('RESOURCE_GROUP');
  const subscription = required('AZURE_SUBSCRIPTION_ID');
  const image = required('IMAGE');
  const suffix = required('REVISION_SUFFIX');
  if (!/^deploy-[0-9]+-[0-9]+$/.test(suffix)) throw Error('Invalid deployment revision suffix');
  const appId = `/subscriptions/${subscription}/resourceGroups/${group}/providers/Microsoft.App/containerApps/${name}`;
  const ownName = `${name}--${suffix}`;
  const target = ['--name', name, '--resource-group', group, '--subscription', subscription];

  function az(command, query, mutation = false) {
    const result = spawnSync('az', ['containerapp', ...command, ...target,
      ...(query ? ['--query', query] : []), '--output', mutation ? 'none' : 'json', '--only-show-errors'], {
      encoding: 'utf8', timeout: 180_000, maxBuffer: 4 * 1024 * 1024,
      // ARM IDs must not be rewritten by Git Bash on Windows.
      env: { ...process.env, MSYS_NO_PATHCONV: '1' },
    });
    // Do not echo arbitrary Azure responses: template env vars can contain secrets.
    if (result.error || result.status !== 0) throw Error(`Azure ${command.slice(0, 2).join(' ')} failed or timed out; outcome may be uncertain`);
    if (mutation) return;
    try { return JSON.parse(result.stdout); } catch { throw Error('Invalid Azure JSON response'); }
  }
  const sameId = (a, b) => typeof a === 'string' && a.toLowerCase() === b.toLowerCase();
  const containers = value => {
    const items = value?.properties?.template?.containers;
    // Current deployment updates one ingestion container. Refuse ambiguous targets.
    if (!Array.isArray(items) || items.length !== 1 || !items[0].name || !items[0].image) throw Error('Expected exactly one named ingestion container with an image');
    return items;
  };
  const identity = value => JSON.stringify([value.id.toLowerCase(), value.name, containers(value)]);
  function app() {
    const value = az(['show'], appProjection);
    if (!sameId(value?.id, appId) || value.name !== name) throw Error('Container App identity mismatch');
    containers(value);
    return value;
  }
  function validateRevision(value) {
    if (!value?.name?.startsWith(`${name}--`) ||
        !sameId(value.id, `${appId}/revisions/${value.name}`) ||
        typeof value?.properties?.active !== 'boolean') throw Error('Revision identity or activation state is uncertain');
    containers(value);
    return value;
  }
  function revisions() {
    // CLI defaults to active-only. --all is necessary for both ownership binding
    // of old revisions and independent final verification.
    const values = az(['revision', 'list', '--all'], `[].${revisionProjection}`);
    if (!Array.isArray(values)) throw Error('Revision inventory is uncertain');
    values.forEach(validateRevision);
    if (new Set(values.map(r => r.name)).size !== values.length) throw Error('Duplicate revision identities');
    return values;
  }
  const fingerprint = value => JSON.stringify(value);
  const inventoryFingerprint = values => JSON.stringify(values.toSorted((a, b) => a.name.localeCompare(b.name)));
  const beforeApp = app();
  const before = revisions();
  if (beforeApp.properties.provisioningState !== 'Succeeded' || before.length === 0) throw Error('Deployment baseline is not established');
  if (before.some(r => r.name === ownName)) throw Error('Deployment revision already exists; refusing ambiguous retry');
  if (fingerprint(beforeApp) !== fingerprint(app()) || inventoryFingerprint(before) !== inventoryFingerprint(revisions())) throw Error('Concurrent change before image update; no mutation attempted');
  const paused = before.every(r => !r.properties.active);
  const known = new Map(before.map(r => [r.name, identity(r)]));
  const expectedNames = new Set([...known.keys(), ownName]);
  const errors = [];
  console.log(`Verified ingestion baseline: ${paused ? 'paused (zero active revisions)' : 'enabled'}`);
  try {
    az(['update', '--image', image, '--revision-suffix', suffix], null, true);
  } catch (error) { errors.push(error.message); }

  // Run even after an uncertain update result. Never blanket-stop revisions:
  // only preobserved immutable identities or this run's suffix + exact image.
  let finalApp;
  let finalRevisions;
  try {
    const afterApp = app();
    const ownedConfig = value => value.properties.template.revisionSuffix === suffix &&
      value.properties.latestRevisionName === ownName && containers(value)[0].image === image &&
      containers(value)[0].name === containers(beforeApp)[0].name;
    const configIsExpected = ownedConfig(afterApp);
    const originalConfig = fingerprint(afterApp) === fingerprint(beforeApp);
    if (!configIsExpected && !originalConfig) errors.push('Concurrent or unexpected configured image/revision; baseline cleanup withheld');
    if (!configIsExpected) errors.push('Requested configured image and revision were not verified');
    if (afterApp.properties.provisioningState !== 'Succeeded') errors.push('Container App provisioning is not complete');
    let stableInactive = false;
    let confirmedStable = false;
    for (let attempt = 0; attempt < 4; attempt++) {
      const inventory = revisions();
      if (inventory.some(r => r.properties.active)) stableInactive = false;
      const own = inventory.find(r => r.name === ownName);
      const ownMatches = own && containers(own)[0].name === containers(beforeApp)[0].name && containers(own)[0].image === image;
      if (!ownMatches) errors.push('Requested revision image identity is missing or unexpected');
      const unexpected = inventory.filter(r => r.name !== ownName && !known.has(r.name));
      if (unexpected.length) errors.push('Unexpected revision detected; it will not be deactivated');
      for (const observed of inventory) {
        if (!paused || !observed.properties.active) continue;
        const isOwn = observed.name === ownName && ownMatches;
        const isKnown = known.get(observed.name) === identity(observed) && (configIsExpected || originalConfig);
        if (!isOwn && !isKnown) { errors.push('Active revision is not an authorized cleanup target'); continue; }
        try {
          const currentApp = app();
          if (fingerprint(currentApp) !== fingerprint(afterApp)) throw Error('Concurrent application change; cleanup withheld');
          const current = validateRevision(az(['revision', 'show', '--revision', observed.name], revisionProjection));
          if (identity(current) !== identity(observed)) throw Error('Revision identity changed before cleanup');
          if (current.properties.active) az(['revision', 'deactivate', '--revision', current.name], null, true);
        } catch (error) { errors.push(error.message); }
      }
      finalApp = app();
      finalRevisions = revisions();
      if (fingerprint(finalApp) !== fingerprint(afterApp)) errors.push('Concurrent application change during final verification');
      // Validate the complete inventory, including inactive identities, rather
      // than trusting a successful deactivate response or running replica count.
      const unchanged = finalRevisions.length === expectedNames.size &&
        finalRevisions.every(r => expectedNames.has(r.name) && (r.name === ownName
        ? ownMatches && identity(r) === identity(own)
        : known.get(r.name) === identity(r)));
      if (!unchanged) errors.push('Final revision inventory is incomplete or identities changed');
      if (!unchanged) errors.push('Final revision inventory contains an unexpected identity');
      const inactive = finalRevisions.every(r => !r.properties.active);
      const valid = unchanged && (!paused || inactive);
      if (valid && (!paused || stableInactive)) { confirmedStable = true; break; }
      stableInactive = valid && inactive;
      if (attempt < 3) await sleep(1000);
    }
    if (paused && finalRevisions.some(r => r.properties.active)) errors.push('Final ingestion inventory is not paused');
    if (paused && !confirmedStable) errors.push('Two consecutive inactive inventories were not established');
    if (!paused && !finalRevisions.some(r => r.name === ownName && r.properties.active)) errors.push('Enabled deployment did not establish an active requested revision');
    console.log(JSON.stringify({ paused, configuredImage: containers(finalApp)[0].image,
      revisions: finalRevisions.map(r => ({ name: r.name, id: r.id, image: containers(r)[0].image, active: r.properties.active })) }));
  } catch (error) { errors.push(`Final revision state could not be verified: ${error.message}`); }
  if (errors.length) throw Error([...new Set(errors)].join('; '));
  console.log(`Verified ingestion deployment: ${paused ? 'zero active revisions' : 'requested revision active'}`);
}

deploy().catch(error => { console.error(`::error::${error.message}`); process.exitCode = 1; });
