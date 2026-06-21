import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import SupportPage from '../SupportPage';

describe('SupportPage', () => {
  it('renders the heading and the four support cards', () => {
    render(<SupportPage />);
    expect(screen.getByRole('heading', { level: 1, name: /help & support/i })).toBeInTheDocument();
    expect(screen.getByText('Help center')).toBeInTheDocument();
    expect(screen.getByText('Contact us')).toBeInTheDocument();
    expect(screen.getByText('API status')).toBeInTheDocument();
    expect(screen.getByText('Changelog')).toBeInTheDocument();
  });

  it('links Contact us to a mailto and API status to the external status page', () => {
    render(<SupportPage />);
    expect(screen.getByRole('link', { name: /contact us/i })).toHaveAttribute(
      'href',
      'mailto:support@apexracers.gg'
    );
    const status = screen.getByRole('link', { name: /api status/i });
    expect(status).toHaveAttribute('href', 'https://apex-racers.betteruptime.com/');
    expect(status).toHaveAttribute('target', '_blank');
  });

  it('renders informational cards (no link) for Help center and Changelog', () => {
    render(<SupportPage />);
    expect(screen.queryByRole('link', { name: /help center/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /changelog/i })).not.toBeInTheDocument();
  });
});
