import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, it, expect } from 'vitest';
import HomePage from '../HomePage';

describe('HomePage', () => {
  it('renders the hero heading', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    );
    expect(screen.getByRole('heading', { name: /win races/i })).toBeInTheDocument();
  });

  it('renders sign in and browse series CTAs', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    );
    expect(screen.getAllByRole('link', { name: /start free/i }).length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: /browse series/i })).toBeInTheDocument();
  });

  it('renders the features section', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    );
    expect(screen.getByText('Performance Percentiles')).toBeInTheDocument();
    expect(screen.getByText('Edge Recommendations')).toBeInTheDocument();
    expect(screen.getByText('Telemetry, Decoded')).toBeInTheDocument();
  });

  it('renders percentile mention in hero body copy', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    );
    expect(screen.getAllByText(/percentile/i).length).toBeGreaterThan(0);
  });
});
