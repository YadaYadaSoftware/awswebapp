# Hybrid Deployment Guide - CloudFormation + SAM

## Overview
The TaskManager application now uses a **hybrid deployment approach** that separates infrastructure management from application deployment for better maintainability and Lambda Annotations compatibility.

## Architecture Split

### **Infrastructure Stack** (CloudFormation)
**File**: [`infrastructure/cloudformation.yaml`](infrastructure/cloudformation.yaml)
**Stack Name**: `taskmanager-infrastructure-{environment}`

**Manages**:
- ✅ VPC with public/private subnets
- ✅ RDS PostgreSQL database
- ✅ Security Groups
- ✅ NAT Gateway
- ✅ Secrets Manager
- ✅ IAM Roles for Lambda
- ✅ CloudWatch monitoring (database)

### **Application Stack** (SAM)
**File**: [`src/TaskManager.Api/serverless.template`](src/TaskManager.Api/serverless.template)
**Stack Name**: `taskmanager-api-{environment}`

**Manages**:
- ✅ Lambda functions
- ✅ API Gateway
- ✅ Lambda-specific CloudWatch logs
- ✅ Function-level monitoring

## Deployment Flow

### **1. Infrastructure Deployment**
```bash
aws cloudformation deploy \
  --template-file infrastructure/cloudformation.yaml \
  --stack-name taskmanager-infrastructure-prod \
  --parameter-overrides \
    Environment=prod \
    DatabasePassword=your-secure-password \
  --capabilities CAPABILITY_IAM
```

**Creates**: VPC, RDS, Security Groups, IAM Roles
**Exports**: VPC ID, Subnet IDs, Security Group IDs, IAM Role ARN

### **2. Application Deployment**
```bash
cd src/TaskManager.Api
sam deploy \
  --template-file serverless.template \
  --stack-name taskmanager-api-prod \
  --parameter-overrides \
    Environment=prod \
    InfrastructureStackName=taskmanager-infrastructure-prod \
    GoogleClientId=your-client-id \
    GoogleClientSecret=your-client-secret \
  --capabilities CAPABILITY_IAM \
  --resolve-s3
```

**Creates**: Lambda function, API Gateway
**References**: Infrastructure exports via `Fn::ImportValue`

## GitHub Actions Workflow

### **Updated Pipeline** ([`.github/workflows/dotnet.yml`](.github/workflows/dotnet.yml))

**Job 1: Build & Test**
- Compiles .NET application
- Runs tests
- Creates Lambda deployment package

**Job 2: Deploy Infrastructure**
- Deploys CloudFormation template
- Creates VPC, RDS, Security Groups
- Exports values for SAM template

**Job 3: Deploy Application**
- Uses SAM CLI to deploy Lambda functions
- References infrastructure via stack exports
- Creates API Gateway with Lambda integration

## Benefits of Hybrid Approach

### **Infrastructure Benefits (CloudFormation)**
- ✅ **Complete VPC Control**: Custom networking, security groups
- ✅ **RDS Management**: Database with proper security and backups
- ✅ **Enterprise Features**: NAT Gateway, Secrets Manager, monitoring
- ✅ **Stability**: Infrastructure changes less frequently

### **Application Benefits (SAM)**
- ✅ **Lambda Annotations Support**: Future compatibility with annotations
- ✅ **Simplified Lambda Deployment**: SAM handles packaging and deployment
- ✅ **API Gateway Integration**: Automatic API creation from Lambda events
- ✅ **Development Tools**: SAM local testing capabilities

## Stack Dependencies

### **Infrastructure Exports**
The infrastructure stack exports these values for the application stack:

```yaml
Exports:
  - VPCId: !Ref TaskManagerVPC
  - PrivateSubnets: !Sub '${PrivateSubnet1},${PrivateSubnet2}'
  - LambdaSecurityGroup: !Ref LambdaSecurityGroup
  - LambdaExecutionRole: !GetAtt LambdaExecutionRole.Arn
  - DatabaseSecret: !Ref DatabaseSecret
```

### **Application Imports**
The SAM template imports these values:

```json
{
  "Fn::ImportValue": {
    "Fn::Sub": "${InfrastructureStackName}-VPC"
  }
}
```

## Local Development

### **SAM Local Testing**
```bash
# Start local API Gateway and Lambda
cd src/TaskManager.Api
sam local start-api --template serverless.template

# Test specific function
sam local invoke TaskManagerApiFunction --event events/test-event.json
```

### **Infrastructure Testing**
```bash
# Validate CloudFormation template
aws cloudformation validate-template \
  --template-body file://infrastructure/cloudformation.yaml

# Test deployment (dry run)
aws cloudformation create-change-set \
  --template-body file://infrastructure/cloudformation.yaml \
  --stack-name test-stack \
  --change-set-name test-changes
```

## Required GitHub Secrets

### **Same Secrets as Before**
```
AWS_ACCESS_KEY_ID=your-aws-access-key-id
AWS_SECRET_ACCESS_KEY=your-aws-secret-access-key
DATABASE_PASSWORD=your-secure-database-password
GOOGLE_CLIENT_ID=your-google-oauth-client-id
GOOGLE_CLIENT_SECRET=your-google-oauth-client-secret
```

## Deployment Commands

### **Manual Deployment**
```bash
# 1. Deploy infrastructure
aws cloudformation deploy \
  --template-file infrastructure/cloudformation.yaml \
  --stack-name taskmanager-infrastructure-prod \
  --parameter-overrides Environment=prod DatabasePassword=YourPassword \
  --capabilities CAPABILITY_IAM

# 2. Build and package application
dotnet publish src/TaskManager.Api/TaskManager.Api.csproj \
  --configuration Release \
  --output ./publish/api \
  --runtime linux-x64

# 3. Deploy application with SAM
cd src/TaskManager.Api
sam deploy \
  --template-file serverless.template \
  --stack-name taskmanager-api-prod \
  --parameter-overrides \
    Environment=prod \
    InfrastructureStackName=taskmanager-infrastructure-prod \
    GoogleClientId=your-client-id \
    GoogleClientSecret=your-client-secret \
  --capabilities CAPABILITY_IAM \
  --resolve-s3
```

## Monitoring and Management

### **Infrastructure Monitoring**
- CloudWatch alarms for RDS CPU
- VPC Flow Logs (optional)
- Database performance insights

### **Application Monitoring**
- Lambda function metrics
- API Gateway metrics
- Application-specific CloudWatch logs

## Troubleshooting

### **Common Issues**

**1. Stack Export Not Found**
```
Error: Export taskmanager-infrastructure-prod-VPC cannot be imported
```
**Solution**: Ensure infrastructure stack is deployed first

**2. SAM Deployment Fails**
```
Error: Unable to upload artifact
```
**Solution**: Ensure AWS credentials have S3 access for SAM bucket

**3. Lambda VPC Timeout**
```
Error: Task timed out after 30.00 seconds
```
**Solution**: Check NAT Gateway and route table configuration

## Cost Implications

### **Infrastructure Stack** (~$65/month)
- RDS db.t3.micro: ~$15-20
- NAT Gateway: ~$45
- VPC: Free

### **Application Stack** (~$5/month)
- Lambda: Pay per request
- API Gateway: Pay per request
- CloudWatch Logs: Minimal cost

**Total**: Similar cost to unified approach but better separation of concerns.

This hybrid approach provides the best balance of infrastructure control and application deployment flexibility!