# 🤖 AI-Powered Knowledge Hub

This repository contains the source code for a modern, cloud-native AI assistant designed to serve as an internal "Copilot" for enterprise knowledge. It allows users to chat with, upload, and analyze company documents using a sophisticated, scalable, and secure architecture.

This project is built to showcase advanced skills in full-stack development, AI integration, and cloud-native architecture on Microsoft Azure.

---

## ✨ Core Features

-   **Conversational AI**: Chat with an AI assistant that can reason over and answer questions about your documents.
-   **Document Ingestion**: Upload PDF, DOCX, TXT, and image files for the AI to process and index.
-   **Retrieval-Augmented Generation (RAG)**: Get answers grounded in your documents, complete with citations.
-   **Vision & OCR**: Analyze images and extract text from scanned documents.
-   **Sentiment Analysis**: Understand the sentiment of customer feedback or other text data.
-   **Secure & Scalable**: Built on a secure, cloud-native foundation using modern best practices.

---

## 🏛️ Architecture Overview

This project is built using a modern, distributed architecture designed for scalability, maintainability, and security.

-   **Backend**: A **.NET 10** application built with **ASP.NET Core Minimal APIs** following a **Vertical Slice Architecture (VSA)** with clean, feature-focused code.
-   **Frontend**: A **React 19** Single Page Application (SPA) built with **Vite** and **Bun**, styled with **Tailwind CSS** and **shadcn/ui**.
-   **Cloud Platform**: Hosted entirely on **Azure Container Apps**, with a containerized frontend and backend.
-   **Service Communication**: **Dapr (Distributed Application Runtime)** is used for secure, internal service-to-service communication.
-   **AI Orchestration**: **Semantic Kernel** is used to orchestrate calls to the **Hugging Face Inference Router**, providing flexibility in model choice.
-   **Data Storage**:
    -   **Azure SQL Database**: For structured, relational data.
    -   **Azure Blob Storage**: For all uploaded documents and files.
    -   **Azure Cosmos DB**: For vector embeddings to power the RAG pipeline.
-   **Security**: Authentication is handled by **Microsoft Entra ID** using the **MSAL** library. All communication between the backend and Azure services uses passwordless **Managed Identities**.
-   **DevOps**: Infrastructure is defined with **Bicep (IaC)** and deployed automatically via a **GitHub Actions** CI/CD pipeline.

For a deeper dive, please see the detailed architectural documents:

-   **[Cloud Architecture](./docs/Cloud-Architecture.md)**: The holistic, end-to-end deployment and security plan.
-   **[Backend Architecture](./docs/Backend-Architecture.md)**: The internal structure of the .NET backend.
-   **[Frontend Architecture](./docs/Frontend-Architecture.md)**: The technology stack and patterns for the React frontend.

---

## 🚀 Getting Started

To run this project locally, you will need the following prerequisites installed:

-   [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
-   [Bun](https://bun.sh/)
-   [Docker Desktop](https://www.docker.com/products/docker-desktop/)
-   [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
-   [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)

## 🛠️ Local Development Setup

### 1. Clone the Repository
```bash
git clone <repository-url>
cd <repository-name>
```

### 2. Set Up Environment Variables
Copy the example environment file and fill in your actual values:
```bash
cp src/aspire/.env.example src/aspire/.env
```

Edit `src/aspire/.env` and fill in your actual values for Hugging Face API key and other configuration.

### 3. Run the Application
Navigate to the Aspire project directory and run the application:
```bash
cd src/aspire
dotnet run
```

The application will automatically load configuration from the `.env` file.

Note: The `.env` file is gitignored and will not be committed to the repository.

## 🚀 Running the Application

After completing the setup steps above, you can run the application with:
```bash
cd src/aspire
dotnet run
```

This will start all services (frontend, backend, and worker) with Dapr sidecars.
---

## ✅ Testing

### Architecture Tests

We enforce architectural boundaries and best practices using xUnit tests. These tests validate:
- ✅ Backend does NOT depend on Frontend
- ✅ Backend has required infrastructure dependencies (Dapr, SQL, Semantic Kernel)
- ✅ Features follow Vertical Slice Architecture
- ✅ Features are independent (no cross-feature coupling)

Run architecture tests locally:
```bash
dotnet test src/Backend.Tests/Backend.Tests.csproj
```

For detailed information, see [Architecture-Tests.md](./docs/Architecture-Tests.md)

---

## 🚀 Cloud Deployment

### Prerequisites for Azure Deployment

1. **Azure Subscription**: [Create a free account](https://azure.microsoft.com/en-us/free/)
2. **GitHub Account**: For CI/CD automation
3. **Azure CLI**: [Install instructions](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
4. **Azure Developer CLI (azd)**: [Install instructions](https://github.com/Azure/azure-dev)

### Step 1: Configure GitHub Secrets

To enable automatic deployment via GitHub Actions, configure these secrets:

**Azure Authentication:**
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`

**AI Services:**
- `AZURE_OPENAI_API_KEY`
- `AZURE_OPENAI_MODEL_ID`
- `AZURE_OPENAI_ENDPOINT`

For detailed setup instructions, see [GitHub-Secrets-Setup.md](./docs/GitHub-Secrets-Setup.md)

### Step 2: Commit and Push

```bash
git add .
git commit -m "Initial commit: AI Hub with GitHub Actions CI/CD"
git push origin main
```

### Step 3: Monitor Deployment

GitHub Actions will automatically:
1. ✅ Build backend and frontend
2. ✅ Run architecture tests
3. ✅ Validate Azure environment prerequisites
4. ✅ Deploy to Azure Container Apps using `azd up`
5. ✅ Inject secrets into Container Apps environment

Monitor progress in: **GitHub Actions → Deploy to Azure Container Apps**

---

## 🔄 CI/CD Pipeline

The project includes a fully automated GitHub Actions workflow:

**Workflow file**: `.github/workflows/deploy.yml`

### What the Pipeline Does

1. **Validation Job** (runs on every push to main):
   - Builds .NET solution
   - Runs architecture tests
   - Builds React frontend
   - Validates all code quality checks

2. **Deployment Job** (runs after validation succeeds):
   - Authenticates to Azure using OIDC
   - Validates Azure infrastructure prerequisites
   - Runs `azd up` to deploy all containers
   - Injects Azure OpenAI secrets from GitHub Secrets
   - Reports deployment status

### Triggering Deployment

**Automatic**: Push code to `main` branch
```bash
git push origin main
```

**Manual**: Trigger via GitHub Actions UI
- Go to **Actions → Deploy to Azure Container Apps → Run workflow → main**

For comprehensive CI/CD documentation, see [CI-CD-GitHub-Actions.md](./docs/CI-CD-GitHub-Actions.md)

---

## 📋 Pre-deployment Environment Validation

Before deploying to Azure, validate that required resources exist:

```bash
# Bash
bash scripts/validate-azure-env.sh <SUBSCRIPTION_ID> aihub-rg

# PowerShell
pwsh scripts/validate-azure-env.ps1 -SubscriptionId <SUBSCRIPTION_ID> -ResourceGroup aihub-rg
```

This script checks for:
- ✅ Resource group
- ✅ SQL Server and database
- ✅ Container Registry
- ✅ Container Apps Environment

