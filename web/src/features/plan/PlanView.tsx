import { useQuery } from "@tanstack/react-query";
import { motion, useReducedMotion } from "framer-motion";
import { useState } from "react";
import { ErrorState, Empty, Loading } from "../../shared/ui/States";
import { getBenches, getOrders, getSchedule } from "./api";
import "./plan.css";

const HOURS = 24 * 10;
const KIND = { passengerCar: "Passenger car", commercialVehicle: "Commercial vehicle", nvh: "NVH" } as Record<string, string>;

export function PlanView() {
  const still = useReducedMotion();
  const [policy, setPolicy] = useState("slack-aware");
  const plan = useQuery({ queryKey: ["schedule", policy], queryFn: () => getSchedule(policy), retry: false });
  const other = useQuery({ queryKey: ["schedule", policy === "fifo" ? "slack-aware" : "fifo"], queryFn: () => getSchedule(policy === "fifo" ? "slack-aware" : "fifo"), retry: false });
  const orders = useQuery({ queryKey: ["orders"], queryFn: getOrders, retry: false });
  const benches = useQuery({ queryKey: ["benches"], queryFn: getBenches, retry: false });

  if (plan.isPending || orders.isPending || benches.isPending) return <Loading label="Planning the benches" rows={8} />;
  if (plan.isError) return <ErrorState title="The plan could not be made" detail={plan.error.message} onRetry={() => plan.refetch()} />;
  if (orders.isError || benches.isError) return <ErrorState title="Orders are unavailable" detail={(orders.error ?? benches.error)!.message} />;
  if (plan.data.dispatches.length === 0) return <Empty title="Nothing to plan" detail="There are no open orders. Submit a request or start the API with RDO_DEMO_SEED=60." />;

  const byId = new Map(orders.data.map((o) => [o.id, o]));
  const t0 = new Date(plan.data.generatedAt).getTime();
  const pos = (iso: string) => Math.max(0, Math.min(100, ((new Date(iso).getTime() - t0) / 3.6e6 / HOURS) * 100));
  const changeoverPct = (3 / HOURS) * 100;

  return (
    <div className="plan">
      <header className="plan-head">
        <div>
          <h1>Bench plan</h1>
          <p className="lede">The open orders on the eight dynamometers for the next ten days. Changing a fixture costs about three hours; the planner keeps fixtures mounted unless an order would miss its deadline or has waited too long.</p>
        </div>
        <div className="policy" role="radiogroup" aria-label="Planning rule">
          <button type="button" role="radio" aria-checked={policy === "slack-aware"} onClick={() => setPolicy("slack-aware")}>Slack-aware planner</button>
          <button type="button" role="radio" aria-checked={policy === "fifo"} onClick={() => setPolicy("fifo")}>First come, first served</button>
        </div>
      </header>
      <dl className="plan-stats tabular">
        <div><dt>Fixture changeovers</dt><dd>{plan.data.changeovers}{other.data && <small> vs {other.data.changeovers} with the other rule</small>}</dd></div>
        <div><dt>Orders finishing after their deadline</dt><dd>{plan.data.late}{other.data && <small> vs {other.data.late}</small>}</dd></div>
        <div><dt>Orders planned</dt><dd>{plan.data.dispatches.length}</dd></div>
      </dl>
      <div className="gantt" role="table" aria-label="Plan per bench">
        <div className="gantt-axis" aria-hidden>
          {Array.from({ length: HOURS / 24 + 1 }, (_, d) => <span key={d} style={{ left: `${(d * 24 / HOURS) * 100}%` }}>day {d}</span>)}
        </div>
        {benches.data.map((b) => (
          <div key={b.id} className="gantt-row" role="row">
            <div className="gantt-bench" role="rowheader"><strong>{b.id}</strong><span>{KIND[b.kind] ?? b.kind}</span></div>
            <div className="gantt-track" role="cell">
              {plan.data.dispatches.filter((d) => d.benchId === b.id && pos(d.start) < 100).map((d, i) => {
                const o = byId.get(d.orderId);
                const late = !!o?.deadline && new Date(d.end) > new Date(o.deadline);
                const left = pos(d.start);
                const width = Math.max(pos(d.end) - left, 0.4);
                return (
                  <motion.div key={`${policy}-${d.orderId}`} className={`job ${o?.priority ?? ""} ${late ? "late" : ""}`}
                    style={{ left: `${left}%`, width: `${width}%` }}
                    initial={still ? false : { opacity: 0, scaleX: 0.6 }} animate={{ opacity: 1, scaleX: 1 }}
                    transition={{ delay: still ? 0 : Math.min(i * 0.03, 0.4), duration: 0.2 }}
                    title={`${d.orderId}: ${o?.procedure} ${o?.formulation} on ${o?.application}${o?.deadline ? `, due ${o.deadline.slice(0, 10)}` : ""}. ${d.reason}`}>
                    {d.changeover && <span className="changeover" style={{ width: `${Math.min((changeoverPct / width) * 100, 100)}%` }} />}
                    <span className="job-label">{o?.procedure}</span>
                  </motion.div>
                );
              })}
            </div>
          </div>
        ))}
      </div>
      <ul className="legend">
        <li><span className="key homologation" />Homologation deadline</li>
        <li><span className="key development" />Development deadline</li>
        <li><span className="key exploratory" />No deadline</li>
        <li><span className="key changeover-key" />Fixture changeover</li>
        <li><span className="key late-key" />Finishes after its deadline</li>
      </ul>
    </div>
  );
}
