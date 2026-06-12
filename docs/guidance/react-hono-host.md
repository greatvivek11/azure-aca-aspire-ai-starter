# React Frontend Host Best Practices Guide

## Philosophy

* Treat the frontend as a **Vite-built SPA** with a thin **Hono** host, not as a full-stack SSR framework.
* Keep browser rendering, host-level proxying, and backend integration responsibilities clearly separated.
* Prefer operational simplicity over framework complexity.
* Optimize for **performance, reliability, and developer experience**.

## Project Setup

* Use **npm** as the supported package manager.
* Enforce linting/formatting with **ESLint + Prettier**.
* Organize the React app so it can grow from a simple `src/` entrypoint into feature-based folders as needed.
* Keep host concerns in `server.js` and browser concerns in `src/`.

```text
src/frontend/
  package.json
  package-lock.json
  server.js
  vite.config.js
  src/
```

## Data Fetching Strategy

* Fetch UI data with browser `fetch` or a thin client wrapper.
* Put sensitive integration logic in **Hono host routes** or in the backend, not in browser code.
* Stream long-running AI responses from backend endpoints when available.
* Add a dedicated client-side query library only when the data surface grows enough to justify it.

```js
const response = await fetch('/api/customers');
const customers = await response.json();
```

## Routing & Navigation

* Keep client-side routing lightweight unless the UI grows enough to require a dedicated router.
* Use Hono routes for health checks, backend proxy routes, and operational endpoints.
* Keep URL structure stable so ACA ingress and Dapr proxying stay predictable.

## State Management

* Local state → React `useState`, `useReducer`.
* Shared state → Context API or a small dedicated store only when needed.
* Server state → start with explicit fetch utilities, then introduce a query library deliberately.

## Styling

* Use **component-local CSS** or a lightweight styling system that matches the current SPA.
* Keep global styles minimal and predictable.
* Use **CSS variables** for theming when the design system grows.

## Performance

* Keep the Hono host small so cold starts remain acceptable.
* Use Vite code splitting and lazy loading for heavier UI features.
* Cache static assets aggressively.
* Apply bundle analysis with Vite-compatible tooling when bundle size starts to matter.

```bash
npm run build
```

## Security

* Always enable **HTTPS & CSP headers** at the host and ingress layers.
* Sanitize user input (XSS prevention).
* Use **environment variables** through Node `process.env` in `server.js`.
* Enforce **RBAC/ABAC** in backend or host proxy routes.

## Observability

* Integrate Application Insights or comparable telemetry where it adds operational value.
* Use **OpenTelemetry** or Sentry for tracing & error tracking.
* Log structured JSON for container-hosted observability.

## Testing

* Unit tests → Vitest/Jest + React Testing Library.
* Integration → Playwright/Cypress.
* Use **Mock Service Worker (MSW)** for API mocking.

## Deployment

* Deploy the frontend as a container to **Azure Container Apps**.
* Keep the host compatible with Docker-based local and cloud execution.
* Integrate with backend-owned data services through APIs rather than direct browser credentials.

## Anti-Patterns

* Moving too much backend logic into the frontend host.
* Over-fetching from the browser when a host-side proxy or aggregation route is more appropriate.
* Bloated `server.js` with domain logic that belongs in the backend.
* Ignoring caching headers (`Cache-Control`).
* Putting secrets in client-side code.

## Checklist

* [ ] npm scripts documented
* [ ] ESLint + Prettier enforced
* [ ] Data fetching strategy chosen (browser fetch vs host proxy)
* [ ] Security headers & env vars configured
* [ ] Error monitoring enabled
* [ ] Tested with Lighthouse & Web Vitals
