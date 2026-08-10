import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import ResourceView from './ResourceView';

describe('ResourceView', () => {
  it('owns the loading and error presentations', () => {
    const { rerender } = render(<ResourceView resource={{ status: 'loading' }} />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();

    rerender(<ResourceView resource={{ status: 'error', message: 'No connection' }} />);
    expect(screen.getByText('No connection')).toBeInTheDocument();
  });

  it('renders the shared not-linked prompt', () => {
    render(
      <MemoryRouter>
        <ResourceView
          resource={{ status: 'not-linked' }}
          notLinkedReason="Progression needs an iRacing identity."
        />
      </MemoryRouter>
    );

    expect(screen.getByText(/Progression needs an iRacing identity/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings');
  });

  it('renders nothing for loaded data', () => {
    const { container } = render(<ResourceView resource={{ status: 'ok', data: 42 }} />);
    expect(container).toBeEmptyDOMElement();
  });
});
