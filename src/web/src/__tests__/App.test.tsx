import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../App';

describe('App', () => {
  it('renders navigation links on the home route', () => {
    render(<App />);
    expect(screen.getByRole('link', { name: 'Home' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Series' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recommendations' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'My Laps' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Upload Telemetry' })).toBeInTheDocument();
  });

  it('renders the home page content by default', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: /apexracers/i })).toBeInTheDocument();
  });
});
