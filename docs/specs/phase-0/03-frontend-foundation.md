# Phase 0 Spec: Frontend Foundation

**Component:** Frontend (`/src/frontend`)
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)
**Architectural Blueprint:** [`/docs/Architecture/Frontend-Architecture.md`](/docs/Architecture/Frontend-Architecture.md)

## 1. Goal

To scaffold a minimal Vite + React application served by a Hono Node host that acts as the project's initial status dashboard. It must verify end-to-end connectivity from the browser to backend APIs via Dapr, and by extension, the backend's connectivity to the AI model.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

-   **Goal:** A clean, modern Vite + React project setup with a lightweight Hono host ready for future feature development.
-   **Acceptance Criteria:**
    -   [ ] A Vite-based React project is created in `/src/frontend`.
    -   [ ] The frontend host is implemented in `server.js` using Hono and serves the built SPA.
    -   [ ] The project structure strictly adheres to the **feature-based organization** defined in the blueprint. The following directories **must** be created:
        -   `src/`: For the React entry points and application components.
        -   `components/`, `features/`, `hooks/`, or similar folders may be added as the UI grows, but the initial foundation should stay simple and operational.
    -   [ ] npm is the supported package manager and the project includes `package-lock.json`.
    -   [ ] The frontend host uses same-origin routes for any backend proxy behavior that should stay out of the browser.
    -   [ ] A `Dockerfile` is present in `/src/frontend` capable of building a production-ready container for the Vite build plus Hono host.

### 2.2. Status Dashboard UI

-   **Goal:** A simple UI to display the status of core system components.
-   **Acceptance Criteria:**
    -   [ ] The main page of the application is titled "ACA Aspire AI Starter - System Status".
    -   [ ] The UI displays a section for "Backend API Status".
    -   [ ] The UI displays a section for "Worker Service Status".
    -   [ ] The UI displays a section for "AI Service Status".
    -   [ ] A button labeled "Ping AI Service" is present.

### 2.3. Dapr-Powered Connectivity Checks

-   **Goal:** To prove that the frontend can communicate with backend services through the Dapr service invocation building block.
-   **Acceptance Criteria:**
    -   [ ] **Backend Health Check:**
        -   [ ] On page load, the application makes an asynchronous `GET` request to the backend's `/v1/health` endpoint.
        -   [ ] This request **must** be routed through the Hono host or Dapr sidecar, using the Dapr App ID of the backend where applicable (e.g., `http://localhost:3500/v1.0/invoke/api/method/v1/health`).
        -   [ ] The "Backend API Status" section updates to show "Healthy" (or an error state) based on the API response.
    -   [ ] **Worker Health Check:**
        -   [ ] On page load, the application makes an asynchronous `GET` request to the worker's `/v1/health` endpoint.
        -   [ ] This request **must** be routed through the Hono host or Dapr sidecar, using the Dapr App ID of the worker where applicable (e.g., `http://localhost:3500/v1.0/invoke/worker/method/v1/health`).
        -   [ ] The "Worker Service Status" section updates to show "Healthy" (or an error state) based on the API response.
    -   [ ] **AI Service Ping:**
        -   [ ] When the "Ping AI Service" button is clicked, the application makes a `GET` request to the backend's `/v1/ping-ai` endpoint via the Hono host or the Dapr sidecar.
        -   [ ] The "AI Service Status" section updates to show the response from the backend (e.g., "Pong" or an error).


## 3. Definition of Done (DoD)

-   [ ] The frontend application can be successfully containerized using its `Dockerfile`.
-   [ ] When run locally via .NET Aspire, the frontend UI loads in the browser as a Vite-built SPA served by the Hono host.
-   [ ] The status dashboard correctly reflects the health of the backend and the connectivity to the AI service by calling the backend through Dapr.
-   [ ] The status dashboard correctly reflects the health of the worker service by calling it through Dapr.
-   [ ] The application is successfully deployed to Azure Container Apps and is publicly accessible.