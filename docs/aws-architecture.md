# Deployment on AWS

| Part | Service | Notes |
|---|---|---|
| Platform API (C#) | ECS on Fargate, behind an ALB | `src/Rdo.Api/Dockerfile`; stateless; health at `/health` |
| Dyno analytics (Python) | ECS on Fargate, internal | `analytics/Dockerfile`; called only by the API |
| Database | Amazon RDS for SQL Server | the legacy database; the platform adds the `rdo` schema (additive migrations) |
| Legacy desktop application | Amazon AppStream 2.0 (or the existing Citrix) | stays until its last screen has moved (ADR 1) |
| Raw dyno files | Amazon S3 | per-run stop data, kept for re-evaluation |
| Secrets | AWS Secrets Manager | `RDO_SQLSERVER`, `RDO_API_KEYS`, `ANTHROPIC_API_KEY` (or Amazon Bedrock with an IAM role) |
| Console | S3 + CloudFront | static build of `web/` |
| Logs and metrics | CloudWatch | monitor stops, intake outcomes, parity differences as metrics |

Not built in this repository: Terraform for the above, SSO for people (the API uses keys), and the
dyno software's push of stops into `/api/runs/{orderId}/stops`.
