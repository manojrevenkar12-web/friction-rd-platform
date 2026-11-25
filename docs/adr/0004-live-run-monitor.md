# 4. Watch runs stop by stop, against the procedure that was ordered

Status: accepted

## Context
An AK Master style run takes 14 hours. A loose thermocouple, a drifting torque transducer, a wrong
procedure file, failed cooling air or a cracked pad make the result unusable; today they are found,
if at all, when an engineer reviews the report days later.

## Decision
- The Python analytics service checks each stop against the ordered procedure (pressure, speed,
  section), the initial temperature, the cooling time against earlier stops at the same speed,
  µ jumps, and the characteristic sections against each other (tighter before the first fade,
  where real pads do not shift).
- The C# API forwards stops and stops the run on the first failed check. If the analytics service
  is down, the run continues and is reviewed afterwards: the monitor never blocks a bench.
- After a false stop, an engineer may clear that check for the order and restart.

## Consequences
- In the lab, wrong results released fall from 44 to 17 per 1,000 against review only.
- Faults with their onset in the last fade section cannot be seen live (no cooling or
  characteristic stop follows) and are left to review.
