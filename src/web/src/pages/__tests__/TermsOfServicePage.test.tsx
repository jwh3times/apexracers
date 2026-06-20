import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import TermsOfServicePage from '../TermsOfServicePage';

describe('TermsOfServicePage', () => {
  it('renders the heading and policy sections', () => {
    render(<TermsOfServicePage />);
    expect(
      screen.getByRole('heading', { level: 1, name: /terms of service/i })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /use of service/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /iRacing data/i })).toBeInTheDocument();
  });
});
