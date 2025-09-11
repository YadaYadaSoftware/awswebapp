# Database Access Guide - Private VPC Database

## Overview
The TaskManager RDS PostgreSQL database is deployed in private subnets for security. This guide explains how to access it from your development environment.

## Access Methods

### **Option 1: Bastion Host (Recommended for Development)**

#### **Create EC2 Bastion Host**
```bash
# Create a small EC2 instance in the public subnet
aws ec2 run-instances \
  --image-id ami-0c02fb55956c7d316 \
  --instance-type t3.micro \
  --key-name your-key-pair \
  --security-group-ids sg-your-bastion-sg \
  --subnet-id subnet-your-public-subnet \
  --associate-public-ip-address \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=TaskManager-Bastion}]'
```

#### **SSH Tunnel for Database Access**
```bash
# Create SSH tunnel to database
ssh -i your-key.pem -L 5432:your-rds-endpoint:5432 ec2-user@your-bastion-ip

# Connect with psql through tunnel
psql -h localhost -p 5432 -U taskmanager_admin -d taskmanager
```

### **Option 2: AWS Systems Manager Session Manager (Secure)**

#### **Setup Session Manager**
```bash
# Install Session Manager plugin
# https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html

# Connect to bastion via Session Manager
aws ssm start-session --target i-your-instance-id

# From bastion, connect to database
psql -h your-rds-endpoint -U taskmanager_admin -d taskmanager
```

### **Option 3: Lambda Function for Database Queries**

#### **Create Database Query Lambda**
```yaml
# Add to regional-infrastructure.yaml
DatabaseQueryFunction:
  Type: AWS::Serverless::Function
  Properties:
    FunctionName: !Sub 'TaskManager-DbQuery-${Environment}'
    InlineCode: |
      // Lambda function to execute database queries
      // Returns results as JSON
    Handler: index.handler
    Runtime: nodejs18.x
    VpcConfig:
      SecurityGroupIds:
        - !Ref LambdaSecurityGroup
      SubnetIds:
        - !Ref PrivateSubnet1
        - !Ref PrivateSubnet2
    Environment:
      Variables:
        DB_HOST: !GetAtt TaskManagerDatabase.Endpoint.Address
        DB_NAME: taskmanager
```

### **Option 4: RDS Proxy (Production Recommended)**

#### **Add RDS Proxy to Template**
```yaml
# Already included in regional-infrastructure.yaml
TaskManagerRDSProxy:
  Type: AWS::RDS::DBProxy
  Properties:
    DBProxyName: !Sub 'taskmanager-proxy-${Environment}'
    EngineFamily: POSTGRESQL
    Auth:
      - AuthScheme: SECRETS
        SecretArn: !Ref DatabaseSecret
    RoleArn: !GetAtt RDSProxyRole.Arn
    VpcSubnetIds:
      - !Ref PrivateSubnet1
      - !Ref PrivateSubnet2
    VpcSecurityGroupIds:
      - !Ref RDSSecurityGroup
```

## Quick Access Solutions

### **Temporary Public Access (Development Only)**

#### **⚠️ WARNING: Only for Development**
```bash
# Temporarily make RDS publicly accessible (NOT for production)
aws rds modify-db-instance \
  --db-instance-identifier taskmanager-db-main \
  --publicly-accessible \
  --apply-immediately

# Connect directly
psql -h your-rds-endpoint -U taskmanager_admin -d taskmanager

# IMPORTANT: Revert after use
aws rds modify-db-instance \
  --db-instance-identifier taskmanager-db-main \
  --no-publicly-accessible \
  --apply-immediately
```

### **Database Administration Tools**

#### **Using pgAdmin with SSH Tunnel**
1. **Setup SSH Tunnel**: Use bastion host method above
2. **Configure pgAdmin**:
   - Host: `localhost`
   - Port: `5432`
   - Database: `taskmanager`
   - Username: `taskmanager_admin`
   - Password: From your GitHub secrets

#### **Using DBeaver with SSH Tunnel**
1. **Create Connection**: New PostgreSQL connection
2. **SSH Tab**: Configure SSH tunnel through bastion
3. **Main Tab**: Database connection details

## Automated Database Inspection

### **Create Database Inspection Script**
```bash
#!/bin/bash
# inspect-database.sh

# Get database endpoint from CloudFormation
DB_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`DatabaseEndpoint`].OutputValue' \
  --output text)

echo "Database endpoint: $DB_ENDPOINT"

# Get database credentials from Secrets Manager
SECRET_ARN=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`DatabaseSecretArn`].OutputValue' \
  --output text)

DB_CREDS=$(aws secretsmanager get-secret-value \
  --secret-id $SECRET_ARN \
  --query 'SecretString' \
  --output text)

echo "Database credentials retrieved from Secrets Manager"
echo "Use these with your preferred database client through a bastion host"
```

## Development Workflow

### **Recommended Approach**
1. **Local Development**: Use local PostgreSQL for development
2. **Testing**: Use bastion host to inspect production data
3. **Debugging**: Use Lambda logs and application endpoints
4. **Administration**: Use RDS console for basic monitoring

### **Local Development Setup**
```bash
# Install PostgreSQL locally
# Windows: Download from postgresql.org
# macOS: brew install postgresql
# Linux: sudo apt-get install postgresql

# Create local development database
createdb taskmanager_dev

# Run migrations locally
dotnet run --project src/TaskManager.Migrations
```

## Security Best Practices

### **Access Control**
- ✅ **Bastion Host**: Minimal access, specific security groups
- ✅ **SSH Keys**: Use key-based authentication
- ✅ **Session Manager**: Preferred over direct SSH
- ✅ **Temporary Access**: Remove bastion when not needed

### **Network Security**
- ✅ **Private Subnets**: Database never exposed to internet
- ✅ **Security Groups**: Restrict access to specific sources
- ✅ **VPC Flow Logs**: Monitor network access
- ✅ **CloudTrail**: Audit all database access

## Monitoring and Logging

### **Database Monitoring**
- ✅ **RDS Performance Insights**: Query performance monitoring
- ✅ **CloudWatch Metrics**: CPU, connections, storage
- ✅ **Enhanced Monitoring**: OS-level metrics
- ✅ **Slow Query Logs**: Identify performance issues

### **Application Monitoring**
- ✅ **Lambda Logs**: Migration and application logs
- ✅ **API Gateway Logs**: Request/response logging
- ✅ **CloudWatch Alarms**: Error rate and performance alerts

## Quick Commands

### **Get Database Information**
```bash
# Get database endpoint
aws rds describe-db-instances \
  --db-instance-identifier taskmanager-db-main \
  --query 'DBInstances[0].Endpoint.Address' \
  --output text

# Get database credentials
aws secretsmanager get-secret-value \
  --secret-id taskmanager/database/main \
  --query 'SecretString' \
  --output text | jq .
```

### **Check Database Status**
```bash
# Check RDS instance status
aws rds describe-db-instances \
  --db-instance-identifier taskmanager-db-main \
  --query 'DBInstances[0].DBInstanceStatus' \
  --output text

# Check VPC and security groups
aws ec2 describe-security-groups \
  --group-names TaskManager-RDS-SG-main
```

**For development, the bastion host approach is most practical for database inspection and administration.**