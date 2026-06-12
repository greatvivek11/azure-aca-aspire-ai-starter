# 🖥️ Frontend Architecture

This document outlines the architectural decisions, technology stack, and design principles for the frontend of the AI-Powered Knowledge Hub. It distinguishes between the current implementation in the repository and the target direction for later phases.

## 🎯 Core Principles

1. **Performance First**: Given the scale-to-zero nature of Azure Container Apps, optimize for a fast First Contentful Paint (FCP) and predictable cold-start behavior.
2. **Developer Experience**: Favor a small, understandable toolchain that is easy to build, run, and containerize.
3. **Thin Frontend Host**: Keep the user experience as a Vite-built SPA while using a small **Hono** Node host for static serving, API proxying, and environment-specific integration.
4. **Pragmatic State Management**: Use local React state until the UI complexity justifies a dedicated state or data-fetching library.
5. **Progressive Architecture**: Document future UI and AI-specific patterns explicitly as evolution targets, not as current implementation facts.

## 🛠️ Technology Stack

This section reflects the current frontend stack checked into the repository.

| Category            | Technology                      | Justification                                                                                                                                                          |
| ------------------- | ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Runtime/Toolkit** | **Node.js 20 + npm**            | The Docker build and runtime both use Node 20 and npm, matching the checked-in `package-lock.json` and deployment flow.                                               |
| **UI Framework**    | **React 18.3**                  | The current UI is built as a small React application with hooks and `StrictMode`.                                                                                      |
| **Build Tool**      | **Vite 5.4**                    | Vite keeps the SPA development loop fast and aligns with the current frontend build and packaging model.                                                               |
| **Host Layer**      | **Hono 4.x**                    | Hono provides a lightweight Node host for static asset serving, health endpoints, and backend proxy routes without adopting a full SSR framework.                     |
| **Rendering**       | **Client-rendered SPA**         | The browser renders the application, while the Hono host handles operational endpoints and same-origin integration points.                                             |
| **Styling**         | **Plain CSS**                   | The current frontend uses `App.css` and does not yet use Tailwind, shadcn/ui, or a broader design-system layer.                                                       |
| **State**           | **React state/hooks**           | Customer data, form state, saving state, and error state are managed locally inside the app component.                                                                 |
| **Language**        | **JavaScript (ES Modules)**     | The frontend currently runs as an ESM JavaScript application in both the Vite bundle and the Hono host.                                                               |

## Current Implementation Snapshot

The current frontend is an operational foundation rather than the final AI product UI.

* The Hono host in `server.js` serves the built Vite assets, exposes `/health`, and proxies CRUD requests under `/api/customers` to the backend.
* The React app in `src/App.jsx` renders a simple customer records screen with list, create, and delete flows.
* Data loading uses direct `fetch` calls to same-origin routes exposed by the Hono host.
* There is no client-side router, auth flow, design-system layer, or dedicated data-fetching library in the current implementation.
* Application Insights is wired in the host process, not in the browser bundle.

## 🎨 UI Direction

The current UI is intentionally simple so the repository can validate frontend-to-backend connectivity. Later phases can evolve toward a more task-oriented workspace once chat, documents, and insights are implemented.

### Current UI

The current screen is a two-card operational dashboard:

* A customer table that loads records from `/api/customers`
* A create-record form that posts new entries back through the Hono host to the backend

### Future Direction

As chat, document workflows, and insights land, the frontend can grow into a multi-panel workspace better suited to the product vision.

```
+------+-------------------------+-----------------------------------------+
|      |                         |                                         |
| Icon |   Navigation / History  |           Main Content Area             |
| Side |       (Panel 2)         |                (Panel 3)                |
| Bar  |                         |                                         |
| (P1) | - Chat History          | - Chat Interface                        |
|      | - Document Explorer     | - Document Viewer                       |
|      | - ...                   | - Insights Dashboard                    |
|      |                         |                                         |
+------+-------------------------+-----------------------------------------+
```

1. **Panel 1: Icon Sidebar (Far Left)**

   * A thin, static vertical bar containing icons for the main application modules:

     * 💬 Chat
     * 📚 Documents
     * 📊 Insights
     * ⚙️ Settings
