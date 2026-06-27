const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

type SupportCard = {
  icon: string;
  title: string;
  description: string;
  href?: string;
  external?: boolean;
};

const CARDS: SupportCard[] = [
  {
    icon: 'help',
    title: 'Help center',
    description: 'Guides on percentiles, ingestion, and telemetry.',
  },
  {
    icon: 'mail',
    title: 'Contact us',
    description: 'Average reply time under 6 hours.',
    href: 'mailto:support@apexracers.gg',
  },
  {
    icon: 'monitor_heart',
    title: 'API status',
    description: 'Live uptime for the API and ingestion worker.',
    href: 'https://apex-racers.betteruptime.com/',
    external: true,
  },
  {
    icon: 'history',
    title: 'Changelog',
    description: 'See what shipped recently.',
  },
];

function Card({ card }: { card: SupportCard }) {
  const inner = (
    <div
      className="card-r border border-line-2 bg-surface card-p flex items-start gap-3.5 h-full"
      style={cardStyle}
    >
      <div className="w-10 h-10 shrink-0 rounded-[11px] grid place-items-center bg-primary-container/15 text-primary-container">
        <span className="material-symbols-outlined text-[20px]" aria-hidden="true">
          {card.icon}
        </span>
      </div>
      <div>
        <div className="text-section-head text-on-surface">{card.title}</div>
        <p className="text-small-fluid text-on-surface-variant mt-1">{card.description}</p>
      </div>
    </div>
  );

  if (!card.href) return inner;

  return (
    <a
      href={card.href}
      {...(card.external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
      className="block transition-colors hover:[&>div]:border-primary-container/40"
    >
      {inner}
    </a>
  );
}

export default function SupportPage() {
  return (
    <main className="page-wrap max-w-[760px]">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">SUPPORT</p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">Help &amp; support</h1>
        <p className="text-body-fluid text-on-surface-variant">
          Find answers or reach the ApexRacers crew.
        </p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-fluid">
        {CARDS.map(card => (
          <Card key={card.title} card={card} />
        ))}
      </div>
    </main>
  );
}
