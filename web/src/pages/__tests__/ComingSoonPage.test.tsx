import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import ComingSoonPage from '../ComingSoonPage';

function renderPage() {
  return render(
    <MemoryRouter>
      <ComingSoonPage />
    </MemoryRouter>
  );
}

describe('ComingSoonPage', () => {
  it('shows the coming-soon headline', () => {
    renderPage();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });

  it('links back to the always-on tools', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /telemetry/i })).toHaveAttribute('href', '/telemetry');
    expect(screen.getByRole('link', { name: /my laps/i })).toHaveAttribute('href', '/my-laps');
  });
});
