# Phase 6 Spec: Agents & Tooling

**Component:** Backend (`/src/backend`) & Frontend (`/src/frontend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Enable the assistant to execute enterprise tasks via approved tools with explicit transparency.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Tool Layer

- **Goal:** Provide safe, auditable tools.
- **Acceptance Criteria:**
    -   [ ] Tool contracts are defined for search retrieval, document lookup, read-only SQL querying, and export operations.
    -   [ ] Input validation, auth checks, and guardrails are enforced per tool.
    -   [ ] Tool outputs are structured for composable workflows.

### 2.2. Multi-Step Execution

- **Goal:** Support planner/agent orchestration over tools.
- **Acceptance Criteria:**
    -   [ ] Backend can execute multi-step tasks involving multiple tools.
    -   [ ] Execution status for each step is captured and returned.
    -   [ ] Failures are explicit and do not hide partial outcomes.

### 2.3. Frontend Transparency

- **Goal:** Show users what happened.
- **Acceptance Criteria:**
    -   [ ] UI shows a timeline of tool steps and statuses.
    -   [ ] Tool name and sanitized inputs/outputs are visible.
    -   [ ] Final result and failure states are clearly communicated.

## 3. Definition of Done (DoD)

-   [ ] User can run a multi-step enterprise task and see tool-driven execution end to end.
