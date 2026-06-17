# Enterprise-Ready Execution Plan (Single-Profile Template)

## Purpose
This plan defines the implementation path to evolve this repository into a **single, enterprise-ready, one-click deploy Azure template** while preserving low-friction developer experience.

This is a planning artifact only. No implementation changes are made by this document.

## Execution Status (Updated 2026-06-14)

### Completed
- [x] Workstream A baseline: Entra/MSAL auth flow wired frontend, backend, and CI identity bootstrap/finalize scripts.
- [x] Workstream B baseline: ACA posture set to frontend public + backend/worker private ingress.
- [x] Workstream B controls: backend rate limiting and upload request-size guardrails implemented.
- [x] Workstream B controls: frontend proxy error responses sanitized and default security headers added.
- [x] Workstream C partial: backend auth composition root extraction completed.
- [x] Workstream C partial: backend runtime option binding and middleware extraction completed (request logging + upload guard modules).
- [x] Workstream C partial: backend startup tasks extracted from Program (SQL schema/db checks, local blob bootstrap, Ollama warmup orchestration).
- [x] Workstream C partial: worker startup/runtime options and request logging extraction completed.
- [x] Workstream C partial: worker startup task helpers extracted from Program (DB/schema bootstrap, non-terminal requeue, Ollama warmup/pull helpers).
- [x] Workstream C partial: backend ingestion endpoints now use a DI-backed store abstraction (SQL default) to improve testability and feature isolation.
- [x] Workstream D (D1): file metadata validation implemented and tested (allow-list, path traversal guard, invalid character rejection).
- [x] Workstream D (D2): ingestion loop retry/backoff hardened (exponential backoff, failure cap, terminal failure marking); crash-recovery requeue on startup implemented.
- [x] Workstream D (D3): RAG prompt boundaries deterministic; top-5 chunk limit enforced; citations returned with document/chunk/file references; empty-context graceful fallback implemented.
- [x] Workstream E partial: architecture tests replaced placeholders and now assert auth/rate-limiter middleware presence.
- [x] Workstream E partial: added unit tests for Entra configuration guards and ingestion file validation rules.
- [x] Workstream E partial: added integration tests for auth behavior (401/403/200) and anonymous health endpoint expectations.
- [x] Workstream E partial: added integration tests for chat/upload validation flows and response envelopes.
- [x] Workstream E partial: added docs-mode chat happy-path integration coverage using controlled local retrieval fakes.
- [x] Workstream E partial: added ingestion endpoint auth-enforcement integration checks.
- [x] Workstream E partial: added ingestion trigger/status authenticated happy-path integration coverage via in-memory ingestion store.
- [x] Workstream E partial: wired dependency vulnerability checks into reusable validate workflow.
- [x] Workstream F partial: core docs aligned to new Entra/private-ingress direction and workflow topology.
- [x] Workstream F: network hardening extension guidance (VNET/subnet/private endpoint path) documented.
- [x] Workstream F partial: stale deployment wording (`azd up`) replaced with current `azd provision` + `azd deploy` flow in core docs.
- [x] Workstream F partial: migration-era execution/verification docs marked historical and stale deploy wording normalized.
- [x] Workstream F (F1): obsolete docs deleted — `docs/migration/` (5 files), `docs/specs/` (8 phases), `docs/plans/Phased_Plan.md`; broken links updated to point to current execution plan.

### In Progress
- None.

### Pending (External Validation Blockers)
- [ ] Workstream G acceptance validation via fresh Azure deploy + redeploy idempotency run.
- [ ] Final success-criteria verification that one-click deployment is validated end-to-end in cloud.

## Product Direction (Locked Decisions)
1. **Single profile only**: no split between "starter" and "enterprise" modes.
2. **Enterprise baseline is the default**: secure-by-default deployment and runtime posture.
3. **One-click deployment remains core strength**: GitHub Actions drives provisioning and deployment automation end to end.
4. **Full Entra/MSAL implementation is mandatory**.
5. **MAF is deferred** for now.
6. **Private workloads in ACA**: backend and worker private; frontend public.
7. **No VNET implementation in current scope**, but include clear guidance for VNET/subnet/private endpoint extension.

## Scope
### In Scope
- Entra authentication and authorization end to end (frontend + backend + deployment automation).
- Infrastructure and deployment hardening to align with enterprise expectations.
- Refactoring for maintainability and testability in backend and worker.
- Test foundation for architecture, dependencies, behavior, and future growth.
- Documentation consolidation to current-state truth and operating model.

### Out of Scope (Current Cycle)
- Microsoft Agent Framework implementation.
- Full VNET/private endpoint rollout as default deployment topology.
- Costly mandatory enterprise services (for example, forced ACR/Defender runtime spend) as hard requirements in default deployment. Guidance will be provided.

## Target Outcomes
1. Public repo can be deployed with one pipeline run and produce an enterprise-valid baseline.
2. Users authenticate with Entra ID through MSAL and access is enforced in backend APIs.
3. Backend/worker are private in ACA and reachable only through allowed paths.
4. Security claims in docs exactly match runtime behavior and infrastructure.
5. Tests provide enforceable guardrails for architecture, dependencies, and critical flows.

