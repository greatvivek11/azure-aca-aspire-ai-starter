# Phase 2 Spec: Document Ingestion & RAG Pipeline

**Component:** Backend (`/src/backend`) & Platform  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

Implement enterprise document ingestion and grounded Q&A using Blob Storage for files, Azure AI Search for retrieval, and AI Foundry/Azure AI Services model deployments for embeddings and responses.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Secure Upload Endpoint

- **Goal:** Provide secure frontend-to-blob upload without exposing storage keys.
- **Acceptance Criteria:**
    -   [ ] `POST /v1/uploads/signed-url` returns a short-lived upload contract.
    -   [ ] Upload authorization is scoped and expires quickly.
    -   [ ] SQL document metadata is persisted after upload confirmation.

### 2.2. Ingestion Worker Pipeline

- **Goal:** Extract, chunk, embed, and index content asynchronously.
- **Acceptance Criteria:**
    -   [ ] Worker handles `.txt`, `.pdf`, and `.docx` extraction.
    -   [ ] Token-aware chunking with overlap is applied.
    -   [ ] Chunks are embedded with the configured embeddings model.
    -   [ ] Chunk documents are indexed into Azure AI Search with source metadata for filtering and citations.
    -   [ ] Document ingestion status is tracked in SQL.

### 2.3. Grounded Chat Retrieval

- **Goal:** Return grounded answers with citations.
- **Acceptance Criteria:**
    -   [ ] Chat endpoint supports grounded/document mode.
    -   [ ] Grounded mode embeds the query, retrieves relevant chunks from Azure AI Search, and injects context into the answer prompt.
    -   [ ] Responses include structured citations (file, location/page/chunk metadata).

## 3. Definition of Done (DoD)

-   [ ] Documents can be uploaded, ingested, and indexed in Azure AI Search.
-   [ ] Grounded chat answers include reliable citations.
-   [ ] Full flow works in deployed Azure environment.
