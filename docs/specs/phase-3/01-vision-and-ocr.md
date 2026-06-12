# Phase 3 Spec: Vision & OCR

**Component:** Backend (`/src/backend`) & Frontend (`/src/frontend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Extend ingestion and retrieval to include scanned and image-based documents while preserving grounded citation quality.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Image Enrichment

- **Goal:** Convert images into searchable knowledge artifacts.
- **Acceptance Criteria:**
    -   [ ] Upload flow accepts approved image formats.
    -   [ ] Ingestion generates textual descriptions/captions for images.
    -   [ ] Captions are chunked, embedded, and indexed in Azure AI Search with source linkage.

### 2.2. OCR for Scanned Content

- **Goal:** Extract text from scanned PDFs and image-heavy docs.
- **Acceptance Criteria:**
    -   [ ] Pipeline detects scanned/image-based content.
    -   [ ] OCR extraction path is implemented and integrated with standard chunk/index flow.
    -   [ ] Indexed metadata preserves document/page references for citations.

### 2.3. Retrieval & UI

- **Goal:** Make visual evidence visible and explainable.
- **Acceptance Criteria:**
    -   [ ] Grounded retrieval works across text and visual-derived chunks.
    -   [ ] Frontend can distinguish and label visual citations clearly.
    -   [ ] Users can identify the correct source file and location from answers.

## 3. Definition of Done (DoD)

-   [ ] Scanned/image files are retrievable through grounded chat.
-   [ ] Responses cite correct visual sources.
