import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { api } from '../services/api';
import { driverDisclosure } from '../services/driverDisclosure';
import { PersonalDriverCard } from './DriverDisclosure';

afterEach(() => {
  localStorage.clear();
  driverDisclosure.setOwner(null);
  vi.restoreAllMocks();
});

it('clears the own Driver before durable withdrawal resolves and reports no erasure claim', async () => {
  driverDisclosure.setOwner('aaaaaaaa-3760-4000-8000-000000000001');
  vi.spyOn(api, 'getScopedDriverPersonal').mockResolvedValue({
    driverName: 'Authorized Driver',
    officialBestLapSeconds: 89.9,
    iRating: 1500,
    provenance: 'synthetic',
  });
  let complete!: (value: Awaited<ReturnType<typeof api.withdrawDriver>>) => void;
  const withdrawal = vi.spyOn(api, 'withdrawDriver').mockImplementation(
    () =>
      new Promise(resolve => {
        complete = resolve;
      })
  );
  render(<PersonalDriverCard />);
  await screen.findByText('Authorized Driver');
  fireEvent.click(screen.getByRole('button', { name: 'Withdraw personal consent' }));
  expect(screen.queryByText('Authorized Driver')).not.toBeInTheDocument();
  const request = withdrawal.mock.calls[0][0];
  vi.spyOn(api, 'getScopedDriverPersonal').mockRejectedValue(new Error('unavailable'));
  await act(() => {
    complete({
      ...request,
      withdrawalRecorded: true,
      writersDrained: false,
      originalLossAt: '2026-10-08T00:00:00Z',
      liveErasureVerified: false,
      backupExpiryVerified: false,
    });
    return Promise.resolve();
  });
  await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Withdrawal recorded'));
  expect(screen.getByRole('status')).toHaveTextContent(
    'Live erasure and backup expiry have not been verified'
  );
  expect(screen.queryByText('Authorized Driver')).not.toBeInTheDocument();
});
