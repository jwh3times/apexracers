import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import HomePage from '../HomePage';

describe('HomePage', () => {
  it('renders the hero heading', () => {
    render(<MemoryRouter><HomePage /></MemoryRouter>);
    expect(
      screen.getByRole('heading', { name: /find the car that best fits your pace/i })
    ).toBeInTheDocument();
  });

  it('renders sign in and explore links', () => {
    render(<MemoryRouter><HomePage /></MemoryRouter>);
    expect(screen.getByRole('link', { name: /sign in with iracing/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /explore features/i })).toBeInTheDocument();
  });

  it('renders the active series section with cards', () => {
    render(<MemoryRouter><HomePage /></MemoryRouter>);
    expect(screen.getByRole('heading', { name: /active series/i })).toBeInTheDocument();
    expect(screen.getByText('VRS GT3 Sprint')).toBeInTheDocument();
    expect(screen.getByText('IMSA iRacing Series')).toBeInTheDocument();
    expect(screen.getByText('Porsche Cup')).toBeInTheDocument();
  });

  it('renders percentile mention in hero body copy', () => {
    render(<MemoryRouter><HomePage /></MemoryRouter>);
    expect(screen.getByText(/percentile/i)).toBeInTheDocument();
  });
});
