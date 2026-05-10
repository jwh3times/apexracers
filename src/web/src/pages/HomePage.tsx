import { Link } from 'react-router-dom';
import heroImg from '../assets/hero.png';

const featuredSeries = [
  {
    id: 1,
    seriesClass: 'ROAD B CLASS',
    name: 'VRS GT3 Sprint',
    track: 'Spa-Francorchamps',
    cars: 'GT3 Class',
    activeDrivers: '1,245',
    tier: 'PRO',
    tierClass: 'tier-badge-gold',
    accentFrom: 'from-primary-container',
  },
  {
    id: 2,
    seriesClass: 'ROAD A CLASS',
    name: 'IMSA iRacing Series',
    track: 'Road America',
    cars: 'LMDh, LMP2, GT3',
    activeDrivers: '892',
    tier: 'PRO',
    tierClass: 'tier-badge-gold',
    accentFrom: 'from-[#FFD700]',
  },
  {
    id: 3,
    seriesClass: 'ROAD C CLASS',
    name: 'Porsche Cup',
    track: 'Nürburgring Nordschleife',
    cars: 'Porsche 911 GT3 Cup',
    activeDrivers: '2,104',
    tier: 'POPULAR',
    tierClass: 'tier-badge-green',
    accentFrom: null,
  },
];

export default function HomePage() {
  return (
    <main className="pt-8 pb-20 px-6 max-w-[1440px] mx-auto w-full">
      {/* Hero */}
      <section className="relative rounded-xl overflow-hidden mb-16 min-h-[600px] flex items-center glass-panel">
        <img
          alt="Sim racing cockpit with neon green telemetry display"
          className="absolute inset-0 w-full h-full object-cover opacity-30 mix-blend-luminosity"
          src={heroImg}
        />
        <div className="absolute inset-0 bg-gradient-to-r from-surface via-surface/80 to-transparent" />
        <div className="relative z-10 px-8 md:px-16 max-w-3xl">
          <h1 className="font-display-lg text-display-lg text-on-surface mb-6 leading-tight">
            Find the car that best fits your pace
          </h1>
          <p className="font-body-lg text-body-lg text-on-surface-variant mb-10 max-w-xl">
            Unlock professional-grade telemetry analysis. Compare your lap times against the full
            field and discover where you rank by percentile — so you can pick the car where you are
            most competitive.
          </p>
          <div className="flex flex-col sm:flex-row gap-4">
            <Link
              to="/login"
              className="bg-gradient-to-r from-[#0F0F12] to-[#1A1A1C] border border-primary-container text-primary-container font-headline-sm px-8 py-4 rounded hover:border-[#00D1FF] transition-all duration-300 shadow-[0_0_20px_rgba(0,255,136,0.2)] flex items-center justify-center gap-3 w-full sm:w-auto"
            >
              Sign in with iRacing
              <span className="material-symbols-outlined">arrow_forward</span>
            </Link>
            <Link
              to="/series"
              className="bg-surface-variant text-on-surface font-headline-sm px-8 py-4 rounded hover:bg-surface-container-high transition-colors flex items-center justify-center gap-2 w-full sm:w-auto"
            >
              <span className="material-symbols-outlined">explore</span>
              Explore Features
            </Link>
          </div>
        </div>
      </section>

      {/* Active Series */}
      <section className="mb-16">
        <div className="flex justify-between items-end mb-8">
          <div>
            <h2 className="font-headline-md text-headline-md text-on-surface mb-2">Active Series</h2>
            <p className="font-body-sm text-body-sm text-on-surface-variant">
              Top competitive leagues running this week.
            </p>
          </div>
          <Link
            to="/series"
            className="text-primary-fixed-dim hover:text-primary font-body-sm flex items-center gap-1 transition-colors"
          >
            View All <span className="material-symbols-outlined text-[16px]">chevron_right</span>
          </Link>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {featuredSeries.map((series) => (
            <div
              key={series.id}
              className="glass-panel p-6 rounded-lg relative overflow-hidden hover:border-primary-container/50 transition-colors duration-300"
            >
              {series.accentFrom && (
                <div
                  className={`absolute top-0 left-0 w-full h-1 bg-gradient-to-r ${series.accentFrom} to-transparent`}
                />
              )}
              <div className="flex justify-between items-start mb-6">
                <div>
                  <span className="font-label-caps text-label-caps text-on-surface-variant tracking-widest block mb-1">
                    {series.seriesClass}
                  </span>
                  <h3 className="font-headline-sm text-headline-sm text-on-surface">{series.name}</h3>
                </div>
                <span className={`${series.tierClass} font-label-caps text-label-caps px-2 py-1 rounded`}>
                  {series.tier}
                </span>
              </div>
              <div className="space-y-4 mb-6">
                <div className="flex items-center gap-3 text-on-surface-variant">
                  <span className="material-symbols-outlined text-[20px]">flag</span>
                  <span className="font-body-sm text-body-sm">{series.track}</span>
                </div>
                <div className="flex items-center gap-3 text-on-surface-variant">
                  <span className="material-symbols-outlined text-[20px]">directions_car</span>
                  <span className="font-body-sm text-body-sm">{series.cars}</span>
                </div>
              </div>
              <div className="border-t border-white/5 pt-4 flex justify-between items-center">
                <div className="flex flex-col">
                  <span className="font-label-caps text-label-caps text-on-surface-variant mb-1">
                    ACTIVE DRIVERS
                  </span>
                  <span className="font-data-lg text-data-lg text-primary-fixed-dim">
                    {series.activeDrivers}
                  </span>
                </div>
                <Link to="/series" className="text-on-surface hover:text-primary-container transition-colors">
                  <span className="material-symbols-outlined">analytics</span>
                </Link>
              </div>
            </div>
          ))}
        </div>
      </section>
    </main>
  );
}
