import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import HomePage from '../HomePage';

describe('HomePage', () => {
  it('renders the ApexRacers heading and description', () => {
    render(<HomePage />);
    expect(screen.getByRole('heading', { name: /apexracers/i })).toBeInTheDocument();
    expect(screen.getByText(/percentile/i)).toBeInTheDocument();
  });
});
