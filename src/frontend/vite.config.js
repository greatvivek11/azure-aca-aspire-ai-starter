import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
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
//   BACKEND_PROXY_BASE_URL="http://127.0.0.1:3500/v1.0/invoke/api/method"
//   (requires dapr placement service running on localhost:50005)

const backendProxyBaseUrl =
	process.env.BACKEND_PROXY_BASE_URL || "http://127.0.0.1:8080";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "../..");
const localAuthScriptPath = path.join(
	repoRoot,
	"scripts",
	"setup-local-entra-auth.sh",
);
const aspireEnvPath = path.join(repoRoot, "src", "aspire", ".env");

function parseDotEnv(text) {
	const values = {};
	for (const rawLine of text.split(/\r?\n/)) {
		const line = rawLine.trim();
		if (!line || line.startsWith("#")) {
			continue;
		}
		const idx = line.indexOf("=");
		if (idx === -1) {
			continue;
		}
		const key = line.slice(0, idx).trim();
		const value = line.slice(idx + 1).trim();
		values[key] = value;
	}
	return values;
}

function runSync(command, args) {
	try {
		return spawnSync(command, args, { stdio: "pipe", encoding: "utf8" });
	} catch {
		return { status: 1, stdout: "", stderr: "command failed" };
	}
}

async function getLocalAuthSetupStatus() {
	const status = {
		supported: true,
		mode: "vite-dev",
		scriptExists: existsSync(localAuthScriptPath),
		azInstalled: false,
		azureLoggedIn: false,
		envFileExists: existsSync(aspireEnvPath),
		enabledByEnv: false,
		setupReady: false,
		canRunSetup: false,
		missing: [],
		message: "",
	};

	const azCheck = runSync("az", ["--version"]);
	status.azInstalled = azCheck.status === 0;

	if (status.azInstalled) {
		const accountCheck = runSync("az", [
			"account",
			"show",
			"--query",
			"tenantId",
			"-o",
			"tsv",
		]);
		status.azureLoggedIn =
			accountCheck.status === 0 && Boolean(accountCheck.stdout?.trim());
	}

	let envValues = {};
	if (status.envFileExists) {
		const envText = await readFile(aspireEnvPath, "utf8");
		envValues = parseDotEnv(envText);
	}

	status.enabledByEnv =
		String(envValues.ENTRA_AUTH_ENABLED || "").toLowerCase() === "true";
	const required = [
		"ENTRA_TENANT_ID",
		"ENTRA_API_CLIENT_ID",
		"ENTRA_SPA_CLIENT_ID",
		"ENTRA_SCOPE",
	];
	for (const key of required) {
		if (!envValues[key]) {
			status.missing.push(key);
		}
	}

	status.setupReady = status.enabledByEnv && status.missing.length === 0;
	status.canRunSetup =
		status.scriptExists && status.azInstalled && status.azureLoggedIn;

	if (!status.scriptExists) {
		status.message = "Setup script not found in repo.";
	} else if (!status.azInstalled) {
		status.message = "Azure CLI is not installed. Install az CLI first.";
	} else if (!status.azureLoggedIn) {
		status.message = "Run 'az login' to enable one-click setup.";
	} else if (!status.setupReady) {
		status.message = "Ready to configure local auth.";
	} else {
		status.message = "Local auth is configured in src/aspire/.env.";
	}

	return status;
}

let setupInProgress = false;

function runLocalAuthSetup() {
	return new Promise((resolve, reject) => {
		const child = spawn("bash", [localAuthScriptPath], {
			cwd: repoRoot,
			env: process.env,
			stdio: ["ignore", "pipe", "pipe"],
		});

		let stdout = "";
		let stderr = "";
		const timer = setTimeout(() => {
			child.kill("SIGTERM");
			reject(new Error("Local auth setup timed out after 5 minutes."));
		}, 300000);

		child.stdout.on("data", (chunk) => {
			stdout += chunk.toString();
		});
		child.stderr.on("data", (chunk) => {
			stderr += chunk.toString();
		});
		child.on("error", (error) => {
			clearTimeout(timer);
			reject(error);
		});
		child.on("close", (code) => {
			clearTimeout(timer);
			resolve({ code: code ?? 1, stdout, stderr });
		});
	});
}

