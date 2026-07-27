import { Link } from 'react-router';

export default function Footer() {
  return (
    <footer className="bg-surface-dim text-on-surface-variant font-body-sm text-body-sm w-full py-6 border-t border-line-2 flex flex-col md:flex-row justify-between items-center px-6 mt-auto">
      <div className="font-body-lg text-on-surface mb-4 md:mb-0">ApexRacers</div>
      <div className="flex gap-6 mb-4 md:mb-0">
        <Link className="hover:text-primary-fixed-dim transition-colors" to="/terms">
          Terms of Service
        </Link>
        <Link className="hover:text-primary-fixed-dim transition-colors" to="/privacy">
          Privacy Policy
        </Link>
        <a
          className="hover:text-primary-fixed-dim transition-colors"
          href="https://apex-racers.betteruptime.com/"
          target="_blank"
          rel="noopener noreferrer"
        >
          API Status
        </a>
      </div>
      <div>© {new Date().getFullYear()} ApexRacers. Not affiliated with iRacing.com</div>
    </footer>
  );
}
