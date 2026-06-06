import { useCallback, useEffect, useState } from "react";

const emptyForm = {
  name: "",
  email: "",
  city: "",
  status: "Active"
};

export default function App() {
  const [customers, setCustomers] = useState([]);
  const [form, setForm] = useState(emptyForm);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const loadCustomers = useCallback(async () => {
    setError("");
    const response = await fetch("/api/customers");
    if (!response.ok) {
      throw new Error(`Failed to load customers (${response.status})`);
    }
    const data = await response.json();
    setCustomers(data);
  }, []);

  useEffect(() => {
    loadCustomers().catch((err) => setError(err.message));
  }, [loadCustomers]);

  async function onCreateCustomer(event) {
    event.preventDefault();
    setSaving(true);
    setError("");

    try {
      const response = await fetch("/api/customers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form)
      });

      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || `Failed to create record (${response.status})`);
      }

      setForm(emptyForm);
      await loadCustomers();
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  }

  async function onDeleteCustomer(id) {
    setError("");
    try {
      const response = await fetch(`/api/customers/${id}`, { method: "DELETE" });
      if (!response.ok && response.status !== 404) {
        const text = await response.text();
        throw new Error(text || `Failed to delete record (${response.status})`);
      }
      await loadCustomers();
    } catch (err) {
      setError(err.message);
    }
  }

  return (
    <main className="app-shell">
      <section className="card">
        <h1>Customer Records</h1>
        <p className="subtitle">React frontend to Dapr sidecar to backend API to SQL Server</p>

        {error ? <div className="error">{error}</div> : null}

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
    </main>
  );
}
