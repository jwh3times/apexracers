import { Link } from 'react-router';
import type { Resource } from '../hooks/useResource';

export function NotLinkedCard({ reason }: { reason: string }) {
  return (
    <div className="card-r card-shadow border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center w-full max-w-md">
      <span
        className="material-symbols-outlined text-4xl text-primary-container"
        aria-hidden="true"
      >
        link_off
      </span>
      <p className="text-body-fluid text-on-surface-variant">{reason}</p>
      <Link to="/settings" className="text-primary-container underline hover:opacity-80">
        Open Settings
      </Link>
    </div>
  );
}

type ResourceViewProps<T> = {
  resource: Resource<T>;
  notLinkedReason?: string;
};

/** Renders the shared non-data states. Page-specific data stays with the page. */
export default function ResourceView<T>({ resource, notLinkedReason }: ResourceViewProps<T>) {
  if (resource.status === 'loading') {
    return <p className="text-body-fluid text-on-surface-variant animate-pulse">Loading&hellip;</p>;
  }

  if (resource.status === 'error') {
    return (
      <div className="card-r card-shadow border border-line-2 bg-surface p-6 text-body-fluid text-error">
        {resource.message}
      </div>
    );
  }

  if (resource.status === 'not-linked') {
    return <NotLinkedCard reason={notLinkedReason ?? 'This page needs your iRacing identity.'} />;
  }

  return null;
}
