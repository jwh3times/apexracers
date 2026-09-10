import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import TelemetryPage from './TelemetryPage';
import { api } from '../../services/api';
import { MAX_UPLOAD_BYTES, MAX_UPLOAD_MEGABYTES } from './uploadLimits';

/**
 * A File reporting `size` bytes without allocating them. jsdom takes `size` from the blob parts,
 * so a genuinely oversized fixture would mean allocating a quarter of a gigabyte per test.
 */
function fileOfSize(name: string, size: number): File {
  const file = new File(['dummy'], name, { type: 'application/octet-stream' });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockUpload = vi.mocked(api.uploadTelemetry);
const mockGetMyLaps = vi.mocked(api.getMyUploadedBests);

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

  it('advertises the limit it actually enforces', async () => {
    await renderPage();
    expect(screen.getByText(`MAX ${MAX_UPLOAD_MEGABYTES} MB per file`)).toBeInTheDocument();
  });

  it('refuses an oversized file without sending it', async () => {
    await renderPage();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(input, fileOfSize('huge.ibt', MAX_UPLOAD_BYTES + 1));
    await waitFor(() => expect(screen.getByText(/too large/i)).toBeInTheDocument());
    // The point of the client-side bound: the bytes never leave the browser.
    expect(mockUpload).not.toHaveBeenCalled();
  });

  it('still uploads the acceptable files alongside an oversized one', async () => {
    mockUpload.mockResolvedValue({
      totalLaps: 3,
      validLaps: 3,
      bestLapSeconds: 90.5,
      trackName: 'Watkins Glen',
      configName: null,
      carName: 'Ferrari 296 GT3',
      customerId: 12345,
      driverName: 'Jerry Holland',
    });
    await renderPage();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(input, [
      fileOfSize('huge.ibt', MAX_UPLOAD_BYTES + 1),
      fileOfSize('fine.ibt', 4096),
    ]);
    await waitFor(() => expect(screen.getByText(/too large/i)).toBeInTheDocument());
    // One rejected file must not abandon the rest of the selection.
    expect(mockUpload).toHaveBeenCalledTimes(1);
    expect(mockUpload.mock.calls[0][0].name).toBe('fine.ibt');
  });
});
