# RDS Proxy + Bastion Host Access Guide

## Important Clarification

### **RDS Proxy Limitations**
❌ **RDS Proxy does NOT provide direct external access** - it's still within the private VPC
✅ **RDS Proxy provides**: Connection pooling, automatic failover, IAM authentication, better security

### **Complete Access Solution**
**RDS Proxy + Bastion Host** = Secure, efficient database access from your desktop

## RDS Proxy Benefits

### **Why We Added RDS Proxy**
- ✅ **Connection Pooling**: Reduces database connections from Lambda
- ✅ **Automatic Failover**: Better reliability for database connections
- ✅ **IAM Authentication**: Can use IAM roles instead of passwords
- ✅ **Connection Management**: Handles connection lifecycle automatically
- ✅ **Security**: Additional layer between application and database

### **Lambda Benefits**
- ✅ **Reduced Cold Starts**: Proxy maintains connection pools
- ✅ **Better Performance**: Faster database connections
- ✅ **Scalability**: Handles many concurrent Lambda executions

## Access from Desktop Environment

### **Step 1: Create Bastion Host**

#### **Manual Creation (AWS Console)**
1. **Go to EC2 Console** → Launch Instance
2. **Choose AMI**: Amazon Linux 2023
3. **Instance Type**: t3.micro (free tier)
4. **Network**: 
   - VPC: TaskManager VPC
   - Subnet: Public Subnet 1
   - Auto-assign Public IP: Enable
5. **Security Group**: Create new with SSH access
6. **Key Pair**: Create or use existing key pair

#### **Automated Creation (CloudFormation)**
```bash
# Add to template.yaml (optional)
BastionHost:
  Type: AWS::EC2::Instance
  Properties:
    ImageId: ami-0c02fb55956c7d316  # Amazon Linux 2023
    InstanceType: t3.micro
    KeyName: your-key-pair-name
    SubnetId: !Ref PublicSubnet1
    SecurityGroupIds:
      - !Ref BastionSecurityGroup
    Tags:
      - Key: Name
        Value: !Sub 'TaskManager-Bastion-${Environment}'

BastionSecurityGroup:
  Type: AWS::EC2::SecurityGroup
  Properties:
    GroupDescription: Security group for bastion host
    VpcId: !Ref TaskManagerVPC
    SecurityGroupIngress:
      - IpProtocol: tcp
        FromPort: 22
        ToPort: 22
        CidrIp: 0.0.0.0/0  # Restrict to your IP for security
```

### **Step 2: Connect to Database via Bastion**

#### **SSH Tunnel to RDS Proxy**
```bash
# Get RDS Proxy endpoint from CloudFormation outputs
PROXY_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`RDSProxyEndpoint`].OutputValue' \
  --output text)

echo "RDS Proxy endpoint: $PROXY_ENDPOINT"

# Create SSH tunnel through bastion host
ssh -i your-key.pem -L 5432:$PROXY_ENDPOINT:5432 ec2-user@your-bastion-public-ip

# In another terminal, connect to database
psql -h localhost -p 5432 -U taskmanager_admin -d taskmanager
```

#### **Alternative: Direct RDS Connection**
```bash
# Get direct RDS endpoint
DB_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`DatabaseEndpoint`].OutputValue' \
  --output text)

# SSH tunnel to direct RDS
ssh -i your-key.pem -L 5432:$DB_ENDPOINT:5432 ec2-user@your-bastion-public-ip

# Connect to database
psql -h localhost -p 5432 -U taskmanager_admin -d taskmanager
```

## Database Connection Instructions

### **Get Database Credentials**
```bash
# Get credentials from Secrets Manager
aws secretsmanager get-secret-value \
  --secret-id taskmanager/database/main \
  --query 'SecretString' \
  --output text | jq .

# Example output:
# {
#   "username": "taskmanager_admin",
#   "password": "your-password",
#   "engine": "postgres",
#   "host": "proxy-endpoint-or-rds-endpoint",
#   "port": 5432,
#   "dbname": "taskmanager"
# }
```

### **Connection Steps**
1. **Start SSH Tunnel**: Use command above with your bastion host IP
2. **Get Password**: From Secrets Manager or your GitHub secrets
3. **Connect**: Use any PostgreSQL client to `localhost:5432`

### **Database Client Configuration**
**pgAdmin**:
- Host: `localhost`
- Port: `5432`
- Database: `taskmanager`
- Username: `taskmanager_admin`
- Password: From Secrets Manager

**DBeaver**:
- Connection Type: PostgreSQL
- Host: `localhost`
- Port: `5432`
- Database: `taskmanager`
- Username: `taskmanager_admin`
- Password: From Secrets Manager

**VS Code PostgreSQL Extension**:
- Host: `localhost`
- Port: `5432`
- Database: `taskmanager`
- Username: `taskmanager_admin`
- Password: From Secrets Manager

## Benefits of This Setup

### **Security**
- ✅ **Private Database**: Never exposed to internet
- ✅ **RDS Proxy**: Additional security layer
- ✅ **Bastion Host**: Controlled access point
- ✅ **SSH Tunnel**: Encrypted connection

### **Performance**
- ✅ **Connection Pooling**: RDS Proxy manages connections efficiently
- ✅ **Reduced Latency**: Proxy optimizes database connections
- ✅ **Lambda Optimization**: Better cold start performance

### **Management**
- ✅ **Easy Access**: Simple SSH tunnel for development
- ✅ **Monitoring**: RDS Proxy provides additional metrics
- ✅ **Scalability**: Handles multiple concurrent connections

## Quick Setup Commands

### **Complete Setup Script**
```bash
#!/bin/bash
# setup-database-access.sh

# Get outputs from CloudFormation
STACK_NAME="taskmanager-main"

# Get RDS Proxy endpoint
PROXY_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name $STACK_NAME \
  --query 'Stacks[0].Outputs[?OutputKey==`RDSProxyEndpoint`].OutputValue' \
  --output text)

# Get database credentials
DB_CREDS=$(aws secretsmanager get-secret-value \
  --secret-id taskmanager/database/main \
  --query 'SecretString' \
  --output text)

echo "RDS Proxy Endpoint: $PROXY_ENDPOINT"
echo "Database Credentials: $DB_CREDS"
echo ""
echo "To connect:"
echo "1. Create SSH tunnel: ssh -i your-key.pem -L 5432:$PROXY_ENDPOINT:5432 ec2-user@your-bastion-ip"
echo "2. Connect to database: psql -h localhost -p 5432 -U taskmanager_admin -d taskmanager"
```

## Next Steps

### **Deploy RDS Proxy**
1. **Push changes** to main branch (RDS Proxy will be created)
2. **Create bastion host** (manual or add to template)
3. **Set up SSH tunnel** using commands above
4. **Connect and inspect** your database

### **Alternative: API Endpoints for Database Inspection**
Consider creating API endpoints for database inspection instead of direct access:
- `GET /api/admin/database/status`
- `GET /api/admin/database/tables`
- `GET /api/admin/database/users`

**RDS Proxy enhances your database architecture, but you'll still need a bastion host for direct desktop access to the private database.**