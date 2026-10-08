import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const appId = '/subscriptions/test-sub/resourceGroups/test-group/providers/Microsoft.App/containerApps/worker';
const oldImage = 'registry.example/ingestion:old';
const image = 'registry.example/apexracers-ingestion:new';
const revision = (suffix, image, active = false) => ({
  id: `${appId}/revisions/worker--${suffix}`, name: `worker--${suffix}`,
  properties: { active, replicas: 0, template: { containers: [{ name: 'worker', image }] } },
});

// Execute the checked-in workflow step, including its actual helper, against an
// az process double. Shapes and --all behavior follow Azure CLI's wire contract.
function runWorkflow(scenario = 'paused') {
  const dir = mkdtempSync(join(tmpdir(), 'ingestion-deploy-'));
  const statePath = join(dir, 'state.json');
  const initial = {
    scenario, calls: [], lists: 0, shows: 0, deactivations: 0,
    app: { id: appId, name: 'worker', properties: {
      provisioningState: 'Succeeded', latestRevisionName: 'worker--old',
      template: { revisionSuffix: 'old', containers: [{ name: 'worker', image: oldImage }] },
    } },
    revisions: [revision('old', oldImage, scenario === 'enabled')],
  };
  writeFileSync(statePath, JSON.stringify(initial));
  writeFileSync(join(dir, 'az'), `#!/usr/bin/env node
const fs = require('node:fs');
const path = process.env.DEPLOY_TEST_STATE;
const state = JSON.parse(fs.readFileSync(path));
const args = process.argv.slice(2);
const value = flag => args[args.indexOf(flag) + 1];
state.calls.push(args);
let result = null, status = 0;
if (args[0] !== 'containerapp') throw Error('Unexpected command');
if (args[1] === 'show') {
  state.shows++;
  if (state.scenario === 'preflight-drift' && state.shows === 2) state.revisions[0].properties.active = true;
  if (state.scenario === 'wrong-app') state.app.id = '${appId}-replaced';
  result = state.app;
} else if (args[1] === 'update') {
  const suffix = args.includes('--revision-suffix') ? value('--revision-suffix') : 'platform-new';
  const name = 'worker--' + suffix;
  state.app.properties.template = { revisionSuffix: suffix, containers: [{name:'worker', image:value('--image')}] };
  state.app.properties.latestRevisionName = name;
  state.revisions[0].properties.active = true;
  state.revisions.push({ id: '${appId}/revisions/' + name, name, properties: {active:true, template: state.app.properties.template} });
  if (state.scenario === 'foreign-revision') state.revisions.push({id:'${appId}/revisions/worker--foreign', name:'worker--foreign', properties:{active:true,template:{containers:[{name:'worker',image:'registry.example/ingestion:foreign'}]}}});
  if (state.scenario === 'concurrent-image') {
    state.app.properties.template = { revisionSuffix:'foreign', containers:[{name:'worker',image:'registry.example/ingestion:foreign'}] };
    state.app.properties.latestRevisionName = 'worker--foreign';
  }
  result = state.app;
  if (state.scenario === 'uncertain') status = 1;
} else if (args[1] === 'revision' && args[2] === 'list') {
  state.lists++;
  if (state.scenario === 'unknown-state' && state.lists === 1) state.revisions[0].properties.active = null;
  if (state.scenario === 'empty-baseline' && state.lists === 1) state.revisions = [];
  if (state.scenario === 'identity-drift' && state.lists === 3) state.revisions[0].properties.template.containers[0].image = 'registry.example/ingestion:foreign';
  if (state.scenario === 'final-read-failure' && state.lists >= 4) status = 1;
  result = args.includes('--all') ? state.revisions : state.revisions.filter(r => r.properties.active);
  if (state.lists === 6 && state.scenario === 'empty-final-inventory') result = [];
  if (state.lists === 6 && state.scenario === 'missing-owned-final-revision') result = state.revisions.filter(r => r.name === 'worker--old');
  if (state.lists === 6 && state.scenario === 'missing-baseline-final-revision') result = state.revisions.filter(r => r.name !== 'worker--old');
} else if (args[1] === 'revision' && args[2] === 'show') {
  if (state.scenario === 'cleanup-identity-drift') state.revisions.find(r => r.name === value('--revision')).id += '-foreign';
  result = state.revisions.find(r => r.name === value('--revision'));
} else if (args[1] === 'revision' && args[2] === 'deactivate') {
  state.deactivations++;
  if (state.scenario !== 'deactivate-noop' && (state.scenario !== 'late-inactive' || state.deactivations >= 7)) state.revisions.find(r => r.name === value('--revision')).properties.active = false;
  if (state.scenario === 'deactivate-uncertain') status = 1;
} else throw Error('Unexpected command: ' + args.join(' '));
fs.writeFileSync(path, JSON.stringify(state));
if (args.includes('--output') && value('--output') === 'none') process.exit(status);
process.stdout.write(JSON.stringify(result));
process.exit(status);
`, { mode: 0o755 });
  try {
    const workflow = readFileSync(new URL('../.github/workflows/deploy.yml', import.meta.url), 'utf8');
    const step = workflow.match(/      - name: Update Container App image\n        run: \|\n((?:          .*\n?)+)/)?.[1];
    assert.ok(step, 'Image update step must remain covered by this harness');
    const replacements = {
      'secrets.INGESTION_APP_NAME': 'worker', 'secrets.RESOURCE_GROUP': 'test-group',
      'env.REGISTRY': 'registry.example', 'github.sha': 'new',
      'vars.AZURE_SUBSCRIPTION_ID': 'test-sub', 'github.run_id': '123', 'github.run_attempt': '1',
    };
    const interpolate = value => value.replace(/\$\{\{ (.*?) \}\}/g, (_, key) => {
      assert.ok(key in replacements, `Unexpected workflow expression: ${key}`);
      return replacements[key];
    });
    const script = interpolate(step.replace(/^          /gm, ''));
    const envBlock = workflow.split('      - name: Update Container App image\n')[1].split('        env:\n')[1];
    const workflowEnv = Object.fromEntries([...envBlock.matchAll(/^          ([A-Z_]+): (.*)$/gm)].map(([, key, value]) => [key, interpolate(value)]));
    const result = spawnSync('bash', ['-e', '-c', script], {
      cwd: new URL('..', import.meta.url), encoding: 'utf8',
      env: { ...process.env, PATH: `${dir}:${process.env.PATH}`, DEPLOY_TEST_STATE: statePath,
        ...workflowEnv },
    });
    return { ...result, state: JSON.parse(readFileSync(statePath)) };
  } finally { rmSync(dir, { recursive: true, force: true }); }
}

