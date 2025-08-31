# Phase 0 Spec: Frontend Foundation

**Component:** Frontend (`/frontend`)  
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)

## 1. Goal

To scaffold a minimal React 19 application that serves as a "status dashboard" for the project's foundation. It must verify end-to-end connectivity from the browser to the backend API via Dapr, and by extension, the backend's connectivity to the AI model.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

-   **Goal:** A clean, modern React project setup ready for future development.
-   **Acceptance Criteria:**
    -   [ ] A new React 19 project is created in the `/frontend` directory using `Vite`.
    -   [ ] `Tailwind CSS` is integrated for styling.
    -   [ ] `shadcn/ui` is initialized for a base component library.
    -   [ ] A `Dockerfile` is present in the `/frontend` directory, capable of building a production-ready, static web server container image (e.g., using Nginx).

### 2.2. Status Dashboard UI

-   **Goal:** A simple UI to display the status of core system components.
-   **Acceptance Criteria:**
    -   [ ] The main page of the application is titled "AI Hub - System Status".
    -   [ ] The UI displays a section for "Backend API Status".
    -   [ ] The UI displays a section for "Worker Service Status".
    -   [ ] The UI displays a section for "AI Service Status".
    -   [ ] A button labeled "Ping AI Service" is present.

### 2.3. Dapr-Powered Connectivity Checks

-   **Goal:** To prove that the frontend can communicate with the backend through the Dapr service invocation building block.
-   **Acceptance Criteria:**
    -   [ ] **Backend Health Check:**
        -   [ ] On page load, the application makes an asynchronous `GET` request to the backend's `/v1/health` endpoint.
        -   [ ] This request **must** be routed through the Dapr sidecar, using the Dapr App ID of the backend (e.g., `http://localhost:3500/v1.0/invoke/aihub-backend/method/v1/health`).
        -   [ ] The "Backend API Status" section updates to show "Healthy" (or an error state) based on the API response.
    -   [ ] **Worker Health Check:**
        -   [ ] On page load, the application makes an asynchronous `GET` request to the worker's `/v1/health` endpoint.
        -   [ ] This request **must** be routed through the Dapr sidecar, using the Dapr App ID of the worker (e.g., `http://localhost:3500/v1.0/invoke/aihub-worker/method/v1/health`).
        -   [ ] The "Worker Service Status" section updates to show "Healthy" (or an error state) based on the API response.
    -   [ ] **AI Service Ping:**
        -   [ ] When the "Ping AI Service" button is clicked, the application makes a `GET` request to the backend's `/v1/ping-ai` endpoint via the Dapr sidecar.
        -   [ ] The "AI Service Status" section updates to show the response from the backend (e.g., "Pong" or an error).

## 3. Definition of Done (DoD)

-   [ ] The frontend application can be successfully containerized using its `Dockerfile`.
-   [ ] When run locally via `docker compose`, the frontend UI loads in the browser.
-   [ ] The status dashboard correctly reflects the health of the backend and the connectivity to the AI service by calling the backend through Dapr.
-   [ ] The status dashboard correctly reflects the health of the worker service by calling it through Dapr.
-   [ ] The application is successfully deployed to Azure Container Apps and is publicly accessible.