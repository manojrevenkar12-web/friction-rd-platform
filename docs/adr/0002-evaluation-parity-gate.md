# 2. Port the evaluation line by line, then run it in shadow before switching

Status: accepted

## Context
Pass/fail verdicts go to OEMs for release decisions. The legacy evaluator rounds every stop's µ to
two decimals before averaging and its fade loop stops one stop early, usually skipping the hottest
stop, where µ is lowest.

## Decision
- `Rdo.Legacy.LegacyFrictionEvaluator` reproduces the legacy calculation exactly (characterization).
- `FrictionEvaluator` computes at full precision over every stop.
- `evaluation.engine` is `legacy`, `shadow` or `new`. In shadow, the new evaluator answers and the
  legacy one is compared on every run; `Parity` reports rounding, differences and changed verdicts.

## Consequences
- In the lab, the legacy calculation releases about 26 wrong pass/fail verdicts per 1,000 results
  and the new one about 2.5 (from measurement noise near the limits).
- Switching to `new` changes some historical verdicts. Those runs are listed by the shadow report
  for the lab lead to review before the switch.
