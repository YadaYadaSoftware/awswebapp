
# Database Migrations Guide

## Overview
The TaskManager application now includes automatic database migrations that run on API startup, ensuring the database schema is always up-to-date.

## Migration Architecture

### **TaskManager.Migrations Project**
**Purpose**: Dedicated assembly for database migration design-time operations
**Location**: [`src/TaskManager.Migrations`](src/TaskManager.Migrations)

**Components**:
- ✅ **DbContext Factory**: [`TaskManagerDbContextFactory.cs`](src/TaskManager.Migrations/TaskManagerDbContextFactory.cs) - Design-time context creation
- ✅ **Migration Program**: [`Program.cs`](src/TaskManager.Migrations/Program.cs) - Design-time operations only (does not apply migrations)
- ✅ **Configuration**: [`appsettings.json`](src/TaskManager.Migrations/appsettings.json) - Connection strings

### **API Integration**
**Location**: [`src/TaskManager.Api/Services`](src/TaskManager.Api/Services)

**Components**:
- ✅ **Interface**: [`IDatabaseMigrationService.cs`](src/TaskManager.Api/Services/IDatabaseMigrationService.cs)
- ✅ **Implementation**: [`DatabaseMigrationService.cs`](src/TaskManager.Api/Services/DatabaseMigrationService.cs)
- ✅ **Startup Integration**: [`Program.cs`](src/TaskManager.Api/Program.cs) - Automatic migration on startup

## How Automatic Migrations Work

### **API Startup Process**
1. **Application Starts**: Lambda function or local development server starts
2. **Migration Check**: `DatabaseMigrationService.MigrateAsync()` checks for pending migrations
3. **Apply Migrations**: Any pending migrations are applied automatically
4. **Seed Data**: Initial data is seeded if database is empty
5. **Application Ready**: API becomes available for requests

### **Migration Service Features**
```csharp
public async Task MigrateAsync()
{
    // Check for pending migrations
    var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();
    
    if (pendingMigrations.Any())
    {
        // Apply migrations
        await _context.Database.MigrateAsync();
        
        // Seed initial data
        await SeedDataAsync();
    }
}
```

## Generated Migration

### **Initial Migration Created**
**File**: `src/TaskManager.Data/Migrations/20250903095337_InitialCreate.cs`

**Creates Tables**:
- ✅ **Users**: Email, names, Google OAuth integration
- ✅ **Projects**: Name, description, ownership
- ✅ **Tasks**: Title, description, status, priority, assignments
- ✅ **ProjectMembers**: Many-to-many relationship with roles

**Includes**:
- ✅ **Primary Keys**: GUID-based identifiers
- ✅ **Foreign Keys**: Proper relationships between entities
- ✅ **Indexes**: Optimized for performance (email, project ownership, task status)
- ✅ **Constraints**: Unique constraints, required fields

## Seed Data

### **Initial Data Created**
**Sample User**:
- Email: `admin@taskmanager.com`
- Name: Admin User
- Ready for Google OAuth linking

**Welcome Project**:
- Name: "Welcome Project"
- Description: Introduction project for new users

**Sample Tasks**:
1. **Welcome Task**: High priority, due in 7 days
2. **Explore Application**: Medium priority, in progress
3. **Create Real Project**: Low priority, unassigned

## Usage Scenarios

### **Development**
```bash
# Start the API - migrations are applied automatically on startup
dotnet run --project src/TaskManager.Api

# Or start the Web application - migrations are applied automatically on startup
dotnet run --project src/TaskManager.Web2
```

### **Production (AWS)**
- ✅ **Automatic**: Migrations run on Lambda cold start
- ✅ **Safe**: Error handling prevents application failure
- ✅ **Logged**: All migration activity logged to CloudWatch

### **Migration Management**
```bash
# Add new migration
dotnet ef migrations add NewFeature --project src/TaskManager.Data --startup-project src/TaskManager.Migrations

# Remove last migration
dotnet ef migrations remove --project src/TaskManager.Data --startup-project src/TaskManager.Migrations

# Generate SQL script
dotnet ef migrations script --project src/TaskManager.Data --startup-project src/TaskManager.Migrations
```

## Benefits

### **Automatic Deployment**
- ✅ **Zero Downtime**: Migrations apply during application startup
- ✅ **Consistent State**: Database always matches application code
- ✅ **No Manual Steps**: Deployment pipeline handles everything
- ✅ **Error Resilience**: Application starts even if migrations fail

### **Development Workflow**
- ✅ **Local Development**: Same migration process as production
- ✅ **Team Collaboration**: Migrations in source control
- ✅ **Version Control**: Database schema changes tracked
- ✅ **Rollback Support**: EF Core migration rollback capabilities

## Security Considerations

### **Production Safety**
- ✅ **Error Handling**: Migration failures don't crash the application
- ✅ **Logging**: All migration activity logged for monitoring
- ✅ **Idempotent**: Safe to run multiple times
- ✅ **Backup**: RDS automated backups protect against issues

### **Connection Security**
- ✅ **Secrets Manager**: Database credentials stored securely
- ✅ **VPC Isolation**: Database in private subnets
- ✅ **Security Groups**: Network access restricted to Lambda

## Monitoring

### **CloudWatch Integration**
- ✅ **Migration Logs**: Detailed logging of migration process
- ✅ **Error Tracking**: Failed migrations logged with stack traces
- ✅ **Performance**: Migration timing and performance metrics
- ✅ **Alerts**: Can set up alarms for migration failures

## Troubleshooting

### **Common Issues**

**1. Migration Timeout**
- **Cause**: Large migrations taking too long
- **Solution**: Increase Lambda timeout or run migrations separately

**2. Connection Issues**
- **Cause**: Database not accessible from Lambda
- **Solution**: Check VPC configuration and security groups

**3. Permission Issues**
- **Cause**: Database user lacks migration permissions
- **Solution**: Verify database user has CREATE/ALTER permissions

### **Debug Commands**
```bash
# Check migration status
dotnet ef migrations list --project src/TaskManager.Data --startup-project src/TaskManager.Migrations

# Generate SQL script for review
dotnet ef migrations script --project src/TaskManager.Data --startup-project src/TaskManager.Migrations
```