import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import PrivacyPolicyPage from '../PrivacyPolicyPage';

describe('PrivacyPolicyPage', () => {
  it('renders the heading and policy sections', () => {
    render(<PrivacyPolicyPage />);
    expect(screen.getByRole('heading', { level: 1, name: /privacy policy/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /information we collect/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /data retention/i })).toBeInTheDocument();
  });
});
