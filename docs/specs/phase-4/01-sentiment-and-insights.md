# Phase 4 Spec: Insights & Analysis Workflows

**Component:** Backend (`/src/backend`) & Frontend (`/src/frontend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Add repeatable enterprise analysis workflows (summarization, sentiment, classification, topic extraction) on top of uploaded knowledge assets.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Analysis API

- **Goal:** Submit and process analysis jobs.
- **Acceptance Criteria:**
    -   [ ] Backend endpoint(s) support submitting analysis requests against text/document sets.
    -   [ ] Jobs can execute asynchronously for larger workloads.
    -   [ ] Results and status are persisted in SQL.
    -   [ ] Model invocation uses configured enterprise model endpoints.

### 2.2. Insights UI

- **Goal:** Let users run and inspect analyses easily.
- **Acceptance Criteria:**
    -   [ ] Frontend has an analysis/insights route.
    -   [ ] User can submit content, run analysis, and observe progress.
    -   [ ] Results are rendered in suitable views (summary/table/chart/export).

## 3. Definition of Done (DoD)

-   [ ] At least one analysis workflow runs end to end with persisted results.
-   [ ] UI clearly presents outputs and failures.
