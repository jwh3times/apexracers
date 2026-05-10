import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../App';

describe('App', () => {
  it('renders navigation links on the home route', () => {
    render(<App />);
    // Sidebar and top-nav both render links — use getAllByRole
    expect(screen.getAllByRole('link', { name: 'Home' }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: 'Browse Series' }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: 'Recommendations' }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: 'My Laps' }).length).toBeGreaterThan(0);
  });

  it('renders the home page content by default', () => {
    render(<App />);
    expect(
      screen.getByRole('heading', { name: /find the car that best fits your pace/i })
    ).toBeInTheDocument();
  });
});
