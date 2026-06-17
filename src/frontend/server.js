import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { serve } from "@hono/node-server";
import { serveStatic } from "@hono/node-server/serve-static";
import { Hono } from "hono";

const app = new Hono();
const port = Number(process.env.PORT || 3000);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const indexHtmlPath = path.join(__dirname, "dist", "index.html");

const appInsightsConfig = parseAppInsightsConnectionString(
	process.env.APPLICATIONINSIGHTS_CONNECTION_STRING || "",
);

function parseAppInsightsConnectionString(connectionString) {
	if (!connectionString) {
		return null;
	}

	const values = Object.fromEntries(
		connectionString
			.split(";")
			.map((part) => part.trim())
			.filter(Boolean)
			.map((part) => {
				const separatorIndex = part.indexOf("=");
				if (separatorIndex < 0) {
					return [part, ""];
				}

				return [part.slice(0, separatorIndex), part.slice(separatorIndex + 1)];
			}),
	);

	const instrumentationKey = values.InstrumentationKey || values.InstrumentationKey?.trim();
	if (!instrumentationKey) {
		return null;
	}

	const ingestionEndpoint = (values.IngestionEndpoint || "https://dc.services.visualstudio.com").replace(/\/$/, "");

	return {
		instrumentationKey,
		ingestionEndpoint,
		roleName: "frontend",
		roleInstance: process.env.CONTAINER_APP_NAME || process.env.HOSTNAME || "local",
	};
}

function appInsightsEnvelope(name, baseType, baseData) {
	if (!appInsightsConfig) {
		return null;
	}

	return {
		name: `Microsoft.ApplicationInsights.${appInsightsConfig.instrumentationKey}.${name}`,
		time: new Date().toISOString(),
		iKey: appInsightsConfig.instrumentationKey,
		tags: {
			"ai.cloud.role": appInsightsConfig.roleName,
			"ai.cloud.roleInstance": appInsightsConfig.roleInstance,
		},
		data: {
			baseType,
			baseData,
		},
	};
}

async function sendAppInsightsTelemetry(envelope) {
	if (!envelope || !appInsightsConfig) {
		return;
	}

	try {
		await fetch(`${appInsightsConfig.ingestionEndpoint}/v2/track`, {
			method: "POST",
			headers: {
				"Content-Type": "application/json; charset=utf-8",
			},
			body: JSON.stringify(envelope),
		});
	} catch (error) {
		console.error(
			JSON.stringify({
				timestamp: new Date().toISOString(),
				severity: "Error",
				eventName: "FrontendAppInsightsTransportError",
				service: "frontend",
				message: toError(error).message,
			}),
		);
	}
}

function trackTrace(message, properties = {}) {
	const traceRecord = {
		timestamp: new Date().toISOString(),
		severity: "Info",
		eventName: "FrontendTrace",
		service: "frontend",
		message,
		properties,
	};

	void sendAppInsightsTelemetry(
		appInsightsEnvelope("Message", "MessageData", {
			ver: 2,
			message,
			severityLevel: 1,
			properties,
		}),
	);
	console.log(JSON.stringify(traceRecord));
}

function trackException(error, properties = {}) {
	const normalizedError = toError(error);
	const exceptionRecord = {
		timestamp: new Date().toISOString(),
		severity: "Error",
		eventName: "FrontendExceptionTelemetry",
		service: "frontend",
		message: normalizedError.message,
		stack: normalizedError.stack,
		properties,
	};

	void sendAppInsightsTelemetry(
		appInsightsEnvelope("Exception", "ExceptionData", {
			ver: 2,
			exceptions: [
				{
					typeName: normalizedError.name,
					message: normalizedError.message,
					stack: normalizedError.stack,
				},
			],
			severityLevel: 3,
			properties,
		}),
	);
	console.error(JSON.stringify(exceptionRecord));
}

function toError(error) {
	if (error instanceof Error) {
		return error;
	}

	return new Error(typeof error === "string" ? error : JSON.stringify(error));
}

function logFrontendException(error, properties = {}) {
	const normalizedError = toError(error);
	const logRecord = {
		timestamp: new Date().toISOString(),
		severity: "Error",
		eventName: "FrontendException",
		service: "frontend",
		message: normalizedError.message,
		route: properties.route ?? "unknown",
		method: properties.method ?? "unknown",
		stack: normalizedError.stack,
	};

	console.error(JSON.stringify(logRecord));
	trackException(normalizedError, properties);
}

function apiError(c, message, status = 500) {
	return c.json({ message }, status);
}

process.on("uncaughtException", (error) => {
	logFrontendException(error, { route: "process", method: "UNCAUGHT_EXCEPTION" });
});

process.on("unhandledRejection", (reason) => {
	logFrontendException(reason, { route: "process", method: "UNHANDLED_REJECTION" });
});

trackTrace("Frontend server booting", {
	port: String(Number(process.env.PORT || 3000)),
});

const backendApiCandidates = [
	process.env.BACKEND_DAPR_BASE_URL,
	process.env.BACKEND_API_BASE_URL,
	"http://localhost:3500/v1.0/invoke/api/method",
	"http://backend:8080",
	"http://localhost:8080",
	"http://host.docker.internal:8080",
].filter(Boolean);
const aiMode = (process.env.AI_MODE || "azure").trim().toLowerCase();
const entraAuthEnabled =
	(process.env.ENTRA_AUTH_ENABLED || "true").trim().toLowerCase() !== "false";
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

