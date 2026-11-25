import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { useState } from "react";
import { Empty, ErrorState, Loading } from "../../shared/ui/States";
import { answerRequest, FIELD_LABEL, highlight, listRequests, STATUS_LABEL, submitRequest, type TestRequest } from "./api";
import "./requests.css";

export function RequestsView() {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["requests"], queryFn: listRequests, retry: false });
  const [selected, setSelected] = useState<string | null>(null);
  const [text, setText] = useState("");
  const submit = useMutation({
    mutationFn: submitRequest,
    onSuccess: (r) => {
      setSelected(r.id);
      setText("");
      qc.invalidateQueries({ queryKey: ["requests"] });
    },
  });
  const current = list.data?.find((r) => r.id === selected) ?? submit.data ?? list.data?.[0];

  return (
    <div className="requests">
      <section className="intake">
        <h1>Test requests</h1>
        <p className="lede">Engineers write requests the way they write e-mails. The intake agent reads each one; code checks every field against the words it came from and the master data, and asks back when the request leaves something open.</p>
        <form onSubmit={(e) => { e.preventDefault(); if (text.trim()) submit.mutate(text); }}>
          <label htmlFor="req">New request</label>
          <textarea id="req" rows={3} value={text} onChange={(e) => setText(e.target.value)}
            placeholder="Hallo Prüffeld, bitte AK Master für Rezeptur FM-1234 auf Compact C1 VA, 2 Muster, bis KW 14 für die OEM-B Freigabe." />
          <div className="form-row">
            <button type="submit" className="btn" disabled={submit.isPending || !text.trim()}>{submit.isPending ? "Reading…" : "Submit request"}</button>
            {submit.isError && <p className="form-error" role="alert">{submit.error.message}</p>}
          </div>
        </form>
      </section>
      <div className="requests-body">
        <section className="request-list" aria-label="Requests">
          {list.isPending && <Loading label="Loading requests" rows={6} />}
          {list.isError && <ErrorState title="Requests are unavailable" detail={list.error.message} onRetry={() => list.refetch()} />}
          {list.data && list.data.length === 0 && <Empty title="No requests yet" detail="Submit the first request above, or start the API with RDO_DEMO_SEED=60." />}
          {list.data && list.data.length > 0 && (
            <ul>
              {list.data.map((r) => (
                <li key={r.id}>
                  <button type="button" aria-current={current?.id === r.id} onClick={() => setSelected(r.id)}>
                    <span className={`chip ${r.status}`}>{STATUS_LABEL[r.status]}</span>
                    <span className="snippet">{r.text}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
        <AnimatePresence mode="wait">{current && <Detail key={current.id} r={current} />}</AnimatePresence>
      </div>
    </div>
  );
}

function Detail({ r }: { r: TestRequest }) {
  const still = useReducedMotion();
  const qc = useQueryClient();
  const [answer, setAnswer] = useState("");
  const reply = useMutation({ mutationFn: (t: string) => answerRequest(r.id, t), onSuccess: () => qc.invalidateQueries({ queryKey: ["requests"] }) });
  const d = r.decision;
  return (
    <motion.article className="request-detail" initial={still ? false : { opacity: 0, x: 8 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0 }} transition={{ duration: 0.18 }}>
      <p className="request-text">
        {highlight(r.text, r.draft).map((p, i) =>
          p.field ? <mark key={i} className={`f-${p.field}`} title={FIELD_LABEL[p.field as keyof typeof FIELD_LABEL]}>{p.text}</mark> : <span key={i}>{p.text}</span>)}
      </p>
      <p className="by">{r.submittedBy}, read by {r.model}</p>
      {d.order && (
        <dl className="order tabular">
          <div><dt>Order</dt><dd>{d.order.id}</dd></div>
          <div><dt>Formulation</dt><dd>{d.order.formulation}</dd></div>
          <div><dt>Application</dt><dd>{d.order.application}</dd></div>
          <div><dt>Procedure</dt><dd>{d.order.procedure}</dd></div>
          <div><dt>Samples</dt><dd>{d.order.samples}</dd></div>
          <div><dt>Due</dt><dd>{d.order.deadline ? d.order.deadline.slice(0, 10) : "no deadline"}</dd></div>
          <div><dt>Priority</dt><dd>{d.order.priority}</dd></div>
        </dl>
      )}
      {d.problems.length > 0 && <Findings title="The draft did not match the request" items={d.problems} tone="stop" />}
      {d.questions.length > 0 && <Findings title="Questions for the engineer" items={d.questions} tone="ask" />}
      {d.warnings.length > 0 && <Findings title="Noted" items={d.warnings} tone="note" />}
      {r.status === "needsClarification" && (
        <form className="answer" onSubmit={(e) => { e.preventDefault(); if (answer.trim()) reply.mutate(answer); }}>
          <label htmlFor="ans">Answer</label>
          <input id="ans" value={answer} onChange={(e) => setAnswer(e.target.value)} placeholder="Vorderachse" />
          <button type="submit" className="btn secondary" disabled={reply.isPending || !answer.trim()}>Send answer</button>
          {reply.isError && <p className="form-error" role="alert">{reply.error.message}</p>}
        </form>
      )}
    </motion.article>
  );
}

function Findings({ title, items, tone }: { title: string; items: string[]; tone: "stop" | "ask" | "note" }) {
  return (
    <section className={`findings ${tone}`}>
      <h2>{title}</h2>
      <ul>{items.map((i) => <li key={i}>{i}</li>)}</ul>
    </section>
  );
}
