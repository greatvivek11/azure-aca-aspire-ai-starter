import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// For local Vite dev, use direct HTTP backend (no Dapr placement service needed)
// In Docker/Kubernetes environments, the backend will be reachable via Dapr service invocation
const backendProxyBaseUrl = process.env.BACKEND_PROXY_BASE_URL || "http://127.0.0.1:8080";

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
