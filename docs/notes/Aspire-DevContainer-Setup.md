# Aspire Development with Dev Containers

This document provides guidance on setting up and troubleshooting local development of .NET Aspire applications using Dev Containers, Docker Compose, and containerized services.

## Overview

The recommended development workflow involves:
1. Running the development environment inside a VS Code Dev Container
2. Using Aspire to orchestrate application services as containers
3. Maintaining parity between local development and production (ACA) environments

## Prerequisites

- VS Code with Remote Development extension pack
- Docker Desktop (or equivalent)
- .NET SDK (matching your project's target framework)

## Configuration Files

### Dev Container Configuration

- `.devcontainer/devcontainer.json`: Defines the dev container environment, including Docker-in-Docker support and port forwarding
- `.devcontainer/Dockerfile`: Base image for the dev container (should match your target .NET version)
- `.devcontainer/docker-compose.yml`: Defines services in the dev container environment (e.g., SQL Server)

### Aspire Configuration

- `src/aspire/Properties/launchSettings.json`: Configures how the Aspire AppHost runs locally
- `src/aspire/AppHost.cs`: Defines application services and their orchestration

### VS Code Configuration

- `.vscode/launch.json`: Configures how to launch and debug the Aspire AppHost
- `.vscode/tasks.json`: Defines build tasks and other operations

## Running the Application

1. Open the project in VS Code
2. When prompted, reopen in Dev Container
3. Wait for the container to build and initialize
4. Press F5 to start debugging the Aspire AppHost
5. Access the Aspire dashboard at the URL shown in the terminal (usually `https://localhost:[port]`)

## Run Modes (Native vs Dev Container)

This is a platform-agnostic starter. You can run the full stack three ways, and they coexist without interfering with each other:

| Mode | How to run | `node_modules` used | Native binaries |
| --- | --- | --- | --- |
| **Native macOS** | `dotnet run` from `src/aspire` (or F5 on the host) | host `src/frontend/node_modules` | `darwin-arm64` |
| **Native Windows** | `dotnet run` from `src/aspire` (or F5 on the host) | host `src/frontend/node_modules` | `win32-x64` |
| **Dev Container** (any host OS) | Reopen in container, then F5 | isolated `frontend-node-modules` Docker volume | `linux-*` |

### Why these don't conflict

Vite/rolldown ship platform-specific native bindings (e.g. `@rolldown/binding-darwin-arm64` vs `@rolldown/binding-linux-arm64-gnu`). Sharing one `node_modules` across host and container would mix incompatible binaries.

To prevent that, `.devcontainer/docker-compose.yml` mounts a dedicated Docker volume (`frontend-node-modules`) over `src/frontend/node_modules`. That volume **only exists inside the container**, so:

- Native runs on your host use the host folder directly and never see the volume.
- The Dev Container gets its own Linux `node_modules`, populated by `npm ci --include=optional` during `postCreate`.
- You can freely alternate between native macOS and the Dev Container without reinstalling dependencies.

`node_modules/` is gitignored, so no platform-specific binaries are ever committed.

> The repo is bind-mounted at the fixed path `/workspaces/app` (not the host folder name), so the volume mapping keeps working even if you fork or rename the repository.

## Common Issues and Solutions

### SSL Certificate Issues

**Problem:** Aspire dashboard fails to start with an HTTPS certificate error.

**Solution:**
1. Ensure a development certificate exists by running in the dev container terminal:
   ```bash
   dotnet dev-certs https --trust
   ```
2. Restart the debugging session (Shift+F5, then F5)

**Note:** Browsers will show a security warning for the self-signed certificate. This is normal for local development. In production environments like Azure Container Apps, SSL certificates are managed by the platform.

### Port Forwarding Issues

**Problem:** Aspire dashboard or services are not accessible from the host machine.

**Solution:**
1. Check VS Code's "Ports" panel to see if the required ports are being forwarded
2. If a port is not forwarded, click the "Forward Port" button in the panel
3. Try accessing `http://localhost:[port]` instead of `https://localhost:[port]` if there are SSL issues
4. Ensure no other applications are using the same ports on your host machine

### Docker-in-Docker (DinD) Issues

**Problem:** Aspire cannot build or run service containers.

**Solution:**
1. Verify that the dev container configuration includes Docker-in-Docker support:
   ```json
   "features": {
     "ghcr.io/devcontainers/features/docker-in-docker:2": {
       "enableNonRootDocker": true,
       "moby": true
     }
   }
   ```
2. Restart the dev container after making changes to the configuration

## Best Practices

1. **Maintain Environment Parity:** Structure your local development to mirror production as closely as possible. This includes running application services as containers orchestrated by Aspire.
2. **Use Dynamic Ports:** Allow Aspire to assign dynamic ports for services to avoid conflicts.
3. **Version Control Configuration:** Keep all dev container and VS Code configuration files in version control.
4. **Document Customizations:** Keep notes on any project-specific configurations or workarounds.
5. **Regular Updates:** Keep your dev container base images and VS Code extensions up to date.

## File Structure

```
project-root/
├── .devcontainer/
│   ├── devcontainer.json
│   ├── Dockerfile
│   └── docker-compose.yml
├── .vscode/
│   ├── launch.json
│   └── tasks.json
├── src/
│   ├── aspire/
│   │   ├── AppHost.cs
│   │   └── Properties/
│   │       └── launchSettings.json
│   ├── backend/
│   ├── frontend/
│   └── worker/
└── docs/
    └── notes/
        └── Aspire-DevContainer-Setup.md (this file)
```

## Platform-Specific Database Configuration

The dev container is configured with comments in the `.devcontainer/docker-compose.yml` file to guide developers in selecting the appropriate database image for their platform:

1. For x86_64 systems (Intel/AMD), use Microsoft SQL Server
2. For ARM64 systems (Apple Silicon, Raspberry Pi, etc.), use Azure SQL Edge

Developers can simply uncomment the appropriate lines in the docker-compose.yml file for their platform. This approach eliminates the need for dynamic script generation and allows for easy manual configuration.

## Troubleshooting Checklist

1. Verify dev container is running with Docker-in-Docker support
2. Check that `dotnet dev-certs https --trust` has been run
3. Confirm Aspire AppHost builds successfully
4. Check VS Code's Ports panel for forwarded ports
5. Review Aspire logs in `/tmp/aspire.*` directories if services fail to start
6. Ensure no port conflicts with other applications
7. Restart dev container and debugging session after configuration changes

This setup provides a robust, containerized development environment that closely mirrors production deployment on Azure Container Apps.