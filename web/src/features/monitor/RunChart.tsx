import { motion, useReducedMotion } from "framer-motion";
import { useMemo, useState } from "react";
import type { Stop } from "./api";

const W = 920;
const H = 300;
const PAD = { l: 44, r: 12, t: 16, b: 42 };
const STRIP = 54;

/** µ for every stop of a run, the fade sections shaded, and where the monitor stopped it. */
export function RunChart({ stops, stopAt, runKey }: { stops: Stop[]; stopAt: number | null; runKey: string }) {
  const still = useReducedMotion();
  const [hover, setHover] = useState<Stop | null>(null);
  const n = stops.length;
  const x = (i: number) => PAD.l + (i / Math.max(n - 1, 1)) * (W - PAD.l - PAD.r);
  const yMu = (mu: number) => PAD.t + (1 - (mu - 0.1) / 0.55) * (H - PAD.t - PAD.b - STRIP);
  const maxCool = Math.max(...stops.map((s) => s.coolingSeconds), 1);
  const stripTop = H - PAD.b - STRIP + 12;
  const ySec = (sec: number) => stripTop + (1 - Math.min(sec, maxCool) / maxCool) * (STRIP - 16);
  const sections = useMemo(() => {
    const out: { name: string; from: number; to: number }[] = [];
    stops.forEach((s, i) => {
      const last = out[out.length - 1];
      if (last && last.name === s.section) last.to = i;
      else out.push({ name: s.section, from: i, to: i });
    });
    return out;
  }, [stops]);
  const shown = stopAt ?? n - 1;
  const revealX = x(shown) + 4;

  return (
    <figure className="run-chart">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Friction coefficient for ${n} stops${stopAt !== null ? `, stopped at stop ${stopAt}` : ""}`}>
        <defs>
          <clipPath id={`reveal-${runKey}`}>
            <motion.rect key={runKey} x={0} y={0} height={H} initial={{ width: still ? revealX : PAD.l }} animate={{ width: revealX }}
              transition={{ duration: still ? 0 : 1.6, ease: "easeInOut" }} />
          </clipPath>
        </defs>
        {sections.filter((s) => s.name.startsWith("fade")).map((s) => (
          <rect key={s.from} className="fade-band" x={x(s.from) - 3} y={PAD.t} width={x(s.to) - x(s.from) + 6} height={H - PAD.t - PAD.b} />
        ))}
        {[0.2, 0.3, 0.4, 0.5, 0.6].map((v) => (
          <g key={v}>
            <line className="grid" x1={PAD.l} x2={W - PAD.r} y1={yMu(v)} y2={yMu(v)} />
            <text className="tick" x={PAD.l - 8} y={yMu(v) + 4} textAnchor="end">{v.toFixed(1)}</text>
          </g>
        ))}
        <text className="axis" x={PAD.l} y={PAD.t - 4}>µ</text>
        <text className="axis" x={PAD.l} y={stripTop - 2}>cooling time per stop</text>
        <g clipPath={`url(#reveal-${runKey})`}>
          {stops.map((s) => (
            <circle key={s.index} cx={x(s.index)} cy={yMu(s.mu)} r={3} className={s.section === "burnish" ? "dot burnish" : s.section.startsWith("fade") ? "dot fade" : "dot"} />
          ))}
          {stops.map((s) => (
            <line key={`c${s.index}`} className="cool" x1={x(s.index)} x2={x(s.index)} y1={stripTop + STRIP - 16} y2={ySec(s.coolingSeconds)} />
          ))}
        </g>
        {sections.map((s) => (
          <text key={`l${s.from}`} className="section-label" x={(x(s.from) + x(s.to)) / 2} y={H - PAD.b + 16} textAnchor="middle">
            {s.to - s.from >= 5 ? s.name.replace("characteristic-", "char. ") : ""}
          </text>
        ))}
        {stopAt !== null && (
          <motion.g initial={{ opacity: still ? 1 : 0 }} animate={{ opacity: 1 }} transition={{ delay: still ? 0 : 1.6 }}>
            <line className="stop-line" x1={x(stopAt)} x2={x(stopAt)} y1={PAD.t} y2={H - PAD.b} />
            <text className="stop-label" x={Math.min(x(stopAt) + 6, W - 120)} y={PAD.t + 12}>stopped at {stopAt}</text>
          </motion.g>
        )}
        <rect x={PAD.l} y={PAD.t} width={W - PAD.l - PAD.r} height={H - PAD.t - PAD.b} fill="transparent"
          onMouseMove={(e) => {
            const r = (e.target as SVGRectElement).getBoundingClientRect();
            const i = Math.round(((e.clientX - r.left) / r.width) * (n - 1));
            setHover(stops[Math.max(0, Math.min(n - 1, i))]);
          }}
          onMouseLeave={() => setHover(null)} />
        {hover && <line className="cursor" x1={x(hover.index)} x2={x(hover.index)} y1={PAD.t} y2={H - PAD.b} />}
      </svg>
      <figcaption className="tabular">
        {hover
          ? `Stop ${hover.index}, ${hover.section}: µ ${hover.mu.toFixed(3)}, ${hover.pressureBar} bar at ${hover.speedKmh} km/h, ${hover.initialTempC} °C, cooled in ${hover.coolingSeconds.toFixed(0)} s`
          : "Move over the chart to read a stop. Shaded: fade sections, where µ is meant to drop."}
      </figcaption>
    </figure>
  );
}