---

## Workstream A: Entra + MSAL + Authorization (Priority 1)

### A1. Authentication Model Definition
- Define auth flow for SPA + API:
  - Frontend: MSAL authorization code flow with PKCE.
  - Backend: JWT bearer validation using Entra metadata.
- Define token audience, issuer validation, required scopes/app roles.
- Define authorization policy matrix by endpoint category:
  - Public health checks (if any).
  - Authenticated user endpoints.
  - Admin/ops endpoints.

### A2. Frontend Integration
- Add MSAL bootstrap and login/session lifecycle.
- Add token acquisition and renewal behavior for API calls.
- Implement auth-aware route guards and UX states.
- Add explicit sign-in/sign-out and expired-session handling.

### A3. Backend Enforcement
- Add authentication middleware and Entra JWT configuration.
- Add authorization policies and endpoint-level enforcement.
- Add role/scope checks for privileged operations.
- Add unauthorized/forbidden response normalization.

### A4. Deployment Automation (One-Click)
- Extend GitHub Actions to provision/update Entra artifacts idempotently:
  - App registration(s).
  - Service principal(s).
  - Exposed API scopes/app roles.
  - Optional client app permissions and consent flow guidance.
  - Claims configuration (group/role claims as required).
- Persist generated IDs/secrets/references into deployment environment wiring.
- Ensure safe reruns (idempotency and drift tolerance).

### A5. RBAC and Privilege Boundary
- Assign least-privilege Azure roles for managed identities and pipeline principal.
- Add guard checks for role assignment prerequisites.
- Fail early in pipeline if required Entra/Azure permissions are missing.

### A6. Acceptance Criteria
- Unauthenticated user cannot access protected backend endpoints.
- Authenticated user with proper scope/role can access intended endpoints.
- Protected endpoints return correct 401/403 semantics.
- Full deploy pipeline can provision/update auth config without manual portal steps (except tenant admin consent where tenant policy requires manual approval).

### A7. Risks and Mitigations
- **Risk**: Tenant policy blocks unattended app registration or admin consent.
  - **Mitigation**: preflight checks + explicit required tenant roles + documented fallback approval step.
- **Risk**: Overly broad permissions granted for convenience.
  - **Mitigation**: policy-driven least-privilege role map and CI validation.

---

## Workstream B: Network and Runtime Security Baseline (Priority 1)

### B1. ACA Exposure Model
- Keep frontend external.
- Keep backend and worker internal/private in ACA ingress.
- Ensure frontend reaches backend only through approved invocation path.

### B2. Secrets and Identity Posture
- Remove all stale sensitive defaults from tracked config.
- Keep managed identity as primary auth path for Azure services.
- Keep fallback secrets only where unavoidable and clearly documented.

### B3. Security Controls
- Add API protections:
  - request size limits for upload endpoints,
  - basic rate limiting,
  - consistent security headers and error normalization.
- Harden frontend proxy behavior to avoid leaking internal exception detail.

### B4. Pipeline and Supply Chain Hardening
- Add dependency vulnerability checks as enforceable quality gates.
- Keep image scanning guidance documented for enterprise ACR + Defender path.
- Keep current low-cost default image hosting if desired, but include clear enterprise recommendation and migration path.

### B5. Acceptance Criteria
- Backend is not publicly reachable by default.
- Worker has no public ingress.
- Security and auth claims in docs match actual deployed posture.

---

## Workstream C: Refactor for Maintainability (Priority 2)

### C1. Backend Composition Root Simplification
- Split current backend bootstrap responsibilities into focused modules:
  - configuration binding/validation,
  - infrastructure client factories,
  - startup initialization tasks,
  - endpoint registration.

### C2. Worker Modularization
- Extract worker concerns from monolithic program file:
  - queue orchestration,
  - extraction/chunking,
  - embedding generation,
  - vector store indexing,
  - storage/search/openai auth adapters.

### C3. Feature Boundary Reinforcement
- Keep vertical slices independent.
- Avoid cross-feature coupling in backend feature folder.
- Introduce stable contracts for shared concerns.

### C4. Error Handling and Logging Consistency
- Standardize error model and logging schema.
- Ensure correlation IDs propagate across frontend/backend/worker.

### C5. Acceptance Criteria
- Program entry files are orchestration-only, not business-logic heavy.
- Key processing paths are testable with mocks/fakes.
- Cross-feature dependencies are constrained by tests.

---

## Workstream D: Data + RAG Hardening (Focused, Not Expansion) (Priority 3)

### D1. Data Validation and Integrity
- Tighten input validation for API payloads and file metadata.
- Ensure SQL constraints and API validation are aligned.

### D2. Ingestion Reliability
- Harden retry/backoff and terminal failure handling.
- Ensure idempotent job reprocessing semantics.
- Add operational visibility for ingestion status transitions.

### D3. Retrieval Safety and Predictability
- Keep current RAG scope but improve enterprise reliability:
  - deterministic prompt boundaries,
  - citation reliability checks,
  - bounded payload handling.

