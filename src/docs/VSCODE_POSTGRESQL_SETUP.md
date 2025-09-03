# VS Code PostgreSQL Extension Setup Guide

## Overview
This guide shows you how to use the PostgreSQL extension in VS Code to connect to your private TaskManager database through a bastion host.

## Prerequisites

### **1. Install VS Code PostgreSQL Extension**
- **Extension Name**: `PostgreSQL` by Chris Kolkman
- **Install**: VS Code → Extensions → Search "PostgreSQL" → Install
- **Alternative**: `SQLTools` with PostgreSQL driver (more features)

### **2. Required Information**
You'll need these values from your AWS deployment:
- Bastion host public IP
- RDS Proxy endpoint (or direct RDS endpoint)
- Database credentials from Secrets Manager
- SSH key pair for bastion access

## Step-by-Step Setup

### **Step 1: Get Database Information**
```bash
# Get RDS Proxy endpoint
PROXY_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`RDSProxyEndpoint`].OutputValue' \
  --output text)

# Get database credentials
aws secretsmanager get-secret-value \
  --secret-id taskmanager/database/main \
  --query 'SecretString' \
  --output text | jq .

# Get bastion host IP (you'll need to create this first)
aws ec2 describe-instances \
  --filters "Name=tag:Name,Values=TaskManager-Bastion-main" \
  --query 'Reservations[0].Instances[0].PublicIpAddress' \
  --output text
```

### **Step 2: Create SSH Tunnel**
```bash
# Create SSH tunnel (keep this running)
ssh -i /path/to/your-key.pem -L 5432:your-proxy-endpoint:5432 ec2-user@your-bastion-ip

# Example:
# ssh -i ~/.ssh/taskmanager-key.pem -L 5432:taskmanager-proxy-main.proxy-xyz.us-east-1.rds.amazonaws.com:5432 ec2-user@3.80.123.45
```

### **Step 3: Configure VS Code PostgreSQL Extension**

#### **Method 1: PostgreSQL Extension**
1. **Open Command Palette**: `Ctrl+Shift+P` (Windows) or `Cmd+Shift+P` (Mac)
2. **Run Command**: `PostgreSQL: New Query`
3. **Configure Connection**:
   ```
   Host: localhost
   Port: 5432
   Database: taskmanager
   Username: taskmanager_admin
   Password: [from Secrets Manager]
   ```

#### **Method 2: SQLTools Extension (Recommended)**
1. **Install SQLTools**: `SQLTools` + `SQLTools PostgreSQL/Cockroach Driver`
2. **Create Connection**:
   - **Connection Name**: `TaskManager AWS Database`
   - **Server**: `localhost`
   - **Port**: `5432`
   - **Database**: `taskmanager`
   - **Username**: `taskmanager_admin`
   - **Password**: `[from Secrets Manager]`
   - **SSL**: Disabled (tunnel is encrypted)

### **Step 4: Test Connection**
1. **Ensure SSH tunnel is running**
2. **Connect in VS Code**: Click connection in SQLTools sidebar
3. **Run Test Query**: `SELECT version();`
4. **Explore Tables**: Browse Users, Projects, Tasks, ProjectMembers

## VS Code Configuration

### **SQLTools Settings (Recommended)**
**Create**: `.vscode/settings.json` in your project
```json
{
  "sqltools.connections": [
    {
      "name": "TaskManager AWS Database",
      "driver": "PostgreSQL",
      "server": "localhost",
      "port": 5432,
      "database": "taskmanager",
      "username": "taskmanager_admin",
      "password": "",
      "askForPassword": true,
      "connectionTimeout": 30
    }
  ]
}
```

### **Workspace Configuration**
**Benefits**:
- ✅ **Team Sharing**: Connection settings shared with team
- ✅ **Quick Access**: One-click database connection
- ✅ **Query History**: Saved queries and history
- ✅ **Schema Browser**: Visual table and column explorer

## Usage Examples