async function proxyToBackend(pathSuffix, options, authorizationHeader) {
	const headers = new Headers(options?.headers || {});
	if (authorizationHeader) {
		headers.set("Authorization", authorizationHeader);
	}

	let lastError = null;

	for (const baseUrl of backendApiCandidates) {
		try {
			const response = await fetch(`${baseUrl}${pathSuffix}`, {
				...options,
				headers,
			});
			return response;
		} catch (error) {
			lastError = error;
		}
	}

	const attempted = backendApiCandidates.join(", ");
	throw (
		lastError ??
		new Error(`No backend API base URLs configured. Tried: ${attempted}`)
	);
}

app.use("*", async (c, next) => {
	await next();
	c.header("X-Content-Type-Options", "nosniff");
	c.header("X-Frame-Options", "DENY");
	c.header("Referrer-Policy", "no-referrer");
	c.header("Cross-Origin-Opener-Policy", "same-origin-allow-popups");
	c.header("Cross-Origin-Resource-Policy", "same-origin");
	if (c.req.path.startsWith("/api/")) {
		c.header("Cache-Control", "no-store");
	}
});

app.get("/health", (c) => c.json({ status: "Healthy", service: "Frontend" }));

app.get("/api/auth/config", (c) =>
	c.json({
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
	}),
);

app.get("/api/dev/auth/setup-status", (c) =>
	c.json({
		supported: false,
		mode: "frontend-container",
		setupReady: false,
		canRunSetup: false,
		message:
			"One-click local auth setup is available only in ASPIRE_FRONTEND_MODE=vite-dev (host process).",
	}),
);

app.post("/api/dev/auth/setup-local", (c) =>
	c.json(
		{
			message:
				"One-click local auth setup is available only in ASPIRE_FRONTEND_MODE=vite-dev (host process).",
		},
		400,
	),
);

app.get("/api/customers", async (c) => {
	try {
		const response = await proxyToBackend(
			"/v1/customers",
			undefined,
			c.req.header("authorization"),
		);
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers", method: "GET" });
		return apiError(c, "Failed to fetch customers");
	}
});

app.post("/api/customers", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/customers", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers", method: "POST" });
		return apiError(c, "Failed to create customer");
	}
});

app.put("/api/customers/:id", async (c) => {
	try {
		const body = await c.req.json();
		const id = c.req.param("id");
		const response = await proxyToBackend(`/v1/customers/${id}`, {
			method: "PUT",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers/:id", method: "PUT" });
		return apiError(c, "Failed to update customer");
	}
});

app.delete("/api/customers/:id", async (c) => {
	try {
		const id = c.req.param("id");
		const response = await proxyToBackend(`/v1/customers/${id}`, {
			method: "DELETE",
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers/:id", method: "DELETE" });
		return apiError(c, "Failed to delete customer");
	}
});

app.post("/api/uploads", async (c) => {
	try {
		if (aiMode === "local") {
			const contentType = c.req.header("content-type");
			const requestBody = await c.req.arrayBuffer();
			const response = await proxyToBackend("/v1/uploads", {
				method: "POST",
				headers: contentType ? { "Content-Type": contentType } : undefined,
				body: requestBody,
			}, c.req.header("authorization"));
			const responseBody = await response.text();
			return c.body(responseBody, response.status, {
				"content-type":
					response.headers.get("content-type") ?? "application/json",
			});
		}

		const formData = await c.req.formData();
		const file = formData.get("file");
		if (!(file instanceof File)) {
			return c.json({ message: "file is required" }, 400);
		}

		const signedResponse = await proxyToBackend("/v1/uploads/signed-url", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ fileName: file.name }),
		}, c.req.header("authorization"));
		const signedResponseBody = await signedResponse.text();
		if (!signedResponse.ok) {
			return c.body(signedResponseBody, signedResponse.status, {
				"content-type":
					signedResponse.headers.get("content-type") ?? "application/json",
			});
		}

		const signedPayload = JSON.parse(signedResponseBody);
		const uploadResponse = await fetch(signedPayload.uploadUrl, {
			method: "PUT",
			headers: {
				"x-ms-blob-type": "BlockBlob",
				"Content-Type": file.type || "application/octet-stream",
			},
			body: await file.arrayBuffer(),
		});
		if (!uploadResponse.ok) {
			return c.json(
				{ message: "Blob upload failed", error: `Blob upload failed (${uploadResponse.status})` },
				502,
			);
		}

		return c.json({
			documentId: signedPayload.documentId,
			fileName: signedPayload.fileName,
			blobName: signedPayload.blobName,
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/uploads", method: "POST" });
		return apiError(c, "Failed to upload file");
	}
});

app.post("/api/uploads/signed-url", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/uploads/signed-url", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/uploads/signed-url", method: "POST" });
		return apiError(c, "Failed to create signed upload URL");
	}
});

app.post("/api/ingest", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/ingest", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/ingest", method: "POST" });
		return apiError(c, "Failed to trigger ingestion");
	}
});

app.get("/api/uploads/:documentId/status", async (c) => {
	try {
		const documentId = c.req.param("documentId");
		const response = await proxyToBackend(`/v1/uploads/${documentId}/status`, {
			method: "GET",
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, {
			route: "/api/uploads/:documentId/status",
			method: "GET",
		});
		return apiError(c, "Failed to fetch ingestion status");
	}
});

app.post("/api/chat", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/chat", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		}, c.req.header("authorization"));
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/chat", method: "POST" });
		return apiError(c, "Failed to call chat API");
	}
});

app.use("/*", serveStatic({ root: "./dist" }));

app.get("*", async (c) => {
	const html = await readFile(indexHtmlPath, "utf8");
	return c.html(html);
});

serve({ fetch: app.fetch, port }, (info) => {
	trackTrace("Frontend server running", { port: String(info.port) });
	console.log(`Frontend server running at http://localhost:${info.port}`);
});
