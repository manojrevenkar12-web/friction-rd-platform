import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Empty, ErrorState, Loading } from "../../shared/ui/States";
import { getSamples, replay, RUN_LABEL } from "./api";
import { RunChart } from "./RunChart";
import "./monitor.css";

const CHECK_LABEL: Record<string, string> = {
  procedure: "Procedure check",
  temperature: "Initial temperature check",
  cooling: "Cooling time check",
  muJump: "µ jump check",
  characteristic: "Characteristic section check",
};

export function MonitorView() {
  const samples = useQuery({ queryKey: ["samples"], queryFn: getSamples, retry: false });
  const [pick, setPick] = useState("thermocoupleLoose");
  const run = samples.data?.[pick];
  const verdict = useQuery({
    queryKey: ["replay", pick],
    queryFn: () => replay(run!.order.procedure, run!.stops),
    enabled: !!run,
    retry: false,
  });

  if (samples.isPending) return <Loading label="Loading recorded runs" />;
  if (samples.isError) return <ErrorState title="No recorded runs" detail={samples.error.message} />;
  const kinds = Object.keys(samples.data);
  if (kinds.length === 0) return <Empty title="No recorded runs" detail="Run `dynolab demo` to record sample runs." />;

  return (
    <div className="monitor">
      <header className="monitor-head">
        <div>
          <h1>Run monitor</h1>
          <p className="lede">A recorded run, replayed stop by stop through the monitor. It checks each stop against the procedure that was ordered and stops the bench as soon as the result can no longer be used.</p>
        </div>
        <div className="run-picker" role="radiogroup" aria-label="Recorded run">
          {kinds.map((k) => (
            <button key={k} type="button" role="radio" aria-checked={pick === k} onClick={() => setPick(k)}>{RUN_LABEL[k] ?? k}</button>
          ))}
        </div>
      </header>
      {run && (
        <section className="run-card">
          <p className="run-meta tabular">
            {run.order.procedure} on {run.order.formulation}, application {run.order.application}, {run.stops.length} stops
          </p>
          {verdict.isPending && <Loading label="The monitor is reading the run" rows={2} />}
          {verdict.isError && <ErrorState title="The monitor could not be asked" detail={verdict.error.message} onRetry={() => verdict.refetch()} />}
          {verdict.data && (
            <>
              <div className={`verdict ${verdict.data.verdict.usable ? "ok" : "stopped"}`}>
                {verdict.data.verdict.usable ? (
                  <p><strong>Run usable.</strong> {verdict.data.verdict.available ? "No check failed." : verdict.data.verdict.reason}</p>
                ) : (
                  <p>
                    <strong>Stopped at stop {verdict.data.verdict.stopAt} of {run.stops.length}</strong>
                    {" "}by the {CHECK_LABEL[verdict.data.verdict.check ?? ""]?.toLowerCase() ?? "monitor"}: {verdict.data.verdict.reason}
                    {" "}<span className="saved tabular">{Math.round((1 - ((verdict.data.verdict.stopAt ?? 0) + 1) / run.stops.length) * 100)}% of the bench time saved</span>
                  </p>
                )}
              </div>
              <RunChart stops={run.stops} stopAt={verdict.data.verdict.stopAt} runKey={pick} />
              {run.fault && (
                <p className="truth">In the simulated world this run had a fault from stop {run.onset}: {RUN_LABEL[run.fault].toLowerCase()}.</p>
              )}
            </>
          )}
        </section>
      )}
    </div>
  );
}
