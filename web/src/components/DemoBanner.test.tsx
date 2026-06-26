import { render, screen } from '@testing-library/react';
import { vi, describe, it, expect } from 'vitest';
import DemoBanner from './DemoBanner';

let demoOn = false;
vi.mock('../context/FeatureFlagContext', () => ({
  useFeatureFlag: (key: string) => (key === 'iracing-demo' ? demoOn : false),
}));

describe('DemoBanner', () => {
  it('renders the demo notice when iracing-demo is on', () => {
    demoOn = true;
    render(<DemoBanner />);
    expect(screen.getByText(/demo data/i)).toBeInTheDocument();
    expect(screen.getByText(/synthetic/i)).toBeInTheDocument();
  });

  it('renders nothing when iracing-demo is off', () => {
    demoOn = false;
    const { container } = render(<DemoBanner />);
    expect(container).toBeEmptyDOMElement();
  });
});
