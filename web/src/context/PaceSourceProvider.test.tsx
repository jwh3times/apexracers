import { fireEvent, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usePaceSource } from './PaceSourceContext';
import { PaceSourceProvider } from './PaceSourceProvider';

let mockUserId: string | null = 'user-1';

vi.mock('./AuthContext', () => ({
  useAuth: () => ({ user: mockUserId ? { userId: mockUserId } : null }),
}));

function Consumer() {
  const { value, setValue, evidenceOptions } = usePaceSource();
  return (
    <>
      <span>{value.mode}</span>
      <span>{evidenceOptions.personalLapTypes?.join(',') ?? 'all-types'}</span>
      <button onClick={() => setValue({ mode: 'blend', sessions: ['Race'] })}>Blend race</button>
      <button onClick={() => setValue({ mode: 'blend', sessions: [] })}>Blend all</button>
    </>
  );
}

function renderProvider(children: ReactNode = <Consumer />) {
  return render(<PaceSourceProvider>{children}</PaceSourceProvider>);
}

describe('PaceSourceProvider', () => {
  beforeEach(() => {
    mockUserId = 'user-1';
  });

  it('defaults to official Race Lap evidence', () => {
    renderProvider();
    expect(screen.getByText('official')).toBeInTheDocument();
  });

  it('projects an empty uploaded-session selection as all Uploaded Lap types', () => {
    renderProvider();
    fireEvent.click(screen.getByRole('button', { name: 'Blend all' }));
    expect(screen.getByText('blend')).toBeInTheDocument();
    expect(screen.getByText('all-types')).toBeInTheDocument();
  });

  it('preserves the choice for the same User and gives a new User Official evidence immediately', () => {
    const view = renderProvider();
    fireEvent.click(screen.getByRole('button', { name: 'Blend race' }));
    expect(screen.getByText('blend')).toBeInTheDocument();
    expect(screen.getByText('Race')).toBeInTheDocument();

    view.rerender(
      <PaceSourceProvider>
        <Consumer />
      </PaceSourceProvider>
    );
    expect(screen.getByText('blend')).toBeInTheDocument();

    mockUserId = 'user-2';
    const modesSeenByNewUser: string[] = [];
    function NewUserConsumer() {
      const { value } = usePaceSource();
      modesSeenByNewUser.push(value.mode);
      return <span>{value.mode}</span>;
    }
    view.rerender(
      <PaceSourceProvider>
        <NewUserConsumer />
      </PaceSourceProvider>
    );

    expect(modesSeenByNewUser[0]).toBe('official');
    expect(screen.getByText('official')).toBeInTheDocument();
  });
});
