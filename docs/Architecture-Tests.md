# Architecture Tests

The backend test suite (`src/Backend.Tests/`) enforces architectural boundaries, auth posture, and endpoint contracts as a regression guard in CI/CD.

> Source of truth: the test files themselves. If this document and the tests differ, follow the tests.

## Framework

- **Test framework**: [xUnit](https://xunit.net/)
- **Assertions**: [Shouldly](https://shouldly.io/)
- **Approach**: source-level checks for structure/middleware, plus in-memory integration tests (`WebApplicationFactory`) for endpoint behavior.

Run the suite:

```bash
dotnet test src/Backend.Tests/Backend.Tests.csproj
```

## What Is Covered

### Dependency & structure (`DependencyArchitectureTests`)

- `Backend_Should_Contain_Expected_Feature_Slices` — verifies the `Health`, `AiPing`, `Customers`, `DocumentIngestion`, and `Chat` slice files exist.
- `Features_Should_Not_Depends_On_Other_Features` — scans feature sources for cross-feature `using` statements and fails on any coupling between slices.
- `Program_Should_Enforce_Auth_Middleware` — asserts `Program.cs` wires `UseAuthentication()`, `UseAuthorization()`, `UseRateLimiter()`, and `AddEntraAuth`, and that `EntraAuthSetup` calls `AddJwtBearer`.

### Auth behavior (`AuthIntegrationTests`)

- Health endpoint is anonymous.
- Protected endpoints return `401` without a token, `200` with a valid token.
- Scope-protected endpoints return `403` without the required scope, `200` with it.

### Endpoint contracts (`ApiEndpointIntegrationTests`)

- Chat validation (`400` when message missing or docs-mode search unconfigured), general-mode `200` envelope, and docs-mode `200` with citations using a controlled local-retrieval fake.
- Upload/signed-URL validation (`400` for unconfigured pipeline, non-multipart content, or unsupported file extensions).
- Ingestion trigger/status auth enforcement (`401` unauthenticated) and authenticated happy-path (`202` Accepted + current state).

### Pipeline & config guards

- `ProgramPipelineIntegrationTests` — runs the real program pipeline to confirm anonymous health, `401` on protected customers without a token, and reachable endpoints when Entra auth is disabled.
- `ValidationAndConfigGuardTests` — Entra option resolution guards (disabled vs. enabled, dev vs. production) and file-name validation rules (allow-list, path-traversal, invalid characters).

## Extending

Add a test class under `src/Backend.Tests/` — it is picked up automatically by the build and CI/CD. Keep new architecture assertions source-level (path/string checks) and behavioral assertions integration-style via `WebApplicationFactory`.
