# 1. Modernize by strangling the legacy application, not by rewriting it

Status: accepted

## Context
The R&D operations platform today is a C#/.NET desktop application on MSSQL, streamed to engineers
worldwide through Citrix or AWS AppStream. It works; it is slow to change, hard to test, and every
feature goes through an external development partner. A rewrite would stop delivery for a year and
risk the evaluation logic that homologation results depend on.

## Decision
- New capabilities are built as services next to the legacy application (ASP.NET Core API, Python
  analytics service, React console), on the same SQL Server, in their own `rdo` schema. No legacy
  table is altered.
- The legacy application keeps working: `dbo.vPlatformOrdersAsLegacy` shows platform orders in its
  shape, and `rdo.ImportLegacyOpenRequests` brings its open requests across once.
- The evaluation is moved behind a parity gate (ADR 2) before anything reads the new numbers.
- Screens move one at a time; the AppStream/Citrix stack is retired when the last one has moved.

## Consequences
- Delivery continues while the platform is modernized; each step is reversible.
- For a while there are two front ends and a bridge to maintain; the bridge is tested in CI against
  SQL Server.
