# Phase 5 Spec: Long-Term Memory

**Component:** Backend (`/src/backend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Implement durable memory that summarizes prior conversations and retrieves only relevant context for future chats.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Memory Creation

- **Goal:** Convert conversation history into compact memory records.
- **Acceptance Criteria:**
    -   [ ] Background workflow summarizes completed/inactive conversations.
    -   [ ] Summaries are embedded and indexed into a dedicated AI Search memory index.
    -   [ ] SQL stores memory ownership, linkage, and lifecycle metadata.

### 2.2. Memory Retrieval

- **Goal:** Use memory safely at conversation start and during follow-ups.
- **Acceptance Criteria:**
    -   [ ] Memory retrieval uses user/workspace-scoped filters.
    -   [ ] Top relevant summaries are injected into prompts with strict bounds.
    -   [ ] Retrieval adds personalization without introducing significant latency.

## 3. Definition of Done (DoD)

-   [ ] Completed conversations produce searchable memory summaries.
-   [ ] New chats can leverage relevant prior context in a controlled way.
