import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import CarsPage from '../CarsPage';
import { api, type CarCatalogItem } from '../../services/api';

vi.mock('../../services/api', () => ({ api: { getCars: vi.fn() } }));

const mockGetCars = vi.mocked(api.getCars);

const CARS: CarCatalogItem[] = [
  {
    carId: 1,
    name: 'Audi R8 LMS',
    nameAbbreviated: 'AudiR8',
    make: 'Audi',
    model: 'R8',
    hp: 562,
    weight: 1300,
    rainEnabled: true,
    freeWithSubscription: false,
    categories: ['sports_car'],
    smallImageUrl: 'https://img/audi.jpg',
  },
  {
    carId: 2,
    name: 'Skip Barber F2000',
    nameAbbreviated: 'Skip',
    make: 'Skip Barber',
    model: 'F2000',
    hp: 132,
    weight: 500,
    rainEnabled: false,
    freeWithSubscription: true,
    categories: ['formula_car'],
    smallImageUrl: null,
  },
];

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/cars']}>
      <CarsPage />
    </MemoryRouter>
  );
}

describe('CarsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetCars.mockResolvedValue(CARS);
  });

  it('shows a loading state while fetching', () => {
    mockGetCars.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders a card per car linking to its detail page', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Audi R8 LMS')).toBeInTheDocument();
      expect(screen.getByText('Skip Barber F2000')).toBeInTheDocument();
    });
    const link = screen.getByRole('link', { name: /audi r8 lms/i });
    expect(link).toHaveAttribute('href', '/cars/1');
  });

  it('filters cars by the name search box', async () => {
    renderPage();
    await screen.findByText('Audi R8 LMS');
    fireEvent.change(screen.getByPlaceholderText(/search cars/i), { target: { value: 'skip' } });
    expect(screen.queryByText('Audi R8 LMS')).not.toBeInTheDocument();
    expect(screen.getByText('Skip Barber F2000')).toBeInTheDocument();
  });

  it('filters cars by category chip', async () => {
    renderPage();
    await screen.findByText('Audi R8 LMS');
    fireEvent.click(screen.getByRole('button', { name: /formula car/i }));
    await waitFor(() => {
      expect(screen.queryByText('Audi R8 LMS')).not.toBeInTheDocument();
      expect(screen.getByText('Skip Barber F2000')).toBeInTheDocument();
    });
  });

  it('shows an error state when the catalog is unavailable', async () => {
    mockGetCars.mockRejectedValue(new Error('iRacing not configured'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/iRacing not configured/i)).toBeInTheDocument());
  });
});
