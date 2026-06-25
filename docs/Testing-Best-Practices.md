# Testing Best Practices

This template prioritizes confidence with low maintenance cost. Keep tests fast, deterministic, and architecture-focused.

## Test Pyramid For This Template

- Guard tests (required): architecture and setup-contract checks that enforce project integrity.
- API integration tests (required): in-memory endpoint contract tests for auth, validation, and happy paths.
- Infrastructure integration tests (optional): adapter tests that hit real SQL/search/storage in controlled environments.
- Browser E2E (optional): only for critical end-to-end smoke flows, run manually or on schedule.

## Required Baseline In CI

- Run backend tests from `src/Backend.Tests/Backend.Tests.csproj`.
- Keep architecture guard tests green for backend and worker boundaries.
- Keep API contract tests for chat, ingestion, and customer CRUD paths green.

## Scope Control Rules

- Prefer adding/adjusting existing tests over creating many new suites.
- Add new tests when behavior changes, bug fixes land, or architecture boundaries are introduced.
- Avoid broad network-dependent tests in the default PR pipeline.

## Contract Test Guidance

- Validate status codes, response envelopes, and auth requirements.
- Use test doubles/in-memory repositories for endpoint contracts.
- Keep one assertion objective per test name.

## Optional E2E Lane

- Keep E2E non-blocking for template consumers.
- Trigger manually (`workflow_dispatch`) or on low-frequency schedule.
- Use E2E for smoke confidence only (for example upload -> ingest trigger -> chat docs-mode response), not exhaustive permutations.

## PR Checklist

- Are architecture guards still valid?
- Are contract tests updated for changed routes/DTOs?
- Are new tests minimal and deterministic?
- If adding E2E, is it optional and non-blocking?
