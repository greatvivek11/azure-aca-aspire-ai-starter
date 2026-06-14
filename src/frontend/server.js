import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { serve } from "@hono/node-server";
import { serveStatic } from "@hono/node-server/serve-static";
import appInsights from "applicationinsights";
import { Hono } from "hono";

const app = new Hono();
const port = Number(process.env.PORT || 3000);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const indexHtmlPath = path.join(__dirname, "dist", "index.html");

const connectionString = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
if (connectionString) {
	appInsights
		.setup(connectionString)
		.setAutoCollectRequests(true)
		.setAutoCollectDependencies(true)
		.setAutoCollectExceptions(true)
		.setAutoCollectConsole(true, true)
		.setUseDiskRetryCaching(true)
		.start();
}

const telemetryClient = appInsights.defaultClient;

function trackTrace(message, properties = {}) {
	telemetryClient?.trackTrace({ message, properties });
}

function trackException(error, properties = {}) {
	telemetryClient?.trackException({ exception: error, properties });
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

async function proxyToBackend(pathSuffix, options) {
	let lastError = null;

	for (const baseUrl of backendApiCandidates) {
		try {
			const response = await fetch(`${baseUrl}${pathSuffix}`, options);
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

app.get("/health", (c) => c.json({ status: "Healthy", service: "Frontend" }));

app.get("/api/customers", async (c) => {
	try {
		const response = await proxyToBackend("/v1/customers");
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers", method: "GET" });
		return c.json(
			{ message: "Failed to fetch customers", error: error.message },
			500,
		);
	}
});

app.post("/api/customers", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/customers", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers", method: "POST" });
		return c.json(
			{ message: "Failed to create customer", error: error.message },
			500,
		);
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
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers/:id", method: "PUT" });
		return c.json(
			{ message: "Failed to update customer", error: error.message },
			500,
		);
	}
});

app.delete("/api/customers/:id", async (c) => {
	try {
		const id = c.req.param("id");
		const response = await proxyToBackend(`/v1/customers/${id}`, {
			method: "DELETE",
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/customers/:id", method: "DELETE" });
		return c.json(
			{ message: "Failed to delete customer", error: error.message },
			500,
		);
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
			});
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
		});
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
		return c.json({ message: "Failed to upload file", error: error.message }, 500);
	}
});

app.post("/api/uploads/signed-url", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/uploads/signed-url", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/uploads/signed-url", method: "POST" });
		return c.json(
			{ message: "Failed to create signed upload URL", error: error.message },
			500,
		);
	}
});

app.post("/api/ingest", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/ingest", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/ingest", method: "POST" });
		return c.json({ message: "Failed to trigger ingestion", error: error.message }, 500);
	}
});

app.get("/api/uploads/:documentId/status", async (c) => {
	try {
		const documentId = c.req.param("documentId");
		const response = await proxyToBackend(`/v1/uploads/${documentId}/status`, {
			method: "GET",
		});
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
		return c.json({ message: "Failed to fetch ingestion status", error: error.message }, 500);
	}
});

app.post("/api/chat", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/chat", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type":
				response.headers.get("content-type") ?? "application/json",
		});
	} catch (error) {
		logFrontendException(error, { route: "/api/chat", method: "POST" });
		return c.json({ message: "Failed to call chat API", error: error.message }, 500);
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
