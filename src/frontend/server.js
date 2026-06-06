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

const backendApiCandidates = [
	process.env.BACKEND_API_BASE_URL,
	process.env.BACKEND_DAPR_BASE_URL,
	"http://localhost:3500/v1.0/invoke/aihub-backend/method",
	"http://backend:8080",
	"http://localhost:8080",
	"http://host.docker.internal:8080"
].filter(Boolean);

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
	throw lastError ?? new Error(`No backend API base URLs configured. Tried: ${attempted}`);
}

app.get("/health", (c) => c.json({ status: "Healthy", service: "Frontend" }));

app.get("/api/customers", async (c) => {
	try {
		const response = await proxyToBackend("/v1/customers");
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type": response.headers.get("content-type") ?? "application/json"
		});
	} catch (error) {
		return c.json({ message: "Failed to fetch customers", error: error.message }, 500);
	}
});

app.post("/api/customers", async (c) => {
	try {
		const body = await c.req.json();
		const response = await proxyToBackend("/v1/customers", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body)
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type": response.headers.get("content-type") ?? "application/json"
		});
	} catch (error) {
		return c.json({ message: "Failed to create customer", error: error.message }, 500);
	}
});

app.put("/api/customers/:id", async (c) => {
	try {
		const body = await c.req.json();
		const id = c.req.param("id");
		const response = await proxyToBackend(`/v1/customers/${id}`, {
			method: "PUT",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body)
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type": response.headers.get("content-type") ?? "application/json"
		});
	} catch (error) {
		return c.json({ message: "Failed to update customer", error: error.message }, 500);
	}
});

app.delete("/api/customers/:id", async (c) => {
	try {
		const id = c.req.param("id");
		const response = await proxyToBackend(`/v1/customers/${id}`, {
			method: "DELETE"
		});
		const responseBody = await response.text();
		return c.body(responseBody, response.status, {
			"content-type": response.headers.get("content-type") ?? "application/json"
		});
	} catch (error) {
		return c.json({ message: "Failed to delete customer", error: error.message }, 500);
	}
});

app.use("/*", serveStatic({ root: "./dist" }));

app.get("*", async (c) => {
	const html = await readFile(indexHtmlPath, "utf8");
	return c.html(html);
});

serve({ fetch: app.fetch, port }, (info) => {
	console.log(`Frontend server running at http://localhost:${info.port}`);
});