2. **Panel 2: Navigation / History Panel**

   * The content of this panel is contextual, driven by the selection in the Icon Sidebar.
   * If "Chat" is active, it lists previous chat sessions.
   * If "Documents" is active, it shows a searchable file tree of ingested documents.
3. **Panel 3: Main Content Area**

   * This is the primary workspace where all user interaction takes place. It will render the active view, whether it's the chat window, a document reader, or the data visualization dashboard.

## 📁 Project Structure

The current frontend structure is intentionally small.

```
/src/frontend/
|
├── index.html                  // Vite entry HTML
├── package.json                // npm scripts and dependencies
├── package-lock.json           // Locked npm dependency graph
├── server.js                   // Hono host and backend proxy routes
├── vite.config.js              // Vite build configuration
└── src/
   ├── App.jsx                 // Main React application shell
   ├── App.css                 // Frontend styles
   └── main.jsx                // Browser entry point
```

### Key Architectural Decisions

* **API Layer**: Same-origin routes exposed by the Hono host proxy backend APIs and Dapr-backed service calls, keeping integration logic out of UI components.
* **Error Handling**: The current UI surfaces request failures in local component state. Error boundaries or centralized fetch wrappers can be added when the UI surface expands.
* **Authentication**: If server-managed auth is added later, the Hono host is the place to terminate sessions or attach backend tokens without introducing a heavier server-rendered framework.
* **Streaming**: Streaming remains a future-facing requirement for chat flows; the current customer-records UI does not implement streaming.

## 🔧 Implementation & Operational Guidance

### 1. Cold-Start & FCP Optimizations (Azure Container Apps)

* **Cold-start posture**: Keep the Node host lightweight so it can return health and static assets quickly even when ACA cold starts.
* **Code Splitting**: Introduce dynamic imports only when the UI grows beyond the current single-screen application.
* **Critical Path**: Minimize the boot path to static asset serving plus API proxy endpoints; add streamed AI responses when chat workflows land.
* **Asset Caching**: Use CDN edge caching for static assets.
* **Skeleton UIs**: Provide skeleton loaders to mask latency.

### 2. API Layer Best Practices

* **Centralized Configuration**: Keep backend base URLs and Dapr invocation routes centralized in the Hono host.
* **Error Handling**: Expand beyond inline component-level error handling only when the UI adds more routes or workflows.
* **Optimistic Updates**: Optional for future chat or document interactions; not required for the current CRUD foundation.
* **API Client Wrapper**: A thin wrapper around `fetch` is sufficient if the current inline calls start repeating across screens.

### 3. State Management Patterns

* **Clear Separation**: Use local React state first; add a dedicated state library only when the SPA outgrows simple hooks and component composition.
* **Data Fetching**: Keep data-access utilities explicit so future migration to a query library remains straightforward.

### 4. Authentication & Security

* **Server-Side Auth**: Use the Hono host for any future server-managed auth flow or token exchange.
* **Token Management**: Prefer server-side attachment of sensitive credentials in host proxy routes rather than exposing them to the browser.
* **Secure Headers**: CSP and other headers can be applied in the Hono host and reinforced at ACA ingress.

### 5. Observability & Performance Monitoring

* **Web Vitals**: Capture metrics via `web-vitals` and forward to App Insights.
* **Error Tracking**: Integrate Sentry or App Insights.

### 6. Offline Support & Resilience

* **Service Worker**: Cache shell assets.
* **Data Caching**: IndexedDB optional for offline docs.
* **Network Awareness**: Detect and adapt to offline state.

### 7. Testing & CI/CD

* **Testing Pyramid**: Vitest + React Testing Library, Playwright for E2E.
* **Host Testing**: Validate Hono proxy routes, health endpoints, and static asset serving behavior.
* **CI/CD**: GitHub Actions build → Dockerize → deploy to ACA.

### 8. Accessibility (A11y) & UX

* **Accessibility**: Semantic HTML, ARIA attributes, keyboard nav.
* **Automated Checks**: axe-core in CI.
* **Perceived Performance**: Skeletons + streaming + transitions.
