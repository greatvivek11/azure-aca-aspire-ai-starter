import {
	InteractionRequiredAuthError,
	PublicClientApplication,
} from "@azure/msal-browser";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

// Module-level singleton so MSAL is not re-created on every React render or
// hot-module reload, and already-cached accounts are found immediately.
let msalClientSingleton = null;
let msalConfigKey = null;

const emptyForm = {
	name: "",
	email: "",
	city: "",
	status: "Active",
};

const terminalStatuses = new Set(["Ready", "Failed"]);

export default function App() {
	const [authConfig, setAuthConfig] = useState(null);
	const [authClient, setAuthClient] = useState(null);
	const [authAccount, setAuthAccount] = useState(null);
	const [authLoading, setAuthLoading] = useState(true);
	const [authError, setAuthError] = useState("");
	const [localAuthStatus, setLocalAuthStatus] = useState(null);
	const [localAuthLoading, setLocalAuthLoading] = useState(true);
	const [localAuthRunning, setLocalAuthRunning] = useState(false);
	const [localAuthMessage, setLocalAuthMessage] = useState("");

	const [activeTab, setActiveTab] = useState("data");
	const [customers, setCustomers] = useState([]);
	const [form, setForm] = useState(emptyForm);
	const [saving, setSaving] = useState(false);
	const [dataError, setDataError] = useState("");

	const [chatInput, setChatInput] = useState("");
	const [chatError, setChatError] = useState("");
	const [chatLoading, setChatLoading] = useState(false);
	const [uploadingFile, setUploadingFile] = useState(false);
	const [messages, setMessages] = useState([]);
	const [ingestionJobs, setIngestionJobs] = useState([]);
	const [activeDocumentId, setActiveDocumentId] = useState(null);
	const fileInputRef = useRef(null);
	const isAuthEnabled = Boolean(authConfig?.enabled);
	const isAuthenticated = !isAuthEnabled || Boolean(authAccount);

	const refreshLocalAuthStatus = useCallback(async () => {
		setLocalAuthLoading(true);
		try {
			const response = await fetch("/api/dev/auth/setup-status");
			const payload = await response.json();
			if (!response.ok) {
				throw new Error(
					payload?.message || "Failed to load local auth setup status.",
				);
			}
			setLocalAuthStatus(payload);
		} catch (error) {
			setLocalAuthStatus({
				supported: false,
				canRunSetup: false,
				setupReady: false,
				message: error?.message || "Unable to load local auth setup status.",
			});
		} finally {
			setLocalAuthLoading(false);
		}
	}, []);

	// Only fetch dev auth status when auth is actually disabled and the
	// setup panel will be shown. Avoids running `az account show` on every
	// page load when auth is already configured.
	useEffect(() => {
		if (authLoading) return;
		if (isAuthEnabled) return;
		refreshLocalAuthStatus().catch(() => {});
	}, [authLoading, isAuthEnabled, refreshLocalAuthStatus]);

	useEffect(() => {
		let isMounted = true;

		async function bootstrapAuth() {
			setAuthLoading(true);
			setAuthError("");

			try {
				const response = await fetch("/api/auth/config");
				if (!response.ok) {
					throw new Error(`Failed to load auth config (${response.status})`);
				}

				const config = await response.json();
				if (!isMounted) {
					return;
				}

				setAuthConfig(config);
				if (!config.enabled) {
					setAuthClient(null);
					setAuthAccount(null);
					return;
				}

				const authRedirectUri = `${window.location.origin}/auth-callback.html`;
				const configKey = `${config.spaClientId}::${config.authority}`;

				// Reuse existing MSAL instance when config hasn't changed to avoid
				// re-initialization overhead and preserve cached token state.
				if (!msalClientSingleton || msalConfigKey !== configKey) {
					msalClientSingleton = new PublicClientApplication({
						auth: {
							clientId: config.spaClientId,
							authority: config.authority,
							redirectUri: authRedirectUri,
							postLogoutRedirectUri: window.location.origin,
							navigateToLoginRequestUrl: false,
						},
						cache: {
							cacheLocation: "localStorage",
							storeAuthStateInCookie: false,
						},
					});
					await msalClientSingleton.initialize();
					msalConfigKey = configKey;
				}

				const client = msalClientSingleton;

				// Only call handleRedirectPromise when Entra actually redirected back
				// with an auth code. On a normal page load/refresh this skips a
				// round-trip to the Entra metadata endpoint (~2-4 s).
				const isRedirectCallback =
					window.location.hash.includes("code=") ||
					window.location.hash.includes("error=");

				if (isRedirectCallback) {
					const redirectResult = await client.handleRedirectPromise();
					if (redirectResult?.account) {
						client.setActiveAccount(redirectResult.account);
					}
				}

				const account = client.getActiveAccount() || client.getAllAccounts()[0];
				setAuthClient(client);
				setAuthAccount(account || null);

				// Auto-redirect to MS login when no cached session exists.
				// Skips the "Not signed in / Sign in" intermediary screen.
				if (!account) {
					await client.loginRedirect({ scopes: [config.scope] });
				}
			} catch (error) {
				if (isMounted) {
					setAuthError(error?.message || "Authentication bootstrap failed.");
				}
			} finally {
				if (isMounted) {
					setAuthLoading(false);
				}
			}
		}

		bootstrapAuth();

		return () => {
			isMounted = false;
		};
	}, []);

	const onSetupLocalAuth = useCallback(async () => {
		setLocalAuthMessage("");
		setLocalAuthRunning(true);
		try {
			const response = await fetch("/api/dev/auth/setup-local", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
			});
			const payload = await response.json();
			if (!response.ok) {
				throw new Error(payload?.message || "Local auth setup failed.");
			}
			setLocalAuthMessage(
				payload?.message ||
					"Local auth setup completed. Restart Aspire so services reload src/aspire/.env.",
			);
			await refreshLocalAuthStatus();
		} catch (error) {
			setLocalAuthMessage(error?.message || "Local auth setup failed.");
		} finally {
			setLocalAuthRunning(false);
		}
	}, [refreshLocalAuthStatus]);

	const acquireAccessToken = useCallback(async () => {
		if (!isAuthEnabled) {
			return "";
		}

		if (!authClient || !authAccount || !authConfig?.scope) {
			throw new Error("Sign in is required before calling protected APIs.");
		}

		const tokenRequest = {
			account: authAccount,
			scopes: [authConfig.scope],
		};

		try {
			const result = await authClient.acquireTokenSilent(tokenRequest);
			return result.accessToken;
		} catch (error) {
			if (error instanceof InteractionRequiredAuthError) {
				setAuthLoading(true);
				await authClient.acquireTokenRedirect(tokenRequest);
				return "";
			}
			throw error;
		}
	}, [authAccount, authClient, authConfig?.scope, isAuthEnabled]);

	const apiFetch = useCallback(
		async (url, options = {}) => {
			const headers = new Headers(options.headers || {});
			if (isAuthEnabled) {
				const accessToken = await acquireAccessToken();
				headers.set("Authorization", `Bearer ${accessToken}`);
			}

			return fetch(url, {
				...options,
				headers,
			});
		},
		[acquireAccessToken, isAuthEnabled],
	);

	const onSignIn = useCallback(async () => {
		if (!authClient || !authConfig?.scope) {
			return;
		}

		setAuthError("");
		setAuthLoading(true);
		try {
			await authClient.loginRedirect({
				scopes: [authConfig.scope],
			});
		} catch (error) {
			const message = error?.message || "Sign-in failed.";
			if (String(message).includes("block_nested_popups")) {
				setAuthError(
					"Sign-in window conflict detected. Retry sign-in from the main app tab and avoid clicking Sign in inside any popup.",
				);
			} else {
				setAuthError(message);
			}
		} finally {
			setAuthLoading(false);
		}
	}, [authClient, authConfig?.scope]);

	const onSignOut = useCallback(async () => {
		if (!authClient) {
			return;
		}

		setAuthError("");
		setAuthLoading(true);
		try {
			await authClient.logoutRedirect({
				account: authAccount || undefined,
				postLogoutRedirectUri: window.location.origin,
			});
			setAuthAccount(null);
			setMessages([]);
			setIngestionJobs([]);
			setCustomers([]);
		} catch (error) {
			setAuthError(error?.message || "Sign-out failed.");
		} finally {
			setAuthLoading(false);
		}
	}, [authAccount, authClient]);

	const loadCustomers = useCallback(async () => {
		if (!isAuthenticated) {
			return;
		}

		setDataError("");
		const response = await apiFetch("/api/customers");
		if (!response.ok) {
			throw new Error(`Failed to load customers (${response.status})`);
		}
		const data = await response.json();
		setCustomers(data);
	}, [apiFetch, isAuthenticated]);

	useEffect(() => {
		// Wait for auth bootstrap to finish before loading data.
		// Without this guard, isAuthenticated is briefly true before
		// auth config is known (isAuthEnabled defaults false), causing
		// a token-less request that returns 401.
		if (authLoading || !isAuthenticated) {
			return;
		}

		loadCustomers().catch((err) => setDataError(err.message));
	}, [authLoading, isAuthenticated, loadCustomers]);

	const pendingJobIds = useMemo(
		() =>
			ingestionJobs
				.filter((job) => !terminalStatuses.has(job.status))
				.map((job) => job.documentId),
		[ingestionJobs],
	);

	useEffect(() => {
		if (!isAuthenticated) {
			return undefined;
		}

		if (pendingJobIds.length === 0) {
			return undefined;
		}

		const intervalId = setInterval(async () => {
			await Promise.all(
				pendingJobIds.map(async (documentId) => {
					try {
						const response = await apiFetch(
							`/api/uploads/${documentId}/status`,
						);
						if (!response.ok) {
							return;
						}

						const latest = await response.json();
						setIngestionJobs((prev) =>
							prev.map((item) =>
								item.documentId === documentId ? latest : item,
							),
						);

						if (latest.status === "Ready") {
							setActiveDocumentId(latest.documentId);
							setMessages((prev) => [
								...prev,
								{
									id: crypto.randomUUID(),
									role: "system",
									text: `${latest.fileName} is indexed and ready for questions.`,
								},
							]);
						}

						if (latest.status === "Failed" && latest.errorMessage) {
							setMessages((prev) => [
								...prev,
								{
									id: crypto.randomUUID(),
									role: "system",
									text: `${latest.fileName} failed to ingest: ${latest.errorMessage}`,
								},
							]);
						}
					} catch (error) {
						setChatError(error.message);
					}
				}),
			);
		}, 2500);

		return () => clearInterval(intervalId);
	}, [apiFetch, isAuthenticated, pendingJobIds]);

	const pendingUploadCount = useMemo(
		() =>
			ingestionJobs.filter((job) => !terminalStatuses.has(job.status)).length,
		[ingestionJobs],
	);
	const hasUploadedFile = ingestionJobs.length > 0;
	const hasReadyDocument = useMemo(
		() =>
			Boolean(activeDocumentId) &&
			ingestionJobs.some(
				(job) => job.documentId === activeDocumentId && job.status === "Ready",
			),
		[activeDocumentId, ingestionJobs],
	);
	const hasIngestionInProgress = useMemo(
		() => ingestionJobs.some((job) => !terminalStatuses.has(job.status)),
		[ingestionJobs],
	);
	const sendDisabledReason = !hasUploadedFile
		? "Upload a file first to send a message."
		: uploadingFile
			? "Please wait for the file upload to finish."
			: !hasReadyDocument && hasIngestionInProgress
				? "Please wait for ingestion to complete before sending."
				: !hasReadyDocument
					? "Upload and ingest a file successfully before sending."
					: "";
	const sendDisabled = chatLoading || uploadingFile || !hasReadyDocument;

	async function onCreateCustomer(event) {
		event.preventDefault();
		if (!isAuthenticated) {
			setDataError("Sign in is required.");
			return;
		}

		setSaving(true);
		setDataError("");

		try {
			const response = await apiFetch("/api/customers", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify(form),
			});

			if (!response.ok) {
				const text = await response.text();
				throw new Error(text || `Failed to create record (${response.status})`);
			}

			setForm(emptyForm);
			await loadCustomers();
		} catch (err) {
			setDataError(err.message);
		} finally {
			setSaving(false);
		}
	}

	async function onDeleteCustomer(id) {
		if (!isAuthenticated) {
			setDataError("Sign in is required.");
			return;
		}

		setDataError("");
		try {
			const response = await apiFetch(`/api/customers/${id}`, {
				method: "DELETE",
			});
			if (!response.ok && response.status !== 404) {
				const text = await response.text();
				throw new Error(text || `Failed to delete record (${response.status})`);
			}
			await loadCustomers();
		} catch (err) {
			setDataError(err.message);
		}
	}

	function appendSystemMessage(text) {
		setMessages((prev) => [
			...prev,
			{ id: crypto.randomUUID(), role: "system", text },
		]);
	}

	async function onAttachFile(event) {
		if (!isAuthenticated) {
			setChatError("Sign in is required.");
			return;
		}

		const file = event.target.files?.[0];
		if (!file) {
			return;
		}

		setChatError("");
		setUploadingFile(true);
		appendSystemMessage(`Uploading ${file.name}...`);

		try {
			const formData = new FormData();
			formData.append("file", file);
			const uploadResponse = await apiFetch("/api/uploads", {
				method: "POST",
				body: formData,
			});
			if (!uploadResponse.ok) {
				const text = await uploadResponse.text();
				throw new Error(
					text || `Blob upload failed (${uploadResponse.status})`,
				);
			}

			const uploadPayload = await uploadResponse.json();

			const ingestResponse = await apiFetch("/api/ingest", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ documentId: uploadPayload.documentId }),
			});
			if (!ingestResponse.ok) {
				const text = await ingestResponse.text();
				throw new Error(
					text || `Failed to trigger ingestion (${ingestResponse.status})`,
				);
			}

			setIngestionJobs((prev) => [
				...prev.filter((item) => item.documentId !== uploadPayload.documentId),
				{
					documentId: uploadPayload.documentId,
					fileName: uploadPayload.fileName,
					blobName: uploadPayload.blobName,
					status: "Queued",
					progressPercent: 15,
				},
			]);
			appendSystemMessage(`${file.name} uploaded. Indexing has started.`);
		} catch (error) {
			setChatError(error.message);
			appendSystemMessage(`Upload failed for ${file.name}: ${error.message}`);
		} finally {
			setUploadingFile(false);
			event.target.value = "";
		}
	}

	async function onSendChat(event) {
		event.preventDefault();
		if (!isAuthenticated) {
			setChatError("Sign in is required.");
			return;
		}

		if (!chatInput.trim() || sendDisabled) {
			return;
		}

		const messageText = chatInput.trim();
		setChatInput("");
		setChatError("");
		setChatLoading(true);

		const userMessage = {
			id: crypto.randomUUID(),
			role: "user",
			text: messageText,
		};
		setMessages((prev) => [...prev, userMessage]);

		try {
			const response = await apiFetch("/api/chat", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({
					message: messageText,
					mode: "rag",
					documentId: activeDocumentId,
				}),
			});

			if (!response.ok) {
				const text = await response.text();
				throw new Error(text || `Chat request failed (${response.status})`);
			}

			const payload = await response.json();
			setMessages((prev) => [
				...prev,
				{
					id: crypto.randomUUID(),
					role: "assistant",
					text: payload.answer,
					citations: payload.citations || [],
				},
			]);
		} catch (error) {
			setChatError(error.message);
			setMessages((prev) => [
				...prev,
				{
					id: crypto.randomUUID(),
					role: "system",
					text: `Chat failed: ${error.message}`,
				},
			]);
		} finally {
			setChatLoading(false);
		}
	}

	return (
		<main className="app-shell">
			<header
				className="tab-header"
				style={{
					display: "grid",
					gridTemplateColumns: "1fr auto 1fr",
					alignItems: "center",
				}}
			>
				<div />
				<div style={{ display: "flex", gap: "10px", justifyContent: "center" }}>
					<button
						type="button"
						className={activeTab === "data" ? "tab active" : "tab"}
						onClick={() => setActiveTab("data")}
						disabled={!isAuthenticated}
					>
						Database View
					</button>
					<button
						type="button"
						className={activeTab === "chat" ? "tab active" : "tab"}
						onClick={() => setActiveTab("chat")}
						disabled={!isAuthenticated}
					>
						AI Chat {pendingUploadCount > 0 ? `(${pendingUploadCount})` : ""}
					</button>
				</div>
				<div
					style={{
						display: "flex",
						alignItems: "center",
						gap: "0.5rem",
						justifyContent: "flex-end",
					}}
				>
					{isAuthEnabled ? (
						<>
							<span style={{ fontSize: "0.85rem", opacity: 0.8 }}>
								{authAccount?.username || "Not signed in"}
							</span>
							{authAccount ? (
								<button
									type="button"
									onClick={onSignOut}
									disabled={authLoading}
								>
									Sign out
								</button>
							) : (
								<button type="button" onClick={onSignIn} disabled={authLoading}>
									Sign in
								</button>
							)}
						</>
					) : (
						<span style={{ fontSize: "0.85rem", opacity: 0.8 }}>
							Auth disabled
						</span>
					)}
				</div>
			</header>

			{authError ? <div className="error auth-error">{authError}</div> : null}
			{localAuthMessage ? (
				<div className="card auth-setup-banner">{localAuthMessage}</div>
			) : null}
			{!isAuthEnabled && !authLoading ? (
				<div className="card auth-required-card">
					<h2>Authentication is currently disabled</h2>
					<p className="subtitle">
						Enable local Entra auth with one click (development mode only), then
						restart Aspire and sign in.
					</p>
					<div className="auth-setup-actions">
						<button
							type="button"
							className="primary"
							onClick={onSetupLocalAuth}
							disabled={
								localAuthLoading ||
								localAuthRunning ||
								!localAuthStatus?.canRunSetup
							}
						>
							{localAuthRunning
								? "Setting up local auth..."
								: "Set up local auth"}
						</button>
						<button
							type="button"
							onClick={() => refreshLocalAuthStatus()}
							disabled={localAuthLoading || localAuthRunning}
						>
							{localAuthLoading ? "Checking..." : "Refresh status"}
						</button>
					</div>
					<p className="setup-hint">
						{localAuthStatus?.message ||
							"Status unavailable. In container mode, switch to ASPIRE_FRONTEND_MODE=vite-dev for one-click setup."}
					</p>
				</div>
			) : null}

			{isAuthEnabled &&
			!authLoading &&
			!authAccount ? null : isAuthenticated ? (
				activeTab === "data" ? (
					<>
						<section className="card">
							<h1>Customer Records</h1>
							<p className="subtitle">
								React frontend to backend API to SQL Server
							</p>
							{dataError ? <div className="error">{dataError}</div> : null}
							<div className="table-wrap">
								<table>
									<thead>
										<tr>
											<th>Id</th>
											<th>Name</th>
											<th>Email</th>
											<th>City</th>
											<th>Status</th>
											<th>Action</th>
										</tr>
									</thead>
									<tbody>
										{customers.map((customer) => (
											<tr key={customer.id}>
												<td>{customer.id}</td>
												<td>{customer.name}</td>
												<td>{customer.email}</td>
												<td>{customer.city}</td>
												<td>{customer.status}</td>
												<td>
													<button
														type="button"
														onClick={() => onDeleteCustomer(customer.id)}
													>
														Delete
													</button>
												</td>
											</tr>
										))}
									</tbody>
								</table>
							</div>
						</section>

						<section className="card">
							<h2>Create Record</h2>
							<form onSubmit={onCreateCustomer} className="form-grid">
								<label>
									Name
									<input
										required
										value={form.name}
										onChange={(event) =>
											setForm({ ...form, name: event.target.value })
										}
									/>
								</label>
								<label>
									Email
									<input
										required
										type="email"
										value={form.email}
										onChange={(event) =>
											setForm({ ...form, email: event.target.value })
										}
									/>
								</label>
								<label>
									City
									<input
										required
										value={form.city}
										onChange={(event) =>
											setForm({ ...form, city: event.target.value })
										}
									/>
								</label>
								<label>
									Status
									<select
										value={form.status}
										onChange={(event) =>
											setForm({ ...form, status: event.target.value })
										}
									>
										<option value="Active">Active</option>
										<option value="Pending">Pending</option>
										<option value="Inactive">Inactive</option>
									</select>
								</label>
								<button className="primary" type="submit" disabled={saving}>
									{saving ? "Saving..." : "Create"}
								</button>
							</form>
						</section>
					</>
				) : (
					<section className="card chat-card">
						<div className="chat-head">
							<h1>Enterprise Copilot</h1>
							<p className="subtitle">
								Attach a file, wait for indexing, then ask grounded questions.
							</p>
						</div>

						{chatError ? <div className="error">{chatError}</div> : null}

						<div className="job-list">
							{ingestionJobs.map((job) => (
								<div key={job.documentId} className="job-pill">
									<span>{job.fileName}</span>
									<span>
										{job.status}{" "}
										{typeof job.progressPercent === "number"
											? `(${job.progressPercent}%)`
											: ""}
									</span>
								</div>
							))}
						</div>

						<div className="message-stream">
							{messages.length === 0 ? (
								<div className="empty-state">
									No conversation yet. Upload a file and ask a question.
								</div>
							) : (
								messages.map((message) => (
									<div key={message.id} className={`message ${message.role}`}>
										<p>{message.text}</p>
										{Array.isArray(message.citations) &&
										message.citations.length > 0 ? (
											<div className="citations">
												{message.citations.map((citation) => (
													<span
														key={`${message.id}-${citation.chunkId}`}
														className="citation-chip"
													>
														{citation.fileName} · {citation.chunkId}
													</span>
												))}
											</div>
										) : null}
									</div>
								))
							)}
						</div>

						<form className="chat-composer" onSubmit={onSendChat}>
							<input
								type="text"
								placeholder="Ask about uploaded documents..."
								value={chatInput}
								onChange={(event) => setChatInput(event.target.value)}
							/>
							<input
								ref={fileInputRef}
								type="file"
								className="hidden-file-input"
								onChange={onAttachFile}
							/>
							<button
								type="button"
								onClick={() => fileInputRef.current?.click()}
								disabled={chatLoading || uploadingFile}
							>
								{uploadingFile ? "Uploading..." : "Attach"}
							</button>
							<span
								className="tooltip-anchor"
								title={sendDisabled ? sendDisabledReason : undefined}
							>
								<button
									className="primary"
									type="submit"
									disabled={sendDisabled}
								>
									{chatLoading ? "Thinking..." : "Send"}
								</button>
							</span>
						</form>
					</section>
				)
			) : null}
		</main>
	);
}