### **Common Queries**
```sql
-- Check migration status
SELECT * FROM "__EFMigrationsHistory";

-- View all users
SELECT id, email, first_name, last_name, created_at FROM "Users";

-- View projects and their owners
SELECT p.name, p.description, u.email as owner_email 
FROM "Projects" p 
JOIN "Users" u ON p.owner_id = u.id;

-- View tasks with assignments
SELECT t.title, t.status, t.priority, u.email as assigned_to
FROM "Tasks" t
LEFT JOIN "Users" u ON t.assigned_to_id = u.id;

-- Check project memberships
SELECT p.name as project, u.email as member, pm.role
FROM "ProjectMembers" pm
JOIN "Projects" p ON pm.project_id = p.id
JOIN "Users" u ON pm.user_id = u.id;
```

### **Database Administration**
```sql
-- Check database size
SELECT pg_size_pretty(pg_database_size('taskmanager'));

-- View table sizes
SELECT 
  schemaname,
  tablename,
  pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
FROM pg_tables 
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Check active connections
SELECT count(*) as active_connections FROM pg_stat_activity;
```

## Troubleshooting

### **Common Issues**

**1. Connection Refused**
- **Check**: SSH tunnel is running
- **Verify**: Bastion host is accessible
- **Test**: `telnet localhost 5432`

**2. Authentication Failed**
- **Check**: Password from Secrets Manager
- **Verify**: Username is `taskmanager_admin`
- **Test**: Direct psql connection first

**3. SSH Tunnel Issues**
- **Check**: SSH key permissions (`chmod 600 your-key.pem`)
- **Verify**: Bastion security group allows SSH (port 22)
- **Test**: Direct SSH to bastion first

### **Debug Commands**
```bash
# Test SSH connection to bastion
ssh -i your-key.pem ec2-user@your-bastion-ip

# Test tunnel is working
netstat -an | grep 5432

# Test database connectivity from bastion
psql -h your-proxy-endpoint -U taskmanager_admin -d taskmanager
```

## Automation Scripts

### **Quick Connect Script**
```bash
#!/bin/bash
# connect-to-database.sh

# Get endpoints from CloudFormation
PROXY_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`RDSProxyEndpoint`].OutputValue' \
  --output text)

BASTION_IP=$(aws ec2 describe-instances \
  --filters "Name=tag:Name,Values=TaskManager-Bastion-main" \
  --query 'Reservations[0].Instances[0].PublicIpAddress' \
  --output text)

echo "Creating SSH tunnel to database..."
echo "Proxy endpoint: $PROXY_ENDPOINT"
echo "Bastion IP: $BASTION_IP"

# Create tunnel (replace with your key path)
ssh -i ~/.ssh/taskmanager-key.pem -L 5432:$PROXY_ENDPOINT:5432 ec2-user@$BASTION_IP
```

### **VS Code Task Configuration**
**Create**: `.vscode/tasks.json`
```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Connect to TaskManager Database",
      "type": "shell",
      "command": "./connect-to-database.sh",
      "group": "build",
      "presentation": {
        "echo": true,
        "reveal": "always",
        "focus": false,
        "panel": "new"
      }
    }
  ]
}
```

## Benefits of VS Code + PostgreSQL Extension

### **Development Workflow**
- ✅ **Integrated Environment**: Database queries in same IDE as code
- ✅ **IntelliSense**: SQL autocomplete and syntax highlighting
- ✅ **Query Results**: Formatted results with export options
- ✅ **Schema Explorer**: Visual database structure browser
- ✅ **Query History**: Saved queries and execution history

### **Team Collaboration**
- ✅ **Shared Settings**: Connection configurations in source control
- ✅ **Query Sharing**: SQL files in project repository
- ✅ **Documentation**: Database queries as part of project docs

**Yes, the PostgreSQL extension in VS Code works perfectly with the bastion host approach! You'll have full database access and administration capabilities directly in your development environment.**