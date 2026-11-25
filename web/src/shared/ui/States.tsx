import type { ReactNode } from "react";
import "./states.css";

export function Loading({ label, rows = 3 }: { label: string; rows?: number }) {
  return (
    <div className="state" role="status" aria-live="polite" aria-label={label}>
      {Array.from({ length: rows }, (_, i) => <span key={i} className="skeleton" style={{ width: `${90 - i * 15}%` }} />)}
      <p className="state-note">{label}</p>
    </div>
  );
}

export function ErrorState({ title, detail, onRetry }: { title: string; detail: string; onRetry?: () => void }) {
  return (
    <div className="state state-error" role="alert">
      <p className="state-title">{title}</p>
      <p>{detail}</p>
      {onRetry && <button type="button" className="btn secondary" onClick={onRetry}>Try again</button>}
    </div>
  );
}

export function Empty({ title, detail, action }: { title: string; detail: string; action?: ReactNode }) {
  return (
    <div className="state state-empty">
      <p className="state-title">{title}</p>
      <p>{detail}</p>
      {action}
    </div>
  );
}
