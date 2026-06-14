import { useCallback, useEffect, useMemo, useRef, useState } from "react";

const emptyForm = {
	name: "",
	email: "",
	city: "",
	status: "Active",
};

const terminalStatuses = new Set(["Ready", "Failed"]);

export default function App() {
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

	const loadCustomers = useCallback(async () => {
		setDataError("");
		const response = await fetch("/api/customers");
		if (!response.ok) {
			throw new Error(`Failed to load customers (${response.status})`);
		}
		const data = await response.json();
		setCustomers(data);
	}, []);

	useEffect(() => {
		loadCustomers().catch((err) => setDataError(err.message));
	}, [loadCustomers]);

	const pendingJobIds = useMemo(
		() =>
			ingestionJobs
				.filter((job) => !terminalStatuses.has(job.status))
				.map((job) => job.documentId),
		[ingestionJobs],
	);

	useEffect(() => {
		if (pendingJobIds.length === 0) {
			return undefined;
		}

		const intervalId = setInterval(async () => {
			await Promise.all(
				pendingJobIds.map(async (documentId) => {
					try {
						const response = await fetch(`/api/uploads/${documentId}/status`);
						if (!response.ok) {
							return;
						}

						const latest = await response.json();
						setIngestionJobs((prev) =>
							prev.map((item) => (item.documentId === documentId ? latest : item)),
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
	}, [pendingJobIds]);

	const pendingUploadCount = useMemo(
		() => ingestionJobs.filter((job) => !terminalStatuses.has(job.status)).length,
		[ingestionJobs],
	);
	const hasUploadedFile = ingestionJobs.length > 0;
	const hasReadyDocument = useMemo(
		() =>
			Boolean(activeDocumentId) &&
			ingestionJobs.some((job) => job.documentId === activeDocumentId && job.status === "Ready"),
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
		setSaving(true);
		setDataError("");

		try {
			const response = await fetch("/api/customers", {
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
		setDataError("");
		try {
			const response = await fetch(`/api/customers/${id}`, { method: "DELETE" });
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
		setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: "system", text }]);
	}

	async function onAttachFile(event) {
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
			const uploadResponse = await fetch("/api/uploads", {
				method: "POST",
				body: formData,
			});
			if (!uploadResponse.ok) {
				const text = await uploadResponse.text();
				throw new Error(text || `Blob upload failed (${uploadResponse.status})`);
			}

			const uploadPayload = await uploadResponse.json();

			const ingestResponse = await fetch("/api/ingest", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({ documentId: uploadPayload.documentId }),
			});
			if (!ingestResponse.ok) {
				const text = await ingestResponse.text();
				throw new Error(text || `Failed to trigger ingestion (${ingestResponse.status})`);
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
		if (!chatInput.trim() || sendDisabled) {
			return;
		}

		const messageText = chatInput.trim();
		setChatInput("");
		setChatError("");
		setChatLoading(true);

		const userMessage = { id: crypto.randomUUID(), role: "user", text: messageText };
		setMessages((prev) => [...prev, userMessage]);

		try {
			const response = await fetch("/api/chat", {
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
			<header className="tab-header">
				<button
					type="button"
					className={activeTab === "data" ? "tab active" : "tab"}
					onClick={() => setActiveTab("data")}
				>
					Database View
				</button>
				<button
					type="button"
					className={activeTab === "chat" ? "tab active" : "tab"}
					onClick={() => setActiveTab("chat")}
				>
					AI Chat {pendingUploadCount > 0 ? `(${pendingUploadCount})` : ""}
				</button>
			</header>

			{activeTab === "data" ? (
				<>
					<section className="card">
						<h1>Customer Records</h1>
						<p className="subtitle">React frontend to backend API to SQL Server</p>
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
												<button type="button" onClick={() => onDeleteCustomer(customer.id)}>
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
									onChange={(event) => setForm({ ...form, name: event.target.value })}
								/>
							</label>
							<label>
								Email
								<input
									required
									type="email"
									value={form.email}
									onChange={(event) => setForm({ ...form, email: event.target.value })}
								/>
							</label>
							<label>
								City
								<input
									required
									value={form.city}
									onChange={(event) => setForm({ ...form, city: event.target.value })}
								/>
							</label>
							<label>
								Status
								<select
									value={form.status}
									onChange={(event) => setForm({ ...form, status: event.target.value })}
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
						<p className="subtitle">Attach a file, wait for indexing, then ask grounded questions.</p>
					</div>

					{chatError ? <div className="error">{chatError}</div> : null}

					<div className="job-list">
						{ingestionJobs.map((job) => (
							<div key={job.documentId} className="job-pill">
								<span>{job.fileName}</span>
								<span>
									{job.status} {typeof job.progressPercent === "number" ? `(${job.progressPercent}%)` : ""}
								</span>
							</div>
						))}
					</div>

					<div className="message-stream">
						{messages.length === 0 ? (
							<div className="empty-state">No conversation yet. Upload a file and ask a question.</div>
						) : (
							messages.map((message) => (
								<div key={message.id} className={`message ${message.role}`}>
									<p>{message.text}</p>
									{Array.isArray(message.citations) && message.citations.length > 0 ? (
										<div className="citations">
											{message.citations.map((citation) => (
												<span key={`${message.id}-${citation.chunkId}`} className="citation-chip">
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
							<button className="primary" type="submit" disabled={sendDisabled}>
								{chatLoading ? "Thinking..." : "Send"}
							</button>
						</span>
					</form>
				</section>
			)}
		</main>
	);
}