// Serve /api/auth/config in Vite dev mode.
//
// In production the Hono server.js generates this endpoint, but the dev server
// only runs Vite. Without this the SPA's fetch("/api/auth/config") falls through
// to index.html and the JSON parse fails ("Unexpected token '<'").
// This middleware mirrors the env logic in server.js to keep dev/prod consistent.
function authConfigDevPlugin() {
	return {
		name: "auth-config-dev",
		configureServer(server) {
		function isLocalhost(req) {
			const remoteAddress = req.socket.remoteAddress || "";
			return (
				remoteAddress === "127.0.0.1" ||
				remoteAddress === "::1" ||
				remoteAddress === "localhost"
			);
		}

		function devAuthMiddleware(path, handler) {
			server.middlewares.use(path, (req, res, next) => {
				if (!isLocalhost(req)) {
					res.statusCode = 403;
					res.setHeader("Content-Type", "application/json");
					res.end(
						JSON.stringify({
							message:
								"Dev endpoints are localhost-only. Remote access is blocked.",
						}),
					);
					return;
				}
				handler(req, res, next);
			});
		}

		devAuthMiddleware(
			"/api/dev/auth/setup-status",
			async (_req, res) => {
					try {
						const status = await getLocalAuthSetupStatus();
						res.statusCode = 200;
						res.setHeader("Content-Type", "application/json");
						res.end(JSON.stringify(status));
					} catch (error) {
						res.statusCode = 500;
						res.setHeader("Content-Type", "application/json");
						res.end(
							JSON.stringify({
								supported: true,
								setupReady: false,
								canRunSetup: false,
								message:
									error?.message ||
									"Failed to evaluate local auth setup status.",
							}),
						);
					}
				},
			);

			devAuthMiddleware("/api/dev/auth/setup-local", async (req, res) => {
				if ((req.method || "GET").toUpperCase() !== "POST") {
					res.statusCode = 405;
					res.setHeader("Content-Type", "application/json");
					res.end(JSON.stringify({ message: "Method not allowed." }));
					return;
				}

				if (setupInProgress) {
					res.statusCode = 409;
					res.setHeader("Content-Type", "application/json");
					res.end(
						JSON.stringify({
							message: "Local auth setup already in progress.",
						}),
					);
					return;
				}

				const status = await getLocalAuthSetupStatus();
				if (!status.canRunSetup) {
					res.statusCode = 400;
					res.setHeader("Content-Type", "application/json");
					res.end(JSON.stringify({ message: status.message, status }));
					return;
				}

				setupInProgress = true;
				try {
					const result = await runLocalAuthSetup();
					if (result.code !== 0) {
						res.statusCode = 500;
						res.setHeader("Content-Type", "application/json");
						res.end(
							JSON.stringify({
								message: "Local auth setup script failed.",
								stdout: result.stdout,
								stderr: result.stderr,
							}),
						);
						return;
					}

					res.statusCode = 200;
					res.setHeader("Content-Type", "application/json");
					res.end(
						JSON.stringify({
							message:
								"Local auth setup completed. Restart Aspire so services reload src/aspire/.env.",
							restartRequired: true,
							stdout: result.stdout,
						}),
					);
				} catch (error) {
					res.statusCode = 500;
					res.setHeader("Content-Type", "application/json");
					res.end(
						JSON.stringify({
							message: error?.message || "Failed to run local auth setup.",
						}),
					);
				} finally {
					setupInProgress = false;
				}
			});

			server.middlewares.use("/api/auth/config", (_req, res) => {
				const entraAuthEnabled =
					(process.env.ENTRA_AUTH_ENABLED || "true").trim().toLowerCase() !==
					"false";
				const entraTenantId = (process.env.ENTRA_TENANT_ID || "").trim();
				const entraAuthority = (
					process.env.ENTRA_AUTHORITY ||
					(entraTenantId
						? `https://login.microsoftonline.com/${entraTenantId}/v2.0`
						: "")
				).trim();
				const entraApiClientId = (process.env.ENTRA_API_CLIENT_ID || "").trim();
				const entraSpaClientId = (process.env.ENTRA_SPA_CLIENT_ID || "").trim();
				const entraScope = (
					process.env.ENTRA_SCOPE ||
					(entraApiClientId ? `api://${entraApiClientId}/access_as_user` : "")
				).trim();

				const payload = {
					enabled:
						entraAuthEnabled &&
						Boolean(entraAuthority) &&
						Boolean(entraSpaClientId) &&
						Boolean(entraScope),
					authority: entraAuthority,
					tenantId: entraTenantId,
					spaClientId: entraSpaClientId,
					apiClientId: entraApiClientId,
					scope: entraScope,
				};

				res.setHeader("Content-Type", "application/json");
				res.setHeader("Cache-Control", "no-store");
				res.end(JSON.stringify(payload));
			});
		},
	};
}

export default defineConfig({
	plugins: [react(), authConfigDevPlugin()],
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
