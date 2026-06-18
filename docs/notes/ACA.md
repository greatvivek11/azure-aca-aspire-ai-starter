# Technical Primer: Azure Container Apps, Dapr, and Bicep

This document provides a detailed, project-agnostic overview of the core technologies used in this repository: Azure Container Apps, Dapr, and Bicep, with a focus on their practical implementation on Azure.

---

## 1. Azure Container Apps (ACA)

Azure Container Apps is a fully managed, serverless container service for building and deploying modern applications and microservices. It abstracts away the complexity of managing the underlying Kubernetes cluster, allowing developers to focus on code.

### 1.1. Networking

Networking in ACA is configured at the *environment* level, which acts as a secure boundary for a group of container apps.

*   **VNet Integration**:
    *   **Default VNet**: By default, an environment is created with a public endpoint, accessible from the internet.
    *   **Custom VNet**: You can deploy an environment into your own existing Azure Virtual Network (VNet). This is crucial for:
        *   Securing outbound traffic with Azure Firewall or Network Security Groups (NSGs).
        *   Enabling private communication with other Azure resources (like databases and storage) via Private Endpoints.
        *   Requires a dedicated subnet (minimum `/23` for Consumption-only, `/27` for Workload profiles).

*   **Ingress (Inbound Traffic)**:
    *   **External**: The container app gets a public, internet-routable IP address.
    *   **Internal**: The container app is only accessible from within its VNet. This is the standard for backend services that should not be exposed publicly.
    *   **IP Restrictions**: You can whitelist specific IP address ranges to control who can access your app.

*   **Communication Between Apps**:
    *   Within the same environment, container apps can communicate with each other using their names as DNS hostnames (e.g., `http://my-backend-app`).
    *   For Dapr-enabled apps, service invocation provides a more robust and secure communication mechanism.

### 1.2. Security

*   **Authentication & Authorization (AuthN/AuthZ)**:
    *   ACA has built-in authentication features ("Easy Auth").
    *   You can configure identity providers like **Microsoft Entra ID**, GitHub, Google, etc., without changing your application code.
    *   It handles the OAuth 2.0 and OpenID Connect flows, validating tokens and managing user sessions.

*   **Managed Identity**:
    *   This is the recommended way for an app to securely access other Azure resources without storing credentials (like connection strings) in code.
    *   **System-Assigned**: An identity is created and tied to the lifecycle of the container app itself.
    *   **User-Assigned**: A standalone Azure resource that can be created once and assigned to multiple container apps. This is ideal for granting a common set of permissions to a group of services.
    *   **Use Case**: The container app's Managed Identity can be granted an RBAC role (e.g., `Storage Blob Data Reader` on a Storage Account, or `SQL DB Contributor` on a SQL Database) to access that service. The application code then uses the `DefaultAzureCredential` from the Azure Identity SDK to seamlessly acquire a token.

### 1.3. Scaling

ACA provides powerful and flexible autoscaling capabilities, primarily powered by KEDA (Kubernetes-based Event-driven Autoscaling).

*   **Scale Rules**: You can define rules to scale the number of replicas (container instances) based on:
    *   **HTTP Traffic**: Number of concurrent HTTP requests.
    *   **CPU/Memory Usage**: Percentage of CPU or memory utilization.
    *   **KEDA Scalers**: A rich set of scalers for various event sources, including:
        *   **Azure Queue Storage**: Number of messages in a queue.
        *   **Azure Service Bus**: Messages in a topic or queue.
        *   **Azure Cosmos DB**: Based on the change feed.

*   **Scale to Zero**: For many triggers (especially event-driven ones), ACA can scale the number of replicas down to zero when there is no traffic or work to be done, significantly reducing costs. The minimum number of replicas can be set to `1` or higher to avoid cold starts for latency-sensitive applications.

### 1.4. Pricing & Free Tier

Billing is based on two main factors: resource consumption and the number of HTTP requests.

*   **Resource Consumption**: Billed for the vCPU-seconds and GiB-seconds of memory allocated to your running container app replicas.
*   **HTTP Requests**: A charge per million requests.
*   **Free Tier**: Azure provides a generous monthly free grant for each subscription, which includes:
    *   First 180,000 vCPU-seconds
    *   First 360,000 GiB-seconds
    *   First 2 million HTTP requests
    This makes it very cost-effective for development, testing, and low-traffic applications.

### 1.5. Integration with Azure Services

ACA integrates seamlessly with other Azure services, typically using Managed Identity for secure, passwordless connections.

*   **Azure SQL & Cosmos DB**: The container app's Managed Identity is granted permissions on the database. The application uses the Azure Identity SDK to connect without needing a connection string with a password.
*   **Azure Blob Storage**: The same principle applies. The app's Managed Identity gets an RBAC role on the storage account (e.g., `Storage Blob Data Contributor`), and the app uses the SDK to interact with blobs.
*   **Persistent Storage**: While containers are ephemeral, ACA can mount Azure Files shares as persistent volumes, allowing stateful applications to store data that outlives a single container instance.

---

## 2. Dapr on Azure Container Apps

Dapr (Distributed Application Runtime) is a first-class citizen in ACA, providing a powerful set of building blocks for microservices. When you enable Dapr for a container app, Azure manages the Dapr sidecar for you.

*   **Dapr Components**: In ACA, Dapr components are defined as resources within the environment. This allows them to be shared across multiple apps or scoped to specific ones. The schema is simplified compared to open-source Dapr.
*   **Leveraging Azure Services**: Dapr's pluggable component model shines on Azure, where its building blocks can be backed by robust, managed Azure services.
    *   **State Management**: Can be backed by **Azure Blob Storage** for simple state, or **Azure Cosmos DB** for a high-performance, queryable state store.
    *   **Pub/Sub**: Can use **Azure Service Bus** or **Azure Event Hubs** as a reliable message broker for asynchronous communication.
    *   **Secrets Management**: Can integrate directly with **Azure Key Vault** to securely load application secrets.

---

## 3. Infrastructure as Code (IaC) with Bicep

Bicep is a declarative language for deploying Azure resources. It provides a cleaner, more readable syntax than ARM JSON templates.

*   **Declarative & Idempotent**: You define the *desired state* of your infrastructure. Running the same Bicep file multiple times results in the same state, making deployments predictable and repeatable.
*   **Modularity**: Bicep files can be broken down into modules, allowing you to create reusable components for common resources (like a standard storage account or a container app with specific settings).
*   **Full Azure Support**: Bicep supports all Azure resource types and API versions from day one, ensuring you can always use the latest Azure features. In this project, Bicep is used to define the Container Apps Environment, the container apps themselves, all data services (SQL, Blob, AI Search), the Managed Identities, and the RBAC role assignments that connect them all.