test('paused deployment leaves zero active revisions after new and old activation', () => {
  const result = runWorkflow();
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(result.state.revisions.filter(r => r.properties.active), [], 'Deployment resumed paused ingestion');
  assert.equal(result.state.app.properties.template.containers[0].image, image);
  const stops = result.state.calls.filter(c => c[2] === 'deactivate');
  assert.equal(stops.length, 2, 'Both observed older and owned new revision need cleanup');
  assert.ok(result.state.calls.filter(c => c[2] === 'list').every(c => c.includes('--all')));
  assert.ok(result.state.calls.every(c => c.includes('--subscription')));
  assert.ok(result.state.lists >= 5, 'Independent final inventories are required');
});

test('enabled deployment preserves ordinary revision activation including scale-to-zero', () => {
  const result = runWorkflow('enabled');
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.state.calls.filter(c => c[2] === 'deactivate').length, 0);
  assert.equal(result.state.revisions.find(r => r.name === 'worker--deploy-123-1').properties.active, true);
});

test('uncertain image update still restores pause and reports failure', () => {
  const result = runWorkflow('uncertain');
  assert.equal(result.status, 1);
  assert.deepEqual(result.state.revisions.filter(r => r.properties.active), []);
  assert.match(result.stderr, /outcome may be uncertain/);
});

for (const scenario of ['preflight-drift', 'wrong-app', 'unknown-state', 'empty-baseline']) {
  test(`${scenario} refuses image mutation`, () => {
    const result = runWorkflow(scenario);
    assert.equal(result.status, 1);
    assert.equal(result.state.calls.some(c => c[1] === 'update'), false);
  });
}

for (const scenario of ['foreign-revision', 'identity-drift']) {
  test(`${scenario} restores owned targets but does not stop unexpected identities`, () => {
    const result = runWorkflow(scenario);
    assert.equal(result.status, 1);
    const stops = result.state.calls.filter(c => c[2] === 'deactivate').map(c => c[c.indexOf('--revision') + 1]);
    assert.ok(stops.includes('worker--deploy-123-1'));
    assert.ok(!stops.includes(scenario === 'identity-drift' ? 'worker--old' : 'worker--foreign'));
    assert.match(result.stderr, /unexpected|authorized cleanup/);
  });
}

for (const scenario of ['concurrent-image', 'cleanup-identity-drift', 'deactivate-noop', 'final-read-failure', 'late-inactive']) {
  test(`${scenario} cannot report successful pause preservation`, () => {
    const result = runWorkflow(scenario);
    assert.equal(result.status, 1);
    if (scenario === 'concurrent-image') {
      const stops = result.state.calls.filter(c => c[2] === 'deactivate').map(c => c[c.indexOf('--revision') + 1]);
      assert.deepEqual(stops, ['worker--deploy-123-1'], 'Only this run owned target is eligible during concurrent config changes');
      assert.equal(result.state.revisions.find(r => r.name === 'worker--old').properties.active, true);
    }
    if (scenario === 'cleanup-identity-drift') assert.equal(result.state.calls.some(c => c[2] === 'deactivate'), false);
    if (scenario === 'late-inactive') {
      assert.deepEqual(result.state.revisions.filter(r => r.properties.active), []);
      assert.match(result.stderr, /Two consecutive inactive inventories/);
    }
    assert.ok(result.stderr.includes('::error::'));
  });
}

test('uncertain deactivation remains failed despite final zero active verification', () => {
  const result = runWorkflow('deactivate-uncertain');
  assert.equal(result.status, 1);
  assert.deepEqual(result.state.revisions.filter(r => r.properties.active), []);
});

for (const scenario of ['empty-final-inventory', 'missing-owned-final-revision', 'missing-baseline-final-revision']) {
  test(`${scenario} cannot turn an incomplete inventory into a verified pause`, () => {
    const result = runWorkflow(scenario);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Final revision inventory is incomplete/);
    assert.doesNotMatch(result.stdout, /Verified ingestion deployment:/);
  });
}
