# Critical and High Remediation Plan (Orchestration First)

Date: 2026-06-06

This plan focuses only on critical/high issues needed to stabilize orchestration before feature work.

## Track A: Critical Runtime Alignment

### A1. Dapr app-port and service-port convergence

Priority: Critical

Tasks:

1. Align backend Dapr sidecar app-port with actual backend listening port.
2. Validate worker and frontend Dapr app-port contracts.
3. Add a port contract table to docs and keep it versioned.

Definition of done:

- Service invocation works consistently in host and devcontainer runs.

### A2. Worker framework/image alignment

Priority: Critical

Tasks:

1. Align Worker target framework with container SDK/runtime base images.
2. Ensure entrypoint matches publish strategy.
3. Validate startup in both debug and release container flows.

Definition of done:

- Worker builds and starts consistently across host and container paths.

## Track B: High Orchestration Convergence

### B1. Compose stack re-scope

Priority: High

Tasks:

1. Retire root Docker Compose as primary app orchestrator.
2. Keep only devcontainer infrastructure Compose usage (if needed).
3. Document this as policy in migration docs.

Definition of done:

- No second app topology remains in root compose for regular development.

### B2. Dapr components path validity

Priority: High

Tasks:

1. Ensure Dapr components directory exists and is referenced correctly.
2. Validate sidecar startup logs for component load success.

Definition of done:

- No missing-path Dapr component errors during startup.

### B3. Devcontainer SQL connectivity and platform support

Priority: High

Tasks:

1. Fix DB host usage from app container context.
2. Remove implicit localhost assumptions for cross-container SQL access.
3. Split SQL container profile for architecture-specific requirements.
4. Ensure tooling install scripts are platform-aware.

Definition of done:

- Devcontainer post-create succeeds on macOS ARM and Windows x64 paths.

## Track C: Deployment Path Readiness (High)

### C1. Add azd and infra baseline

Priority: High

Tasks:

1. Introduce azd project scaffold and environment configuration.
2. Add infra folder with minimal ACA deployment IaC.
3. Ensure local contract names match cloud deployment contract names.

Definition of done:

- Preview and deployment commands work for a minimal health-only workload.

## Sequence and Gates

### Gate 1

Complete A1 and A2 before any additional feature work.

### Gate 2

Complete B1 through B3 and verify host/devcontainer parity.

### Gate 3

Complete C1 and verify local-to-cloud contract continuity.

## Verification Checklist

1. Host F5 starts full app topology via AppHost.
2. Devcontainer F5 starts same topology via AppHost.
3. Health endpoints are reachable through expected Dapr routes.
4. SQL connectivity passes in both modes.
5. ACA deployment preview and deployment complete using azd.

## Risks and Mitigations

- Risk: hidden dependency on root compose scripts.
  - Mitigation: explicit deprecation and migration notes.

- Risk: architecture-specific SQL image behavior.
  - Mitigation: profile split plus stable connection contract.

- Risk: docs drift returns during rapid iteration.
  - Mitigation: update this folder as part of every orchestration PR.
