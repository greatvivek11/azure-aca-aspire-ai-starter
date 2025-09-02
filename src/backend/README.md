# AI Hub Backend

This is the backend service for the AI Hub, built with .NET 9 Minimal API following Vertical Slice Architecture (VSA).

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

- `HUGGINGFACE_API_KEY` - API key for Hugging Face (recommended to use environment variable for security)
- `HUGGINGFACE_MODEL_ID` - Model ID to use (default: gpt2)
- `HUGGINGFACE_ENDPOINT` - Hugging Face API endpoint (default: https://api-inference.huggingface.co/v1/)

### Setting Credentials

For development, you can set the environment variable in several ways:

1. **Terminal/Command Line (Temporary)**:
   ```bash
   export HUGGINGFACE_API_KEY=your_api_key_here
   dotnet run
   ```

2. **Edit Properties/launchSettings.json**:
   Update the `HUGGINGFACE_API_KEY` value in the launchSettings.json file (but don't commit it to source control).

3. **System Environment Variables (Permanent)**:
   Set the environment variable in your operating system's environment settings.

The configuration follows the standard .NET hierarchy:
1. Environment variables (highest priority)
2. appsettings.{Environment}.json (e.g., appsettings.Development.json)
3. appsettings.json (lowest priority)

**Important**: Never commit real API keys to source control. The `appsettings.Development.json` file is included in `.gitignore` to prevent accidental commits.