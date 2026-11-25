import { api, staticJson } from "../../shared/api";

export type Stop = { index: number; section: string; speedKmh: number; pressureBar: number; initialTempC: number; coolingSeconds: number; mu: number };
export type SampleRun = { fault: string | null; onset: number | null; order: { formulation: string; application: string; procedure: string }; stops: Stop[] };
export type Verdict = { usable: boolean; stopAt: number | null; check: string | null; reason: string | null; available: boolean };
export type Section = { name: string; stops: number; pressureBar: number; speedKmh: number; initialTempC: number };

export const getSamples = () => staticJson<Record<string, SampleRun>>("sample-runs.json", "No sample runs. Run `dynolab demo` to write them.");

export const replay = (procedureId: string, stops: Stop[]) =>
  api<{ verdict: Verdict; procedure: { id: string; name: string; sections: Section[] } }>("/api/monitor/replay", {
    method: "POST",
    body: JSON.stringify({ procedureId, stops }),
  });

export const RUN_LABEL: Record<string, string> = {
  healthy: "Healthy run",
  thermocoupleLoose: "Thermocouple came loose",
  torqueDrift: "Torque transducer drifting",
  wrongProcedureLoaded: "Wrong procedure loaded",
  coolingAirFailure: "Cooling air failed",
  sampleCracked: "Pad cracked",
};
