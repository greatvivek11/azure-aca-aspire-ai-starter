# Orchestration Decision Record

Date: 2026-06-06
Status: Accepted
Owner: Project maintainers

## Context

The project currently has two overlapping orchestration paths:

- Aspire AppHost as local orchestrator.
- Docker Compose stack as an alternate orchestrator.

This has caused drift across service ports, dependency wiring, runtime versions, and startup behavior between host runs, devcontainer runs, and cloud deployment expectations.

## Decision

Adopt a single canonical orchestration model:

1. Local orchestration (host and devcontainer): Aspire AppHost.
2. Cloud deployment (ACA): azd + IaC deployment of real workloads.
3. Docker Compose: optional and limited to workspace/devcontainer infrastructure support only, not application orchestration.

## Why

- Prevents topology drift between development modes.
- Keeps service graph, Dapr wiring, and environment contracts consistent.
- Aligns with cloud-native deployment on ACA where Azure is the production control plane.
- Preserves developer flexibility (host or devcontainer) without duplicating app orchestration logic.

## Explicit Clarifications

### Is running Aspire in a container wrong?

No, for local development it is acceptable to run AppHost in a container (for example inside a devcontainer), as long as it orchestrates the same service topology.

### Is hosting Aspire AppHost as a production service in ACA recommended?

No. AppHost should not be treated as a production runtime control plane. In production, deploy business workloads directly (frontend, backend, worker, data dependencies) and use Azure-native observability.

## Consequences

### Positive

- One source of truth for orchestration.
- Simpler onboarding and troubleshooting.
- Better CI/CD reproducibility.

### Trade-offs

- Existing Compose app stack must be retired or re-scoped.
- Initial migration effort to align ports, images, and docs.

## Scope Boundaries

In-scope now:

- Orchestration convergence and environment parity.
- Critical/high runtime fixes.
- Documentation realignment.

Out-of-scope now:

- Feature implementation beyond health-level functionality.
- Broader AI pipeline and data platform implementation.
