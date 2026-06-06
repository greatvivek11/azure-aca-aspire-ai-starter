# AI Hub Backend

This is the backend service for the AI Hub, built with .NET 10 Minimal API following Vertical Slice Architecture (VSA).

## Project Structure

```
/src/backend/
├── Domain/                 # Core POCO entities
├── Features/               # Feature slices (organized by functionality)
│   ├── Health/             # Health check endpoint
│   └── AiPing/             # AI connectivity test endpoint
├── Infrastructure/         # External service implementations
│   └── Ai/                 # AI service abstractions and implementations
├── Shared/                 # Cross-cutting concerns
├── Program.cs              # Application entry point
├── Backend.csproj          # Project file
└── Dockerfile              # Container definition
```

## Features

1. **Health Check** - GET `/v1/health`
2. **AI Ping** - GET `/v1/ping-ai`

## Running the Application

### Local Development

```bash
dotnet run
```

### With Aspire

Run the Aspire AppHost project to orchestrate all services.

### Docker

```bash
docker build -t aihub-backend .
docker run -p 8080:8080 aihub-backend
```

## Configuration

The application can be configured using appsettings.json files or environment variables:

- `AZURE_OPENAI_API_KEY` - API key for Azure AI Foundry/Azure OpenAI (recommended to use environment variable for security)
- `AZURE_OPENAI_MODEL_ID` - Deployed model name (default: gpt-4.1)
- `AZURE_OPENAI_ENDPOINT` - Azure OpenAI endpoint (default: https://your-foundry-resource.openai.azure.com/)

### Setting Credentials

For development, you can set the environment variable in several ways:

1. **Terminal/Command Line (Temporary)**:
   ```bash
   export AZURE_OPENAI_API_KEY=your_api_key_here
   dotnet run
   ```

2. **Edit Properties/launchSettings.json**:
   Update the `AZURE_OPENAI_API_KEY` value in the launchSettings.json file (but don't commit it to source control).

3. **System Environment Variables (Permanent)**:
   Set the environment variable in your operating system's environment settings.

The configuration follows the standard .NET hierarchy:
1. Environment variables (highest priority)
2. appsettings.{Environment}.json (e.g., appsettings.Development.json)
3. appsettings.json (lowest priority)

**Important**: Never commit real API keys to source control. The `appsettings.Development.json` file is included in `.gitignore` to prevent accidental commits.