# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a smart load balancer for OpenAI endpoints built with C# .NET 8 and Microsoft YARP (Yet Another Reverse Proxy). It provides intelligent failover and throttling-aware routing for Azure OpenAI services, handling HTTP 429 (Too Many Requests) responses and "Retry-After" headers automatically.

## Architecture

- **Framework**: ASP.NET Core 8.0 with YARP reverse proxy
- **Main Components**:
  - `Program.cs`: Application entry point and YARP configuration
  - `BackendConfig.cs`: Backend endpoint configuration loader
  - `YarpConfiguration.cs`: YARP route and cluster configuration
  - `RetryMiddleware.cs`: Custom middleware for retry logic and backend failover
  - `ThrottlingHealthPolicy.cs`: Health check policy for handling OpenAI throttling

The load balancer uses environment variables to configure multiple OpenAI backends with priority-based routing. When a backend returns 429 errors, it's temporarily marked as unhealthy and traffic is routed to other available backends.

## Development Commands

### Build and Run
```bash
# Build the project
dotnet build src/openai-loadbalancer.csproj

# Run the application locally
dotnet run --project src/openai-loadbalancer.csproj

# Run with specific configuration
dotnet run --project src/openai-loadbalancer.csproj --environment Development
```

### Testing
```bash
# Run any tests (if present)
dotnet test src/openai-loadbalancer.sln
```

### Docker Operations
```bash
# Build Docker image
cd src
docker build -t aoai-smart-loadbalancing:v1 .

# Run container locally
docker run -p 8080:8080 aoai-smart-loadbalancing:v1
```

## Backend Configuration

The load balancer is configured via environment variables following this pattern:
- `BACKEND_X_URL`: Full Azure OpenAI endpoint URL
- `BACKEND_X_PRIORITY`: Priority level (lower numbers = higher priority)
- `BACKEND_X_APIKEY`: API key (optional if using managed identity)
- `BACKEND_X_DEPLOYMENT_NAME`: Override deployment name in requests (optional)

Global settings:
- `LB_API_KEY`: API key for load balancer authentication (optional but recommended)
- `HTTP_TIMEOUT_SECONDS`: Request timeout (default: 100 seconds)

## Authentication

The load balancer supports API key authentication via the `LB_API_KEY` environment variable. When set, clients must provide the API key using one of these headers:
- `Authorization: Bearer <api_key>`
- `X-API-Key: <api_key>`
- `api-key: <api_key>`

If `LB_API_KEY` is not configured, authentication is disabled (not recommended for production).

## Deployment

### Azure Developer CLI (Recommended)
```bash
# Deploy to Azure
azd up

# Set custom OpenAI capacity before deployment
azd env set OPENAI_CAPACITY 50

# Tear down resources
azd down
```

### Infrastructure
- **Provider**: Azure Bicep templates in `/infra` directory
- **Main template**: `infra/main.bicep`
- **Target platform**: Azure Container Apps
- **Default deployment**: 3 GPT-3.5-turbo backends with 30K TPM each

## Key Features

1. **Priority-based routing**: Routes to highest priority available backends first
2. **Throttling awareness**: Automatically handles 429 responses and Retry-After headers
3. **Immediate failover**: No artificial delays between backend attempts
4. **Health monitoring**: Uses YARP passive health checks to track backend availability
5. **Deployment name override**: Can route to different deployment names per backend

## Health Monitoring

- Health endpoint: `/healthz`
- YARP provides detailed logging of proxy operations and health state changes
- Backends are marked unhealthy when returning 429/5xx and automatically reactivated after Retry-After period

## Security Notes

- API keys from client requests are overridden by configured backend keys
- Supports Azure managed identity authentication when API keys are not provided
- Uses Azure.Identity library for credential management