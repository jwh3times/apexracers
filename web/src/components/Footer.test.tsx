import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import Footer from './Footer';

describe('Footer', () => {
  function renderFooter() {
    return render(
      <MemoryRouter>
        <Footer />
      </MemoryRouter>
    );
  }

  it('renders the brand and legal links', () => {
    renderFooter();
    expect(screen.getByText('ApexRacers')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /terms of service/i })).toHaveAttribute(
      'href',
      '/terms'
    );
    expect(screen.getByRole('link', { name: /privacy policy/i })).toHaveAttribute(
      'href',
      '/privacy'
    );
  });

  it('renders the external API status link and current-year copyright', () => {
    renderFooter();
    expect(screen.getByRole('link', { name: /api status/i })).toHaveAttribute(
      'href',
      'https://apex-racers.betteruptime.com/'
    );
    expect(
      screen.getByText(new RegExp(`© ${new Date().getFullYear()} ApexRacers`))
    ).toBeInTheDocument();
  });
});
