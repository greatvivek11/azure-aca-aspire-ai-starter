# Phase 2 Spec: Document Upload & RAG Chat UI

**Component:** Frontend (`/src/frontend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Provide a production-ready UX for enterprise document upload, ingestion tracking, and grounded chat with citations.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Document Upload Workspace

- **Goal:** Let users upload and manage knowledge files.
- **Acceptance Criteria:**
    -   [ ] A dedicated uploads/knowledge route exists.
    -   [ ] User uploads supported file types and triggers ingestion after secure upload.
    -   [ ] UI displays document lifecycle states (`Uploaded`, `Processing`, `Ready`, `Failed`).
    -   [ ] Previously uploaded documents and metadata are visible.

### 2.2. Grounded Chat Mode

- **Goal:** Clearly separate general chat and knowledge-grounded responses.
- **Acceptance Criteria:**
    -   [ ] Chat UI has explicit grounded mode/scope control.
    -   [ ] Grounded requests send required mode/scope fields to backend.
    -   [ ] UX cues make grounded responses distinct from general chat responses.

### 2.3. Citation Rendering

- **Goal:** Expose evidence for each grounded answer.
- **Acceptance Criteria:**
    -   [ ] Citations returned by API are rendered under assistant responses.
    -   [ ] Citation UI includes source file and location metadata where available.
    -   [ ] UI is extensible for chunk preview/page preview behavior.

## 3. Definition of Done (DoD)

-   [ ] User can upload file, wait for indexing, ask grounded question, and inspect citations end to end.
-   [ ] Upload/processing/chat states and errors are clear in UI.
