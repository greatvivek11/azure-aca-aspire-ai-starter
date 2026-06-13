import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Backend proxy configuration for Vite dev server
// 
// IMPORTANT: Local Vite dev mode uses DIRECT HTTP to backend instead of Dapr service invocation.
// This is a pragmatic choice for local development:
//
// WHY direct HTTP for vite-dev:
//   - No Dapr placement service needed (not running locally by default)
//   - Faster feedback loop for frontend developers (no sidecar overhead)
//   - Simpler debugging (direct visibility into backend API calls)
//   - Hot Module Reload (HMR) iteration is snappy
//
// ARCHITECTURE NOTES:
//   - Production (Docker/Kubernetes): Uses Dapr service invocation via sidecars
//   - Local vite-dev (npm run dev:aspire): Uses direct HTTP for velocity
//   - This divergence is intentional and does NOT compromise production architecture
//   - The backend is still fully Dapr-enabled; only the local dev path differs
//
// To override for testing Dapr locally, set:
//   BACKEND_PROXY_BASE_URL="http://127.0.0.1:3500/v1.0/invoke/aihub-backend/method"
//   (requires dapr placement service running on localhost:50005)

const backendProxyBaseUrl =
	process.env.BACKEND_PROXY_BASE_URL ||
	"http://127.0.0.1:8080";

export default defineConfig({
	plugins: [react()],
	server: {
		host: "0.0.0.0",
		port: 3000,
		proxy: {
			"/api/customers": {
				target: backendProxyBaseUrl,
				changeOrigin: true,
				rewrite: (path) => path.replace(/^\/api\/customers/, "/v1/customers"),
			},
			"/api/uploads": {
				target: backendProxyBaseUrl,
				changeOrigin: true,
				rewrite: (path) => path.replace(/^\/api\/uploads/, "/v1/uploads"),
			},
			"/api/ingest": {
				target: backendProxyBaseUrl,
				changeOrigin: true,
				rewrite: (path) => path.replace(/^\/api\/ingest/, "/v1/ingest"),
			},
			"/api/chat": {
				target: backendProxyBaseUrl,
				changeOrigin: true,
				rewrite: (path) => path.replace(/^\/api\/chat/, "/v1/chat"),
			},
		},
	},
});
