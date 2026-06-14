# Local Development with Dapr: Architecture and Trade-offs

## Overview

This document explains the service communication strategy for local development vs. production environments.

## Development Modes

### 1. **Local Vite Dev Mode** (Recommended for Frontend Development)
```bash
ASPIRE_FRONTEND_MODE=vite-dev dotnet run --project src/aspire/AppHost.csproj
```

**Communication Pattern:**
```
Frontend (Vite HMR)  →  Direct HTTP  →  Backend (Docker)
   port 3000                          port 8080
```

**Characteristics:**
- ✅ **Fast iteration**: Hot Module Reload (HMR) for React changes
- ✅ **Simple debugging**: Direct HTTP calls visible in browser DevTools
- ✅ **No Dapr overhead**: No sidecar indirection, instant feedback
- ✅ **Zero setup**: No additional services needed beyond Docker containers
- ⚠️ **Not production-like**: Direct HTTP instead of service mesh

**When to use:**
- Daily frontend development
- Rapid iteration on UI/UX
- Quick API contract testing
- Hot reload not needed for backend

### 2. **Docker Container Mode** (Default, Production-Like)
```bash
# Default if ASPIRE_FRONTEND_MODE is not set
dotnet run --project src/aspire/AppHost.csproj
```

**Communication Pattern:**
```
Frontend (Container)  →  Dapr Sidecar  →  Backend (Container)
                          (Service Invocation)
```

**Characteristics:**
- ✅ **Production-like**: Uses Dapr service invocation (same as production)
- ✅ **Service mesh**: Dapr handles discovery, mTLS, resilience
- ✅ **Testing infrastructure**: Tests Dapr integration end-to-end
- ⚠️ **Requires Dapr placement**: Must have `dapr placement-service` running
- ⚠️ **Slower iteration**: No HMR; requires full container rebuild
- ⚠️ **Harder debugging**: Service invocation adds complexity

**When to use:**
- End-to-end testing before deployment
- Testing service resilience patterns
- Validating Dapr integration
- Integration testing across services

## Architecture Decision: Why Direct HTTP for Local Vite Dev?

### The Principle
In enterprise cloud architecture, Dapr service invocation is the **correct pattern**:
- ✓ Service discovery abstraction
- ✓ mTLS between services
- ✓ Resilience patterns (retry, circuit breaker, timeout)
- ✓ Observable service mesh behavior
- ✓ Production-ready and scalable

### The Pragmatic Reality
Dapr service invocation requires **Dapr placement service** for service discovery. Running this locally adds:
- Additional setup step before Aspire starts
- Extra process management complexity
- Minimal value for local frontend iteration

### The Resolution: Mode-Based Approach
```
LOCAL (vite-dev)         →  Direct HTTP  (velocity)
DOCKER (container mode)  →  Dapr (architecture validation)
PRODUCTION (ACA)         →  Dapr (service mesh at scale)
```

**This is NOT an architectural compromise because:**
1. Backend is **fully Dapr-enabled** (proper service invocation with worker)
2. Docker mode **validates Dapr integration** end-to-end
3. Production **uses Dapr service mesh** as designed
4. Local dev is just optimized for **developer velocity**

### Analogy
Similar to how local development often skips database migrations, load balancing, or CDN caching—you test the architecture in staging/production, but iterate quickly locally.

## Switching Between Modes

### Enable Direct HTTP (Vite Dev, Default)
```bash
# Set environment variable
export ASPIRE_FRONTEND_MODE=vite-dev

# Frontend runs on port 3000 with HMR
# Proxy: /api/* → http://localhost:8080
dotnet run --project src/aspire/AppHost.csproj
```

### Enable Dapr Service Invocation (Container Mode)
```bash
# Option 1: Explicitly set to container mode
export ASPIRE_FRONTEND_MODE=container

# Option 2: Unset the variable (defaults to container)
unset ASPIRE_FRONTEND_MODE

# Requires dapr placement service:
dapr run --dapr-placement-service-port 50005

# Then start Aspire (frontend rebuilds in container)
dotnet run --project src/aspire/AppHost.csproj
```

## Configuration

### Frontend Proxy Configuration

The proxy behavior is controlled in `src/frontend/vite.config.js`:

```javascript
// Default for vite-dev: direct HTTP
const backendProxyBaseUrl = process.env.BACKEND_PROXY_BASE_URL || "http://127.0.0.1:8080";

// Proxy routes:
// /api/customers  → /v1/customers
// /api/uploads    → /v1/uploads
// /api/ingest     → /v1/ingest
// /api/chat       → /v1/chat
```

### Override Backend URL

To test Dapr service invocation locally without rebuilding containers:

```bash
# Start placement service first
dapr run --dapr-placement-service-port 50005 &

# Set frontend to use Dapr proxy
export BACKEND_PROXY_BASE_URL="http://127.0.0.1:3500/v1.0/invoke/api/method"

# Run Aspire
cd src/aspire && dotnet run
```

## Troubleshooting

### Frontend can't reach backend in vite-dev
- Check: `curl http://localhost:8080/v1/customers`
- Ensure backend container is running: `docker ps | grep backend`
- Check backend logs: `docker logs <backend-container-id>`

### Dapr service invocation fails in container mode
- Ensure placement service is running: `ps aux | grep "dapr.*placement"`
- Check Dapr sidecar logs: `docker logs <container-id>`
- Verify Dapr metadata: `curl http://localhost:3500/v1.0/metadata`
- Common error: `"couldn't find service: api"` means placement is disconnected

### HMR not working in vite-dev mode
- Ensure `ASPIRE_FRONTEND_MODE=vite-dev` is set
- Check Vite is running: `curl http://localhost:3000`
- Check network tab in browser DevTools for WebSocket connection

## References

- [Dapr Service Invocation](https://docs.dapr.io/developing-applications/building-blocks/service-invocation/)
- [Vite Dev Server Configuration](https://vitejs.dev/config/server-options.html#server-proxy)
- [.NET Aspire Dapr Integration](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dapr)
