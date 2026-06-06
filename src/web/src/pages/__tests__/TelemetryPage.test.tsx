import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import TelemetryPage from '../TelemetryPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { uploadTelemetry: vi.fn(), getMyLaps: vi.fn().mockResolvedValue([]) },
}));

const mockUpload = vi.mocked(api.uploadTelemetry);
const mockGetMyLaps = vi.mocked(api.getMyLaps);

async function renderPage() {
  await act(async () => {
    render(
      <MemoryRouter>
        <TelemetryPage />
      </MemoryRouter>
    );
  });
}

describe('TelemetryPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetMyLaps.mockResolvedValue([]);
  });

  it('renders file input and heading', async () => {
    await renderPage();
    expect(screen.getByRole('heading', { name: /upload telemetry/i })).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeInTheDocument();
  });

  it('shows uploading state while processing', async () => {
    mockUpload.mockReturnValue(new Promise(() => {}));
    await renderPage();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['dummy'], 'session.ibt', { type: 'application/octet-stream' });
    await userEvent.upload(input, file);
    expect(screen.getByText(/parsing telemetry/i)).toBeInTheDocument();
  });

  it('shows upload result with driver name and lap count', async () => {
    mockUpload.mockResolvedValue({
      totalLaps: 15,
      validLaps: 12,
      bestLapSeconds: 131.5,
      trackName: 'Spa-Francorchamps',
      configName: 'Full',
      carName: 'Porsche 992 GT3',
      customerId: 99999,
      driverName: 'Jerry Holland',
    });
    await renderPage();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['dummy'], 'session.ibt', { type: 'application/octet-stream' });
    await userEvent.upload(input, file);
    await waitFor(() => {
      expect(screen.getByText(/upload complete/i)).toBeInTheDocument();
      expect(screen.getByText(/jerry holland/i)).toBeInTheDocument();
      expect(screen.getByText(/12 valid/i)).toBeInTheDocument();
    });
  });

  it('shows error message when upload fails', async () => {
    mockUpload.mockRejectedValue(new Error('Invalid file format'));
    await renderPage();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['dummy'], 'session.ibt', { type: 'application/octet-stream' });
    await userEvent.upload(input, file);
    await waitFor(() => expect(screen.getByText(/invalid file format/i)).toBeInTheDocument());
  });
});
