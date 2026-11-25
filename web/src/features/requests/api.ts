import { api } from "../../shared/api";

export type Claim = { value: string | null; evidence: string | null };
export type Draft = { formulation: Claim; application: Claim; procedure: Claim; samples: Claim; deadline: Claim; questions: string[] };
export type Order = { id: string; formulation: string; application: string; procedure: string; samples: number; deadline: string | null; priority: string };
export type Decision = { status: string; order: Order | null; problems: string[]; questions: string[]; warnings: string[] };
export type TestRequest = { id: string; text: string; submittedBy: string; submittedAt: string; status: "accepted" | "needsClarification" | "rejected"; model: string; orderId: string | null; draft: Draft | null; decision: Decision };

export const listRequests = () => api<TestRequest[]>("/api/requests");
export const submitRequest = (text: string) => api<TestRequest>("/api/requests", { method: "POST", body: JSON.stringify({ text }), as: "engineer" });
export const answerRequest = (id: string, text: string) =>
  api<TestRequest>(`/api/requests/${encodeURIComponent(id)}/answer`, { method: "POST", body: JSON.stringify({ text }), as: "engineer" });

export const STATUS_LABEL = { accepted: "Order created", needsClarification: "Asked back", rejected: "Refused" } as const;
export const FIELD_LABEL: Record<keyof Omit<Draft, "questions">, string> = {
  formulation: "Formulation",
  application: "Application",
  procedure: "Procedure",
  samples: "Samples",
  deadline: "Deadline",
};

/** Splits the request text around the words each field was read from. */
export function highlight(text: string, draft: Draft | null): { text: string; field?: string }[] {
  if (!draft) return [{ text }];
  const marks = (Object.keys(FIELD_LABEL) as (keyof typeof FIELD_LABEL)[])
    .map((k) => ({ field: k, ev: draft[k]?.evidence }))
    .filter((m): m is { field: keyof typeof FIELD_LABEL; ev: string } => !!m.ev)
    .map((m) => ({ ...m, at: text.toLowerCase().indexOf(m.ev.toLowerCase()) }))
    .filter((m) => m.at >= 0)
    .sort((a, b) => a.at - b.at);
  const out: { text: string; field?: string }[] = [];
  let pos = 0;
  for (const m of marks) {
    if (m.at < pos) continue;
    if (m.at > pos) out.push({ text: text.slice(pos, m.at) });
    out.push({ text: text.slice(m.at, m.at + m.ev.length), field: m.field });
    pos = m.at + m.ev.length;
  }
  if (pos < text.length) out.push({ text: text.slice(pos) });
  return out;
}
