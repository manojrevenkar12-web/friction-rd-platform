# Working with external development partners

The platform is built partly by external partners. These rules make their work reviewable.

1. **Contracts first.** A change to an API route, a SQL migration or a setting in `config/` is
   reviewed by the platform owner before code is written.
2. **Migrations are additive.** No partner change alters or drops a legacy table; the bridge test
   (`SqlServerTests`) must stay green.
3. **Parity before switching.** A change to the evaluation runs in shadow; the parity report must
   show no unexplained differences.
4. **The lab is the regression suite.** `dynolab lab dev --check` must reproduce the committed
   numbers, or the pull request explains which number moved and why.
5. **Every pull request is green on all CI jobs**: .NET with SQL Server, Python, the lab, the
   console and the containers.
6. **AI-assisted development is welcome and declared.** Generated code meets the same tests and
   review; AGENTS.md is the shared briefing for people and coding agents.
