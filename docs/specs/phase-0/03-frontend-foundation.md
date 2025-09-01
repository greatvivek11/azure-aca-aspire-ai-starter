# Phase 0 Spec: Frontend Foundation

**Component:** Frontend (`/src/frontend`)
**Source Plan:** [`docs/plans/Phased_Plan.md`](/docs/plans/Phased_Plan.md)
**Architectural Blueprint:** [`/docs/architecture/Frontend-Architecture.md`](/docs/architecture/Frontend-Architecture.md)

## 1. Goal

To scaffold a minimal Next.js 19 SSR application that serves as a "status dashboard" for the project's foundation. It must verify end-to-end connectivity from the browser to the backend API via Dapr, and by extension, the backend's connectivity to the AI model.

## 2. Feature Breakdown & Acceptance Criteria

### 2.1. Project Scaffolding

-   **Goal:** A clean, modern Next.js SSR project setup ready for future development with BFF pattern.
-   **Acceptance Criteria:**
    -   [ ] A new **Next.js 19** project is created in `/src/frontend`, replacing any existing Express-based application.
    -   [ ] The project structure strictly adheres to the **feature-based organization** defined in the blueprint. The following directories **must** be created:
        -   `app/`: For Next.js App Router pages and layouts.
        -   `components/`: For shared UI components, including a `ui/` sub-folder for `shadcn/ui` components.
        -   `features/`: For feature-specific components (e.g., `chat/`, `documents/`).
        -   `lib/`: For utility functions and constants.
        -   `hooks/`: For custom React hooks.
        -   `stores/`: For Zustand state management stores.
    -   [ ] `Tailwind CSS` is integrated for styling.
    -   [ ] `shadcn/ui` is initialized, and its components are added to `components/ui/`.
    -   [ ] The project **must** use **Next.js Route Handlers** for its Backend-for-Frontend (BFF) layer, which will proxy calls to the backend via Dapr.
    -   [ ] A `Dockerfile` is present in `/src/frontend` capable of building a production-ready, standalone Next.js container.

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
-   [ ] When run locally via .NET Aspire, the frontend UI loads in the browser with SSR.
-   [ ] The status dashboard correctly reflects the health of the backend and the connectivity to the AI service by calling the backend through Dapr.
-   [ ] The status dashboard correctly reflects the health of the worker service by calling it through Dapr.
-   [ ] The application is successfully deployed to Azure Container Apps and is publicly accessible with SSR.