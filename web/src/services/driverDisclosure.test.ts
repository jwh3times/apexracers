import { afterEach, describe, expect, it } from 'vitest';
import { createDriverDisclosure } from './driverDisclosure';

afterEach(() => localStorage.clear());

describe('Driver display withdrawal veto', () => {
  it('persists only a pending veto across reload and never restores display from that marker', () => {
    const first = createDriverDisclosure();
    first.setOwner('synthetic-user');
    first.beginWithdrawal('aaaaaaaa-3760-4000-8000-000000000001', 'personal');
    expect(first.canRead()).toBe(false);
    const reloaded = createDriverDisclosure();
    reloaded.setOwner('synthetic-user');
    expect(reloaded.canRead()).toBe(false);
    expect(reloaded.pendingWithdrawal()).toEqual({
      operationId: 'aaaaaaaa-3760-4000-8000-000000000001',
      scope: 'personal',
    });
    reloaded.finishWithdrawal('aaaaaaaa-3760-4000-8000-000000000001');
    expect(reloaded.pendingWithdrawal()).toBeNull();
    // Removing a veto permits a new protected request, never reuse of previously delivered data.
    expect(reloaded.canRead()).toBe(true);
  });
});
