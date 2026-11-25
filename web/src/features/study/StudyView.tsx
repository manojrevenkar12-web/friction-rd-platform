import { useQuery } from "@tanstack/react-query";
import { staticJson } from "../../shared/api";
import { ErrorState, Loading } from "../../shared/ui/States";
import "./study.css";

type Mean = Record<string, number | null>;
type Report = { seeds: number[]; requestsPerSeed: number; designs: { id: string; label: string; kind: string; mean: Mean }[]; gates: { id: string; label: string; value: number | null; limit: string; pass: boolean }[] };
type Study = { dev: Report; heldout: Report | null };

const SHORT: Record<string, string> = {
  "status-quo": "Today",
  "model-alone": "Model, no checks",
  platform: "Platform",
  "manual-intake": "Coordinator enters requests",
  "no-intake-checks": "Model drafts taken unchecked",
  "fifo-planner": "First come, first served",
  "review-only": "No live monitor",
  "no-cooling-check": "No cooling check",
  "no-characteristic-check": "No characteristic check",
  "legacy-evaluator": "Legacy evaluation",
};
const n = (v: number | null | undefined, d = 1) => (v == null ? "–" : v.toLocaleString("en", { maximumFractionDigits: d, minimumFractionDigits: d }));
const p = (v: number | null | undefined) => (v == null ? "–" : `${(v * 100).toFixed(1)}%`);

export function StudyView() {
  const q = useQuery({ queryKey: ["study"], queryFn: () => staticJson<Study>("study.json", "No study results yet. Run `dynolab lab dev`."), retry: false });
  if (q.isPending) return <Loading label="Loading the study" rows={6} />;
  if (q.isError) return <ErrorState title="Study results are missing" detail={q.error.message} />;
  const { dev, heldout } = q.data;
  const by = Object.fromEntries(dev.designs.map((d) => [d.id, d.mean]));
  const lead = ["status-quo", "model-alone", "platform"].map((id) => ({ id, v: by[id].leadTimeMedianDays ?? 0 }));
  const max = Math.max(...lead.map((l) => l.v));
  return (
    <div className="study">
      <section>
        <h1>Days from a test request to a released result</h1>
        <p className="lede">{dev.seeds.length} simulated lab years, about {dev.requestsPerSeed.toLocaleString("en")} requests each, eight dynamometers at about 90% load. Every design meets the same requests, runs, faults and model behaviour.</p>
        <div className="lead-bars" role="img" aria-label={lead.map((l) => `${SHORT[l.id]}: ${n(l.v)} days`).join(", ")}>
          {lead.map((l) => (
            <div key={l.id} className={`lead-row ${l.id === "platform" ? "is-platform" : ""}`}>
              <span className="lead-name">{SHORT[l.id]}</span>
              <span className="lead-track"><span className="lead-bar" style={{ width: `${(l.v / max) * 100}%` }} /></span>
              <span className="lead-value tabular">{n(l.v)} days</span>
            </div>
          ))}
        </div>
      </section>
      <section>
        <h2>What each part is worth</h2>
        <div className="table-wrap">
          <table className="designs">
            <thead><tr><th scope="col">Design</th><th scope="col">Median days</th><th scope="col">Homologation missed</th><th scope="col">Wrong results per 1,000</th><th scope="col">Wrong verdicts per 1,000</th><th scope="col">Bench hours lost per week</th><th scope="col">Changeover hours per week</th><th scope="col">Asked back</th></tr></thead>
            <tbody>
              {dev.designs.map((d) => (
                <tr key={d.id} className={d.kind === "platform" ? "is-platform" : ""}>
                  <th scope="row">{SHORT[d.id] ?? d.label}</th>
                  <td>{n(d.mean.leadTimeMedianDays)}</td><td>{p(d.mean.homologationMissRate)}</td><td>{n(d.mean.wrongResultsPer1000)}</td>
                  <td>{n(d.mean.verdictErrorsPer1000)}</td><td>{n(d.mean.lostBenchHoursPerWeek, 0)}</td><td>{n(d.mean.changeoverHoursPerWeek, 0)}</td><td>{p(d.mean.clarificationShare)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      <section className="gate-cols">
        <Gates title={`Gates on dev seeds ${dev.seeds.join(", ")}`} r={dev} />
        {heldout ? <Gates title={`Held-out seeds ${heldout.seeds.join(", ")}, run once`} r={heldout} /> : <p className="lede">Held-out seeds not run yet.</p>}
      </section>
    </div>
  );
}

function Gates({ title, r }: { title: string; r: Report }) {
  return (
    <div className="gates">
      <h2>{title}</h2>
      <p className="lede">{r.gates.filter((g) => g.pass).length} of {r.gates.length} pass</p>
      <ul>
        {r.gates.map((g) => (
          <li key={g.id} className={g.pass ? "pass" : "fail"}>
            <span aria-label={g.pass ? "passes" : "fails"}>{g.pass ? "✓" : "✕"}</span>
            <span>{g.label}</span>
            <span className="tabular">{g.value == null ? "–" : +g.value.toFixed(3)} <small>{g.limit}</small></span>
          </li>
        ))}
      </ul>
    </div>
  );
}
