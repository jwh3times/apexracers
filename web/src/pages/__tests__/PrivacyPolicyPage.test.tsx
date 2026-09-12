import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import PrivacyPolicyPage from '../PrivacyPolicyPage';

describe('PrivacyPolicyPage', () => {
  it('renders the heading and every policy section in order', () => {
    render(<PrivacyPolicyPage />);
    expect(screen.getByRole('heading', { level: 1, name: /privacy policy/i })).toBeInTheDocument();
    const sections = screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent);
    expect(sections).toEqual([
      '1. Information We Collect',
      '2. Information About Other Drivers',
      '3. How We Use Your Information',
      '4. Service Providers',
      '5. Request Logs',
      '6. Data Retention and Deletion',
      '7. Security',
      '8. Cookies and Browser Storage',
      '9. Contact',
    ]);
  });

  it('names the processors that receive personal data instead of denying any transfer', () => {
    const { container } = render(<PrivacyPolicyPage />);
    const text = container.textContent ?? '';
    expect(text).not.toMatch(/do not sell, trade, or otherwise transfer/i);
    expect(text).toMatch(/Azure Communication Services/);
    expect(text).toMatch(/Application Insights/);
    expect(text).toMatch(/directly from iRacing/);
  });

  it('states the retention periods that bound what deletion cannot reach', () => {
    const { container } = render(<PrivacyPolicyPage />);
    const text = container.textContent ?? '';
    expect(text).toMatch(/kept for 90 days/);
    expect(text).toMatch(/backups are kept for 7 days/i);
  });
});
