# Network Hardening Extension Guide (VNET and Private Endpoints)

This guide describes the recommended next-step hardening path beyond the default template posture.

## Current Baseline

The default template deploys with:
- Frontend public ingress in Azure Container Apps.
- Backend and worker private ingress in Azure Container Apps.
- Entra ID authentication for frontend-to-backend API access.

This baseline is enterprise-friendly for many internal use cases while keeping one-click deployment simple.

## Goal State (Extended Hardening)

Adopt a private-network-first model by adding:
- ACA environment integrated with a dedicated VNET/subnet design.
- Private Endpoints for data-plane services (SQL, Storage, Search, AI services where applicable).
- Controlled egress and ingress rules.
- DNS resolution via private DNS zones.

## Recommended Topology

1. Allocate dedicated subnets:
- `aca-environment-subnet` for Container Apps environment integration.
- `private-endpoints-subnet` for Private Endpoints.
- Optional `firewall-subnet` for centralized egress controls.

2. Private Endpoint targets:
- Azure SQL Database (`privatelink.database.windows.net`).
- Azure Storage (`privatelink.blob.core.windows.net`).
- Azure AI Search (`privatelink.search.windows.net`).
- Azure AI/Foundry endpoint (service-specific private link support).

3. DNS:
- Link private DNS zones to the VNET.
- Ensure ACA workloads resolve private endpoints over private DNS.

## Hardening Sequence

1. Introduce VNET/subnet parameters in IaC as optional extension flags.
2. Create and link private DNS zones.
3. Add Private Endpoints for each enabled dependent service.
4. Update service endpoint configurations to use private FQDN resolution.
5. Restrict public network access where supported and verified.
6. Validate workload connectivity and health checks.

## Validation Checklist

- Backend/worker can connect to SQL, Storage, Search, and AI endpoints through private networking.
- Public network access is disabled on services that support it and are covered by private connectivity.
- Frontend ingress remains intentional and controlled.
- No regression in upload, ingestion, and chat flows.

## Operational Considerations

- Private networking introduces DNS and route dependencies; include diagnostics scripts for name resolution and connectivity.
- Roll out progressively by environment (`dev` -> `staging` -> `prod`) and validate per service.
- Keep fallback runbook steps for temporary rollback to public endpoints in incident scenarios.
