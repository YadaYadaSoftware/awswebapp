# Development Troubleshooting Guide

## Common Build Issues

### File Lock Errors During Build

**Problem**: Build fails with errors like:
```
error MSB3027: Could not copy "apphost.exe" to "TaskManager.Web.exe". 
The file is locked by: "TaskManager.Web (PID)"
```

**Cause**: A previous instance of the application is still running and locking the executable file.

**Solution**:

#### Option 1: Find and Kill Specific Process
```bash
# Find the running process
tasklist /FI "IMAGENAME eq TaskManager.Web.exe"

# Kill the specific process (replace PID with actual process ID)
taskkill /PID [PID] /F
```

#### Option 2: Kill All TaskManager Processes
```bash
# Kill all TaskManager processes
taskkill /IM "TaskManager.Web.exe" /F
taskkill /IM "TaskManager.Api.exe" /F
```

#### Option 3: Use PowerShell (Alternative)
```powershell
# Find and kill TaskManager processes
Get-Process -Name "TaskManager*" | Stop-Process -Force
```

### User Secrets Configuration Issues

**Problem**: `dotnet user-secrets` commands fail with:
```
Could not find the global property 'UserSecretsId' in MSBuild project
```

**Solution**: Ensure both project files have `UserSecretsId` configured:

```xml
<PropertyGroup>
  <UserSecretsId>your-unique-id-here</UserSecretsId>
</PropertyGroup>
```

**Current Configuration**:
- API Project: `taskmanager-api-secrets`
- Web Project: `taskmanager-web-secrets`

### Authentication State Issues

**Problem**: Blazor components fail with:
```
Authorization requires a cascading parameter of type Task<AuthenticationState>
```

**Solution**: Ensure `App.razor` wraps the Router with `CascadingAuthenticationState`:

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <!-- Router content -->
    </Router>
</CascadingAuthenticationState>
```

## Development Workflow Best Practices

### Before Building
1. **Check for running processes**: Use `tasklist` to verify no instances are running
2. **Clean solution**: Run `dotnet clean` if you encounter persistent issues
3. **Restore packages**: Run `dotnet restore` after adding new packages

### During Development
1. **Use single instance**: Only run one instance of each project at a time
2. **Proper shutdown**: Use Ctrl+C to properly stop running applications
3. **Monitor processes**: Keep Task Manager open to monitor running processes

### After Development Session
1. **Stop all processes**: Ensure all development servers are stopped
2. **Clean build artifacts**: Run `dotnet clean` to remove temporary files
3. **Commit changes**: Commit working code to avoid conflicts

## Useful Commands

### Process Management
```bash
# List all .NET processes
tasklist /FI "IMAGENAME eq dotnet.exe"

# List TaskManager processes
tasklist /FI "IMAGENAME eq TaskManager*"

# Kill all dotnet processes (use with caution)
taskkill /IM "dotnet.exe" /F
```

### Build Management
```bash
# Clean solution
dotnet clean

# Restore packages
dotnet restore

# Build solution
dotnet build

# Build specific project
dotnet build src/TaskManager.Api/TaskManager.Api.csproj
```

### User Secrets Management
```bash
# List secrets for a project
dotnet user-secrets list --project src/TaskManager.Api

# Set a secret
dotnet user-secrets set "key" "value" --project src/TaskManager.Api

# Remove a secret
dotnet user-secrets remove "key" --project src/TaskManager.Api

# Clear all secrets
dotnet user-secrets clear --project src/TaskManager.Api
```

## IDE-Specific Issues

### Visual Studio Code
- **Terminal conflicts**: Use separate terminals for different projects
- **File watchers**: Disable file watchers if they cause conflicts
- **Extensions**: Ensure C# extension is up to date

### Visual Studio
- **IIS Express**: Stop IIS Express processes from the system tray
- **Background builds**: Disable background compilation if it causes issues
- **Clean solution**: Use Build > Clean Solution regularly

## Prevention Tips

1. **Use different ports**: Configure different ports for API and Web projects
2. **Environment separation**: Use different environments (Development, Staging)
3. **Process monitoring**: Set up process monitoring in your development workflow
4. **Automated cleanup**: Create scripts to clean up processes before builds

This guide should help you avoid and resolve common development issues with the TaskManager application.