# AWS Deployment Setup Guide

## Overview
This guide explains how to set up automated deployment of the TaskManager application to AWS using GitHub Actions.

## Prerequisites

### 1. AWS Account Setup
- AWS account with appropriate permissions
- AWS CLI installed and configured locally (for initial setup)
- IAM user with programmatic access

### 2. Required AWS Permissions
The IAM user needs the following permissions:
- CloudFormation (full access)
- Lambda (full access)
- API Gateway (full access)
- RDS (full access)
- VPC (full access)
- IAM (limited - for role creation)
- Secrets Manager (full access)
- CloudWatch (full access)

## GitHub Secrets Configuration

### Required Secrets
Configure these secrets in your GitHub repository (Settings → Secrets and variables → Actions):

#### AWS Credentials
```
AWS_ACCESS_KEY_ID=your-aws-access-key-id
AWS_SECRET_ACCESS_KEY=your-aws-secret-access-key
```

#### Database Configuration
```
DATABASE_PASSWORD=your-secure-database-password-here
```
**Requirements**: Minimum 8 characters, include letters and numbers

#### Google OAuth Credentials
```
GOOGLE_CLIENT_ID=your-google-oauth-client-id
GOOGLE_CLIENT_SECRET=your-google-oauth-client-secret
```

### How to Set GitHub Secrets
1. Go to your GitHub repository
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Add each secret with the exact name and value

## Deployment Process

### Automatic Deployment
The deployment happens automatically when you push to the `main` branch:

1. **Build Stage**: Compiles and tests the application
2. **Infrastructure Stage**: Deploys AWS resources using CloudFormation
3. **Application Stage**: Deploys the Lambda function with your code
4. **Web Stage**: Prepares web application deployment

### Manual Deployment
You can also trigger deployment manually:
1. Go to **Actions** tab in GitHub
2. Select **TaskManager CI/CD** workflow
3. Click **Run workflow**
4. Choose the branch and click **Run workflow**

## AWS Resources Created

### Infrastructure Components
- **VPC**: Custom VPC with public and private subnets
- **RDS PostgreSQL**: Database instance in private subnets
- **Lambda Function**: API hosting with VPC access
- **API Gateway**: HTTP API for routing requests
- **Secrets Manager**: Secure credential storage
- **CloudWatch**: Logging and monitoring
- **Security Groups**: Network access control

### Resource Naming Convention
All resources are named with the pattern: `TaskManager-{ResourceType}-{Environment}`

Example:
- VPC: `TaskManager-VPC-prod`
- Database: `taskmanager-db-prod`
- Lambda: `TaskManagerApi-prod`

## Environment Configuration

### Current Setup
- **Environment**: `prod` (production)
- **Region**: `us-east-1` (configurable in workflow)
- **Database**: PostgreSQL 15.4 on db.t3.micro
- **Lambda**: .NET 8 runtime with 512MB memory

### Customization
To change environment settings, edit [`.github/workflows/dotnet.yml`](.github/workflows/dotnet.yml):

```yaml
env:
  AWS_REGION: us-east-1  # Change region here
  ENVIRONMENT: prod      # Change environment here
```

## Database Setup

### Initial Database Creation
The CloudFormation template creates:
- PostgreSQL 15.4 instance
- Database named `taskmanager`
- Admin user: `taskmanager_admin`
- Encrypted storage
- Automated backups (7 days retention)

### Connection String
The application automatically retrieves database credentials from AWS Secrets Manager.

## Security Features

### Network Security
- Database in private subnets (no internet access)
- Lambda in private subnets with NAT Gateway for outbound
- Security groups restrict access between components
- VPC isolation from other AWS resources

### Credential Security
- Database password stored in Secrets Manager
- Google OAuth credentials passed as environment variables
- No hardcoded secrets in code or configuration

### Monitoring
- CloudWatch alarms for Lambda errors
- Database CPU utilization monitoring
- Centralized logging in CloudWatch

## Troubleshooting

### Common Deployment Issues

1. **CloudFormation Stack Creation Fails**
   - Check IAM permissions
   - Verify parameter values
   - Check AWS service limits

2. **Lambda Function Update Fails**
   - Verify function exists (created by CloudFormation)
   - Check deployment package size
   - Verify IAM permissions

3. **Database Connection Issues**
   - Verify security group rules
   - Check VPC configuration
   - Validate connection string in Secrets Manager

### Debugging Steps

1. **Check CloudFormation Events**
   ```bash
   aws cloudformation describe-stack-events --stack-name taskmanager-prod
   ```

2. **View Lambda Logs**
   ```bash
   aws logs tail /aws/lambda/TaskManagerApi-prod --follow
   ```

3. **Test Database Connectivity**
   ```bash
   aws rds describe-db-instances --db-instance-identifier taskmanager-db-prod
   ```

## Cost Estimation

### Monthly AWS Costs (Approximate)
- **RDS db.t3.micro**: ~$15-20/month
- **Lambda**: ~$0-5/month (depends on usage)
- **API Gateway**: ~$0-5/month (depends on requests)
- **NAT Gateway**: ~$45/month
- **Data Transfer**: ~$0-10/month
- **CloudWatch**: ~$0-5/month

**Total Estimated**: ~$65-90/month

### Cost Optimization Tips
- Use RDS Reserved Instances for production
- Consider Lambda Provisioned Concurrency only if needed
- Monitor and set up billing alerts
- Use CloudWatch cost monitoring

## Production Readiness Checklist

### Before First Deployment
- [ ] Configure all GitHub secrets
- [ ] Set up Google OAuth credentials with production URLs
- [ ] Review and adjust CloudFormation parameters
- [ ] Set up AWS billing alerts
- [ ] Configure custom domain (optional)

### After Deployment
- [ ] Test all application functionality
- [ ] Verify database connectivity
- [ ] Test Google OAuth flow
- [ ] Set up monitoring dashboards
- [ ] Configure backup strategy
- [ ] Document production URLs

## Support and Maintenance

### Regular Tasks
- Monitor CloudWatch alarms
- Review Lambda function performance
- Update dependencies regularly
- Backup database (automated)
- Review security groups and access

### Scaling Considerations
- Increase Lambda memory if needed
- Consider RDS read replicas for high traffic
- Implement caching layer (Redis) if needed
- Set up multi-region deployment for HA

This deployment setup provides a production-ready, scalable foundation for the TaskManager application on AWS.