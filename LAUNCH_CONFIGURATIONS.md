# Launch Configurations for TaskManager.Web2

This document explains how to use the VS Code launch configurations to run the TaskManager.Web2 project locally with different API endpoints.

## Available Launch Configurations

### 1. Launch TaskManager.Web2 (Local API)
- **Purpose**: Run the web app locally and connect to the local API running on `http://localhost:5001`
- **Environment Variables**:
  - `ASPNETCORE_URLS`: `http://localhost:5000`
  - `Api__BaseUrl`: `http://localhost:5001`
- **Use Case**: Development with local API

### 2. Launch TaskManager.Web2 (AWS API)
- **Purpose**: Run the web app locally and connect to the deployed AWS API Gateway
- **Environment Variables**:
  - `ASPNETCORE_URLS`: `http://localhost:5000`
  - `Api__BaseUrl`: `https://0eclslvxej.execute-api.us-east-1.amazonaws.com/Prod/`
- **Use Case**: Development with production API

## How to Use

1. Open VS Code in the project root
2. Go to Run and Debug (Ctrl+Shift+D)
3. Select the desired launch configuration from the dropdown
4. Click the green play button or press F5

The web app will start on `http://localhost:5000` and automatically open in your default browser.

## Configuration Details

- **Build Task**: The launch configurations use a pre-launch task called "build" that compiles the TaskManager.Web2 project
- **API Endpoint**: The API endpoint is configured via the `Api__BaseUrl` environment variable, which overrides the `Api.BaseUrl` setting in appsettings.json
- **Database**: The web app uses its own Identity database for user authentication

## Prerequisites

- .NET 8.0 SDK installed
- MySQL server running locally with:
  - Username: `root`
  - Password: `password`
  - Databases: `TaskManagerDb` (for API) and `TaskManagerIdentityDb` (for web app)
- The API must be running if using the "Local API" configuration
- For AWS API configuration, ensure the API Gateway endpoint is accessible

## Notes

- The web app currently handles authentication but doesn't make API calls yet
- JWT tokens are generated for authenticated users via `/api/auth/token`
- The API endpoints require valid JWT tokens for access