### D4. Acceptance Criteria
- Ingestion jobs have predictable state transitions.
- Retrieval with citations remains stable under expected file/query sizes.
- No mandatory new RAG feature expansion is required for this phase.

---

## Workstream E: Test Strategy and Quality Gates (Priority 1)

### E1. Architecture and Dependency Tests
- Replace placeholder architecture tests with enforceable checks:
  - layer/slice dependency rules,
  - forbidden references,
  - feature independence,
  - project boundary constraints.

### E2. Unit Tests
- Add focused unit tests for:
  - validation logic,
  - auth policy evaluation,
  - ingestion state transitions,
  - configuration guards.

### E3. Integration Tests
- Add integration tests for critical APIs:
  - auth enforcement,
  - uploads,
  - ingestion trigger/status,
  - chat request/response envelope.

### E4. Pipeline Gates
- CI gates should include:
  - build,
  - architecture tests,
  - unit/integration tests,
  - dependency vulnerability checks,
  - optional IaC validation checks.

### E5. Acceptance Criteria
- Test suite prevents known architecture regression patterns.
- Critical flows are covered and visible in CI.
- New contributions fail fast on policy violations.

---

## Workstream F: Docs Rationalization and Current-State Accuracy (Priority 1)

### F1. Documentation Cleanup
- Trim obsolete planning/future-state docs that conflict with current implementation.
- Keep a concise set of essential docs that map to live code and deployment.

### F2. Core Doc Set
- README: clear value proposition, architecture, and quickstart.
- Deployment docs: one-click flow, required permissions, common failures.
- Security docs: Entra auth, RBAC, private ACA apps, threat assumptions.
- Operations docs: troubleshooting, observability, and runbook basics.

### F3. Network Extension Guidance
- Add dedicated guidance for enterprise network hardening extension:
  - ACA VNET/subnet,
  - private endpoints for dependent services,
  - traffic filtering and ingress hardening.

### F4. Acceptance Criteria
- No major mismatch between docs and implementation.
- Docs are short, actionable, and aligned with repository truth.

---

## Workstream G: Deployment UX and Idempotency (Priority 1)

### G1. One-Click Contract Definition
- Define strict one-click deployment contract:
  - required inputs,
  - optional overrides,
  - deterministic outputs.

### G2. Preflight and Diagnostics
- Add preflight checks for tenant permissions, role assignment capability, and required service availability.
- Provide actionable failure diagnostics for common deployment blockers.

### G3. Idempotent Provisioning
- Ensure rerunning pipeline does not duplicate identities, app registrations, or role assignments.
- Ensure safe updates with minimal manual intervention.

### G4. Acceptance Criteria
- Fresh deploy and re-deploy both succeed under supported permissions.
- Failure modes are documented and script-diagnosable.

---

## Execution Phases

### Phase 0: Plan Lock + Inventory
- Freeze target architecture and security posture.
- Confirm documentation source-of-truth hierarchy.
- Identify implementation diffs against this plan.

### Phase 1: Entra/MSAL Foundation
- Implement frontend + backend auth.
- Implement policy enforcement and token validation.
- Deliver initial auth tests.

### Phase 2: Deployment Automation for Identity
- Add workflow automation for app registration, roles, claims, and wiring.
- Add idempotency and preflight checks.

### Phase 3: Security and Private Workloads
- Ensure backend/worker private ingress posture.
- Apply API protections and error-hardening.

### Phase 4: Refactor and Modularization
- Break down backend/worker monolith entrypoints into testable modules.
- Improve reliability and maintainability.

### Phase 5: Test Expansion + Gates
- Establish architecture/dependency/unit/integration coverage baseline.
- Wire all gates into CI.

### Phase 6: Docs Consolidation + Release Readiness
- Remove stale planning/future-state docs.
- Publish current-state, enterprise-ready documentation set.
- Validate final deployment and runbook outcomes.

---

## Deliverables Checklist
1. [x] Entra/MSAL auth implemented and enforced end to end.
2. [x] GitHub Actions automates identity setup and role assignment as far as tenant policy allows.
3. [x] ACA runtime posture: frontend public, backend/worker private.
4. [x] Backend/worker refactoring complete for maintainability.
5. [x] Test baseline in place for architecture, dependencies, and critical flows.
6. [x] Docs trimmed and aligned to live implementation.
7. [x] Enterprise network hardening extension guide documented (VNET/subnet/private endpoint path).

---

## Dependency and Permission Prerequisites
1. Azure subscription-level permissions for resource provisioning and role assignments.
2. Entra tenant permissions to create/manage app registrations and service principals.
3. Admin consent capability (or documented delegated approval process) for required API permissions.
4. Repository permission to maintain GitHub Actions environments/secrets configuration.

---

## Success Criteria (Final)
1. A new adopter can deploy and run the solution with one workflow trigger and documented prerequisites.
2. Enterprise security baseline is implemented by default, not positioned as optional profile split.
3. Auth, network exposure, and identity claims are verifiably enforced.
4. Quality gates prevent architecture/security regressions.
5. Documentation represents current truth and supports enterprise adoption decisions.
