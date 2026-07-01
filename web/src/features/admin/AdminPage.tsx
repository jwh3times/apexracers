import { useEffect, useState } from 'react';
import { api } from '../../services/api';
import type { AdminUser, FeatureFlag } from '../../services/api';

const ROLES = ['Standard', 'Beta', 'Alpha', 'Admin'] as const;
type Role = (typeof ROLES)[number];

// ── Users tab ─────────────────────────────────────────────────────────────────

function UsersTab() {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [pendingRoles, setPendingRoles] = useState<Record<string, Role>>({});
  const [saving, setSaving] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .getAdminUsers()
      .then(setUsers)
      .catch(() => setError('Failed to load users.'))
      .finally(() => setLoading(false));
  }, []);

  function handleRoleChange(userId: string, role: Role) {
    setPendingRoles(prev => ({ ...prev, [userId]: role }));
  }

  async function saveRole(user: AdminUser) {
    const newRole = pendingRoles[user.userId];
    if (!newRole || newRole === user.role) return;

    setSaving(prev => new Set(prev).add(user.userId));
    try {
      const updated = await api.setAdminUserRole(user.userId, newRole);
      setUsers(prev => prev.map(u => (u.userId === user.userId ? updated : u)));
      setPendingRoles(prev => {
        const next = { ...prev };
        delete next[user.userId];
        return next;
      });
    } catch {
      setError(`Failed to update role for ${user.email}.`);
    } finally {
      setSaving(prev => {
        const next = new Set(prev);
        next.delete(user.userId);
        return next;
      });
    }
  }

  if (loading)
    return (
      <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
        Loading users…
      </p>
    );

  return (
    <div className="glass-panel rounded-xl overflow-hidden border border-line-2">
      {error && <p className="px-6 py-3 text-error font-body-sm text-body-sm">{error}</p>}
      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-surface-container/50 border-b border-line-2">
              <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">EMAIL</th>
              <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                DISPLAY NAME
              </th>
              <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">ROLE</th>
              <th className="p-4 font-label-caps text-label-caps text-on-surface-variant"></th>
            </tr>
          </thead>
          <tbody>
            {users.map(user => {
              const isAdmin = user.role === 'Admin';
              const pending = pendingRoles[user.userId];
              const effectiveRole = (pending ?? user.role) as Role;
              const isDirty = !isAdmin && pending !== undefined && pending !== user.role;
              const isSaving = saving.has(user.userId);
              return (
                <tr
                  key={user.userId}
                  className="border-b border-surface-container-high hover:bg-surface-container-highest transition-colors last:border-b-0"
                >
                  <td className="p-4 font-body-sm text-body-sm text-on-surface">{user.email}</td>
                  <td className="p-4 font-body-sm text-body-sm text-on-surface-variant">
                    {user.displayName}
                  </td>
                  <td className="p-4">
                    {isAdmin ? (
                      <span
                        className="font-body-sm text-body-sm text-on-surface-variant px-3 py-1.5 inline-block"
                        title="Managed via Key Vault"
                      >
                        Admin
                      </span>
                    ) : (
                      <select
                        value={effectiveRole}
                        onChange={e => handleRoleChange(user.userId, e.target.value as Role)}
                        className="min-w-28 bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-1.5 focus:outline-none focus:border-primary-fixed-dim transition-colors"
                      >
                        {ROLES.filter(r => r !== 'Admin').map(r => (
                          <option key={r} value={r}>
                            {r}
                          </option>
                        ))}
                      </select>
                    )}
                  </td>
                  <td className="p-4">
                    {isDirty && (
                      <button
                        onClick={() => saveRole(user)}
                        disabled={isSaving}
                        className="bg-primary-fixed-dim text-on-primary-fixed-variant px-3 py-1.5 rounded font-body-sm text-body-sm hover:opacity-90 transition-opacity disabled:opacity-50"
                      >
                        {isSaving ? 'Saving…' : 'Save'}
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Feature flags tab ─────────────────────────────────────────────────────────

const BLANK_FLAG = {
  key: '',
  name: '',
  description: '',
  isEnabled: true,
  minimumRole: 'Standard' as Role,
};

function FlagsTab() {
  const [flags, setFlags] = useState<FeatureFlag[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [createForm, setCreateForm] = useState({ ...BLANK_FLAG });
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editForm, setEditForm] = useState({
    name: '',
    description: '',
    isEnabled: true,
    minimumRole: 'Standard' as Role,
  });
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api
      .getAdminFeatureFlags()
      .then(setFlags)
      .catch(() => setError('Failed to load feature flags.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setCreating(true);
    setError(null);
    try {
      const created = await api.createFeatureFlag({
        ...createForm,
        description: createForm.description || null,
      });
      setFlags(prev => [...prev, created]);
      setCreateForm({ ...BLANK_FLAG });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create flag.');
    } finally {
      setCreating(false);
    }
  }

  function startEdit(flag: FeatureFlag) {
    setEditingId(flag.id);
    setEditForm({
      name: flag.name,
      description: flag.description ?? '',
      isEnabled: flag.isEnabled,
      minimumRole: flag.minimumRole as Role,
    });
  }

  async function handleUpdate(e: React.FormEvent) {
    e.preventDefault();
    if (editingId === null) return;
    setSaving(true);
    setError(null);
    try {
      const updated = await api.updateFeatureFlag(editingId, {
        ...editForm,
        description: editForm.description || null,
      });
      setFlags(prev => prev.map(f => (f.id === editingId ? updated : f)));
      setEditingId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update flag.');
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    if (!confirm('Delete this feature flag?')) return;
    try {
      await api.deleteFeatureFlag(id);
      setFlags(prev => prev.filter(f => f.id !== id));
    } catch {
      setError('Failed to delete flag.');
    }
  }

  if (loading)
    return (
      <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
        Loading flags…
      </p>
    );

  return (
    <div className="flex flex-col gap-6">
      {error && <p className="text-error font-body-sm text-body-sm">{error}</p>}

      {/* Create form */}
      <div className="glass-panel rounded-xl border border-line-2 p-6">
        <h3 className="font-headline-sm text-headline-sm text-on-surface mb-4">Create Flag</h3>
        <form onSubmit={handleCreate} className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <input
            required
            placeholder="flag.key"
            value={createForm.key}
            onChange={e => setCreateForm(p => ({ ...p, key: e.target.value }))}
            className="bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim transition-colors"
          />
          <input
            required
            placeholder="Display name"
            value={createForm.name}
            onChange={e => setCreateForm(p => ({ ...p, name: e.target.value }))}
            className="bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim transition-colors"
          />
          <input
            placeholder="Description (optional)"
            value={createForm.description}
            onChange={e => setCreateForm(p => ({ ...p, description: e.target.value }))}
            className="bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim transition-colors"
          />
          <div className="flex items-center gap-3">
            <select
              value={createForm.minimumRole}
              onChange={e => setCreateForm(p => ({ ...p, minimumRole: e.target.value as Role }))}
              className="w-36 bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim transition-colors"
            >
              {ROLES.map(r => (
                <option key={r} value={r}>
                  {r}+
                </option>
              ))}
            </select>
            <label className="flex items-center gap-2 font-body-sm text-body-sm text-on-surface-variant cursor-pointer">
              <input
                type="checkbox"
                checked={createForm.isEnabled}
                onChange={e => setCreateForm(p => ({ ...p, isEnabled: e.target.checked }))}
                className="w-4 h-4"
              />
              Enabled
            </label>
          </div>
          <div className="md:col-span-2">
            <button
              type="submit"
              disabled={creating}
              className="bg-primary-fixed-dim text-on-primary-fixed-variant px-4 py-2 rounded font-body-sm text-body-sm hover:opacity-90 transition-opacity disabled:opacity-50"
            >
              {creating ? 'Creating…' : 'Create Flag'}
            </button>
          </div>
        </form>
      </div>

      {/* Flags list */}
      {flags.length === 0 ? (
        <p className="font-body-sm text-body-sm text-on-surface-variant">No feature flags yet.</p>
      ) : (
        <div className="glass-panel rounded-xl overflow-hidden border border-line-2">
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-surface-container/50 border-b border-line-2">
                  <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                    KEY
                  </th>
                  <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                    NAME / DESCRIPTION
                  </th>
                  <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                    MIN ROLE
                  </th>
                  <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                    ENABLED
                  </th>
                  <th className="p-4 font-label-caps text-label-caps text-on-surface-variant"></th>
                </tr>
              </thead>
              <tbody>
                {flags.map(flag =>
                  editingId === flag.id ? (
                    <tr
                      key={flag.id}
                      className="border-b border-surface-container-high bg-surface-container/30"
                    >
                      <td className="p-4 font-body-sm text-body-sm text-on-surface-variant">
                        {flag.key}
                      </td>
                      <td className="p-4" colSpan={2}>
                        <form
                          onSubmit={handleUpdate}
                          id={`edit-${flag.id}`}
                          className="flex flex-wrap gap-2"
                        >
                          <input
                            required
                            value={editForm.name}
                            onChange={e => setEditForm(p => ({ ...p, name: e.target.value }))}
                            className="bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-1.5 focus:outline-none focus:border-primary-fixed-dim transition-colors w-40"
                          />
                          <input
                            value={editForm.description}
                            onChange={e =>
                              setEditForm(p => ({ ...p, description: e.target.value }))
                            }
                            placeholder="Description"
                            className="bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-1.5 focus:outline-none focus:border-primary-fixed-dim transition-colors flex-1"
                          />
                          <select
                            value={editForm.minimumRole}
                            onChange={e =>
                              setEditForm(p => ({ ...p, minimumRole: e.target.value as Role }))
                            }
                            className="w-36 bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-1.5 focus:outline-none focus:border-primary-fixed-dim transition-colors"
                          >
                            {ROLES.map(r => (
                              <option key={r} value={r}>
                                {r}+
                              </option>
                            ))}
                          </select>
                        </form>
                      </td>
                      <td className="p-4">
                        <label className="flex items-center gap-2 font-body-sm text-body-sm text-on-surface-variant cursor-pointer">
                          <input
                            type="checkbox"
                            form={`edit-${flag.id}`}
                            checked={editForm.isEnabled}
                            onChange={e =>
                              setEditForm(p => ({ ...p, isEnabled: e.target.checked }))
                            }
                            className="w-4 h-4"
                          />
                          Enabled
                        </label>
                      </td>
                      <td className="p-4 flex gap-2">
                        <button
                          type="submit"
                          form={`edit-${flag.id}`}
                          disabled={saving}
                          className="bg-primary-fixed-dim text-on-primary-fixed-variant px-3 py-1.5 rounded font-body-sm text-body-sm hover:opacity-90 transition-opacity disabled:opacity-50"
                        >
                          {saving ? 'Saving…' : 'Save'}
                        </button>
                        <button
                          type="button"
                          onClick={() => setEditingId(null)}
                          className="px-3 py-1.5 rounded font-body-sm text-body-sm text-on-surface-variant hover:text-on-surface transition-colors"
                        >
                          Cancel
                        </button>
                      </td>
                    </tr>
                  ) : (
                    <tr
                      key={flag.id}
                      className="border-b border-surface-container-high hover:bg-surface-container-highest transition-colors last:border-b-0"
                    >
                      <td className="p-4 font-body-sm text-body-sm text-primary-fixed-dim font-mono">
                        {flag.key}
                      </td>
                      <td className="p-4">
                        <p className="font-body-sm text-body-sm text-on-surface">{flag.name}</p>
                        {flag.description && (
                          <p className="font-body-sm text-[12px] text-on-surface-variant mt-0.5">
                            {flag.description}
                          </p>
                        )}
                      </td>
                      <td className="p-4">
                        <span className="bg-surface-container-high text-on-surface-variant font-label-caps text-label-caps px-2 py-1 rounded">
                          {flag.minimumRole}+
                        </span>
                      </td>
                      <td className="p-4">
                        <span
                          className={`font-label-caps text-label-caps ${flag.isEnabled ? 'text-primary-fixed-dim' : 'text-on-surface-variant'}`}
                        >
                          {flag.isEnabled ? 'Yes' : 'No'}
                        </span>
                      </td>
                      <td className="p-4 flex gap-2">
                        <button
                          onClick={() => startEdit(flag)}
                          className="font-body-sm text-body-sm text-on-surface-variant hover:text-on-surface transition-colors"
                        >
                          Edit
                        </button>
                        <button
                          onClick={() => handleDelete(flag.id)}
                          className="font-body-sm text-body-sm text-on-surface-variant hover:text-error transition-colors"
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  )
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

type Tab = 'users' | 'flags';

export default function AdminPage() {
  const [tab, setTab] = useState<Tab>('users');

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      <div>
        <h1 className="font-headline-md text-[48px] leading-none font-extrabold tracking-tighter text-on-surface mb-2">
          Admin Panel
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant">
          Manage users, roles, and feature flags.
        </p>
      </div>

      <div className="flex gap-1 border-b border-line-2 pb-0">
        {(['users', 'flags'] as Tab[]).map(t => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2.5 font-body-sm text-body-sm rounded-t transition-colors capitalize ${
              tab === t
                ? 'bg-surface-container text-primary-fixed-dim border-b-2 border-primary-fixed-dim'
                : 'text-on-surface-variant hover:text-on-surface'
            }`}
          >
            {t === 'flags' ? 'Feature Flags' : 'Users'}
          </button>
        ))}
      </div>

      {tab === 'users' && <UsersTab />}
      {tab === 'flags' && <FlagsTab />}
    </main>
  );
}
