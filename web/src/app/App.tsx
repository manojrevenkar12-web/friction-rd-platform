import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { MonitorView } from "../features/monitor/MonitorView";
import { PlanView } from "../features/plan/PlanView";
import { RequestsView } from "../features/requests/RequestsView";
import { StudyView } from "../features/study/StudyView";
import "./app.css";

const client = new QueryClient();
const VIEWS = { requests: "Requests", plan: "Bench plan", monitor: "Run monitor", study: "Study" } as const;
type View = keyof typeof VIEWS;

export function App({ initial }: { initial?: View }) {
  const [view, setView] = useState<View>(() => initial ?? ((location.hash.slice(1) as View) in VIEWS ? (location.hash.slice(1) as View) : "requests"));
  const go = (v: View) => { setView(v); history.replaceState(null, "", `#${v}`); };
  return (
    <QueryClientProvider client={client}>
      <header className="masthead">
        <p className="mark">Friction R&amp;D <span>operations</span></p>
        <nav aria-label="Views">
          {(Object.keys(VIEWS) as View[]).map((v) => (
            <button key={v} type="button" aria-current={view === v ? "page" : undefined} onClick={() => go(v)}>{VIEWS[v]}</button>
          ))}
        </nav>
      </header>
      <main className="page">
        {view === "requests" && <RequestsView />}
        {view === "plan" && <PlanView />}
        {view === "monitor" && <MonitorView />}
        {view === "study" && <StudyView />}
      </main>
    </QueryClientProvider>
  );
}
