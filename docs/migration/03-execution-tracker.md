# Orchestration Migration Execution Tracker

Date opened: 2026-06-06
Owner: Project maintainers

## How to use

- Update status at the end of each orchestration PR.
- Link PR number and validation evidence.
- Keep this file focused on orchestration convergence only.

Status legend:

- Not Started
- In Progress
- Blocked
- Done

## Work items

| ID | Item | Priority | Status | PR | Evidence |
|---|---|---|---|---|---|
| A1 | Dapr app-port and service-port convergence | Critical | Done | local | `src/aspire/AppHost.cs`; Dapr invoke checks returned 200 for backend and worker |
| A2 | Worker framework and image alignment | Critical | Done | local | `src/worker/Dockerfile` aligned to .NET 10 images; solution build passed |
| B1 | Root compose re-scope (non-primary orchestration) | High | Done | local | Root-level `docker-compose.yml`, `compose.yaml`, and `compose.debug.yaml` removed; devcontainer compose remains under `.devcontainer/` |
| B2 | Dapr components path validity | High | Done | local | `src/components/.gitkeep` added and AppHost sidecars pinned to repo components path |
| B3 | Devcontainer SQL connectivity and platform support | High | Done | local | Devcontainer F5 now runs full topology through AppHost; dashboard shows backend/frontend/worker + Dapr + sql + redis all `Running` |
| C1 | azd and infra baseline for ACA | High | In Progress | local | `azure.yaml` and `infra/` baseline added; `azd version` confirmed (`1.25.5`), pending `az login`, `azd provision --preview`, and `azd up` validation |

## Validation matrix

| Scenario | Expected result | Status | Notes |
|---|---|---|---|
| Host F5 | AppHost starts full topology successfully | Done | Aspire dashboard reachable and Dapr sidecars running |
| Devcontainer F5 | Same topology starts via AppHost | Done | Aspire dashboard confirms backend/frontend/worker sidecars plus sql/redis are `Running` |
| Dapr health routes | Frontend to backend and worker invocation works | Done | Dapr invoke endpoints returned `HTTP/1.1 200 OK` |
| Local SQL | Connection succeeds in both host and devcontainer modes | Done | SQL now managed by AppHost using `mcr.microsoft.com/mssql/server:2022-latest`; service remains `Running` |
| azd preview | Infra/app preview validates with no blockers | Not Started | `azd` installed; pending Azure login and preview execution |
| azd deployment | Minimal workload deploys to ACA successfully | Not Started | Pending successful preview and Azure auth context |

## Decision log (delta only)

### 2026-06-06

- Adopted single canonical orchestration strategy: Aspire local, azd for cloud deployment.
- Limited Docker Compose to devcontainer infrastructure support only.
- Deferred all feature work until critical and high orchestration issues are resolved.
- Completed A1, A2, B1, B2, B3 implementation and host-mode runtime validation.
- Added azd and infra baseline files for C1.
- Completed devcontainer runtime validation with sql and redis as Aspire-managed local dependencies.
- Updated C1 blocker: azd is installed; remaining work is Azure authentication plus preview/deploy validation.
