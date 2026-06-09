
# Database Migrations Guide

## Overview
The application includes automatic database migrations that run on application startup, ensuring the database schema is always up-to-date.

## Migration Architecture

### **Tjb.Migrations Project**
**Purpose**: Dedicated assembly for database migration operations. EF Core's `MigrationsAssembly` is set to this project, so all migration files live here (not in `Tjb.Data`).
**Location**: [`src/Tjb.Migrations`](src/Tjb.Migrations)

**Components**:
- ✅ **DbContext Factory**: `IDesignTimeDbContextFactory` in [`src/Tjb.Migrations`](src/Tjb.Migrations) - Design-time context creation (resolves a connection string from this project's `appsettings.json`)
- ✅ **Migration Program**: [`Program.cs`](src/Tjb.Migrations/Program.cs) - Standalone migration runner + seed
- ✅ **Configuration**: [`appsettings.json`](src/Tjb.Migrations/appsettings.json) - Connection strings

### **App Integration**
Both `Tjb.Web` (the deployed app) and `Tjb.Api` reference `Tjb.Migrations` and apply migrations on startup (`EnsureCreatedAsync()` then `MigrateAsync()`). Startup migration exceptions are intentionally swallowed so the app still boots if a migration fails.

**Components**:
- ✅ **Startup Integration**: [`src/Tjb.Web/Program.cs`](src/Tjb.Web/Program.cs) - Automatic migration on startup (the live application)

## How Automatic Migrations Work

### **Startup Process**
1. **Application Starts**: The containerized app (or local development server) starts
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
**File**: `src/Tjb.Migrations/20251007185200_InitialCreate.cs`

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
# Run migrations locally (standalone runner + seed)
dotnet run --project src/Tjb.Migrations

# Or let the Web app handle it automatically on startup
dotnet run --project src/Tjb.Web
```

### **Production (AWS)**
- ✅ **Automatic**: Migrations run on container/application startup
- ✅ **Safe**: Error handling prevents application failure
- ✅ **Logged**: All migration activity logged to CloudWatch

### **Manual Migration Management**
```bash
# Add new migration (migrations live in Tjb.Migrations)
dotnet ef migrations add NewFeature --project src/Tjb.Migrations --startup-project src/Tjb.Migrations

# Remove last migration
dotnet ef migrations remove --project src/Tjb.Migrations --startup-project src/Tjb.Migrations

# Generate SQL script
dotnet ef migrations script --project src/Tjb.Migrations --startup-project src/Tjb.Migrations
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
- ✅ **Security Groups**: Network access restricted to the ECS Fargate tasks

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
- **Solution**: Run migrations separately via `dotnet run --project src/Tjb.Migrations`

**2. Connection Issues**
- **Cause**: Database not accessible from the container/task
- **Solution**: Check VPC configuration and security groups

**3. Permission Issues**
- **Cause**: Database user lacks migration permissions
- **Solution**: Verify database user has CREATE/ALTER permissions

### **Debug Commands**
```bash
# Check migration status
dotnet ef migrations list --project src/Tjb.Migrations --startup-project src/Tjb.Migrations

# Validate migrations
dotnet ef database update --dry-run --project src/Tjb.Migrations --startup-project src/Tjb.Migrations

# Generate SQL script for review