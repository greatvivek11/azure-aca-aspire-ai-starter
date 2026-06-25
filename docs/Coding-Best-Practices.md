# Coding Best Practices

This guide defines coding standards for this repository. It complements existing architecture guidance and applies to backend, worker, frontend, and infrastructure automation.

## SOLID Expectations

- Single Responsibility Principle (SRP): each class/module should have one clear responsibility.
- Open/Closed Principle (OCP): extend behavior through new implementations instead of editing stable code paths.
- Liskov Substitution Principle (LSP): implementations of an interface must be safely interchangeable.
- Interface Segregation Principle (ISP): prefer small, purpose-built interfaces over broad multipurpose contracts.
- Dependency Inversion Principle (DIP): high-level orchestration depends on abstractions, not concrete infrastructure.

## Design Pattern Guidance

Use patterns intentionally based on context.

- Strategy pattern:
  - Use when runtime mode changes behavior (for example local vs Azure embedding/indexing).
  - Keep strategy selection in composition root (`Program.cs`) or a dedicated orchestrator.
- Repository pattern:
  - Use for persistence boundaries to keep SQL logic contained.
  - Expose task-focused methods rather than generic CRUD interfaces.
- Adapter pattern:
  - Wrap external SDKs or HTTP services so business workflows remain SDK-agnostic.
- Pipeline/Orchestrator pattern:
  - Use for multi-step processing workflows.
  - Keep each step side-effect aware and independently testable.

## Project Organization Rules

- Keep `Program.cs` startup-only: composition, middleware, endpoints, and hosted loop bootstrap.
- Place domain types in `Domain/` with no infrastructure dependencies.
- Place infrastructure concerns under `Infrastructure/` grouped by concern (auth, storage, database, messaging).
- Place processing workflows under a dedicated feature folder (for worker: `DocumentProcessing/`).
- Keep feature boundaries explicit and avoid cross-feature coupling.

## Error Handling And Resilience

- Fail fast for invalid configuration at startup.
- Classify transient errors and apply bounded retries with backoff.
- Preserve idempotency where retries can re-run operations.
- Include actionable context in logs without leaking secrets.

## Configuration And Secrets

- Read runtime configuration via centralized options objects.
- Never hardcode credentials, keys, or tenant-specific values.
- Use managed identity where available and define fallback auth behavior explicitly.

## Testing Standards

- Add unit tests for orchestration and business rules.
- Add integration tests for infrastructure adapters and SQL contracts.
- Keep tests deterministic; avoid clock/network dependence unless explicitly integration-scoped.
- When fixing bugs, add or update tests that fail before the fix and pass after.

## Documentation Expectations

- Document architecture decisions when introducing a new pattern or folder structure.
- Keep examples aligned with live code and manifests.
- Update docs in the same PR when behavior or structure changes.
