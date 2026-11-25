# 5. Plan benches by slack and mounted fixture

Status: accepted

## Context
Benches run near capacity. First come, first served mounts a new fixture for most orders (about
three hours each) and lets homologation work wait behind exploratory tests.

## Decision
`SlackAwarePolicy`: an order whose slack falls below `urgentSlackHours` goes first (least slack);
otherwise orders that fit the mounted fixture are preferred, weighed against waiting time and
priority, so a changeover happens only when it is worth it. Settings live in `config/platform.json`.

## Consequences
In the lab: changeover hours fall by about a quarter and median lead time from 11.8 to 5.1 days against
first come, first served with the rest of the platform unchanged.
