# Target Operating Model (Host, Devcontainer, ACA)

Date: 2026-06-06

## Goal

Provide near-identical developer experience across host and devcontainer while keeping production deployment architecture clean and Azure-native.

## Canonical Modes

### Mode A: Local Host Development

- Developer runs AppHost directly.
- AppHost composes frontend, backend, worker, Dapr sidecars, and local data dependencies.
- F5 experience targets AppHost.

### Mode B: Devcontainer Development

- Developer opens repo in devcontainer.
- AppHost runs inside the devcontainer as process orchestrator.
- AppHost controls the same application topology as Mode A.
- Docker Compose in .devcontainer is permitted only for workspace plumbing and dependency containers.

### Mode C: Cloud Deployment (ACA)

- Deployment uses azd and IaC.
- Frontend, backend, worker are deployed as first-class ACA workloads.
- Azure-native services provide monitoring and operations.
- AppHost is not deployed as a production service.

## Parity Rules

1. Same service names and Dapr app IDs across modes.
2. Same app ports and target ports across all modes.
3. Same environment variable names and secret keys.
4. Same connection string key names for all local/cloud manifests.
5. One source of truth for app topology definition (Aspire).

## Data Dependency Strategy (SQL)

Required outcome: every contributor can run the project on Windows or macOS, with or without devcontainer.

### Baseline

- Local SQL dependency is containerized.
- Connection contract is stable regardless of platform.

### Platform handling

- Use explicit platform profiles when image differences are required (amd64 vs arm64).
- Keep app-level connection keys unchanged across profiles.
- Avoid mode-specific connection semantics like host-only localhost assumptions.

## Production Observability

- Use Azure Monitor, Log Analytics, and Application Insights.
- Do not rely on Aspire dashboard as production operations dashboard.

## Acceptance Criteria

1. Host mode and devcontainer mode start the same app topology via AppHost.
2. SQL dependency works in both modes with documented platform profiles.
3. ACA deployment path uses azd and mirrors app contracts used locally.
4. Onboarding doc has one default path and one clearly labeled fallback path.
