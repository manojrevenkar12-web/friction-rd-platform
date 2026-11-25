# 6. The lab drives the real C# core

Status: accepted

## Context
A simulation written in Python could measure a Python copy of the planner and the validator that
drifts from what the service runs.

## Decision
`rdo-core` (Rdo.Cli) exposes the planner, intake validator and evaluators over a JSON-lines
protocol. The Python lab starts it and calls it for every dispatch, every intake decision and every
evaluation. The world (`dynolab.world`) holds the truth and never reads `config/platform.json`.
Every random draw is keyed by what it is for, so every design meets the same luck. Held-out seeds
are run once.

## Consequences
The numbers in the README are produced by the same C# code the API runs. The lab needs the .NET SDK.
