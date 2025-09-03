# AWS Credentials Setup Guide

## Overview
This guide walks you through creating AWS credentials for GitHub Actions deployment of the TaskManager application.

## Step-by-Step AWS IAM Setup

### **Step 1: Sign in to AWS Console**
1. Go to [AWS Console](https://aws.amazon.com/console/)
2. Sign in with your AWS account
3. Navigate to **IAM** service (search "IAM" in the top search bar)

### **Step 2: Create IAM User for GitHub Actions**
1. In IAM Console, click **Users** in the left sidebar
2. Click **Create user**
3. **User name**: `taskmanager-github-actions`
4. **Access type**: Select **Programmatic access** (no AWS Console access needed)
5. Click **Next**

### **Step 3: Attach Permissions**
**Option A: Use Existing AWS Managed Policies (Easier)**
1. Click **Attach policies directly**
2. Search and select these policies:
   - ✅ `CloudFormationFullAccess`
   - ✅ `AWSLambdaFullAccess`
   - ✅ `AmazonAPIGatewayAdministrator`
   - ✅ `AmazonRDSFullAccess`
   - ✅ `AmazonVPCFullAccess`
   - ✅ `SecretsManagerReadWrite`
   - ✅ `CloudWatchFullAccess`
   - ✅ `IAMFullAccess` (needed for creating roles)
   - ✅ `AmazonS3FullAccess` (needed for SAM deployment artifacts)

**Option B: Create Custom Policy (More Secure)**
1. Click **Create policy**
2. Use the JSON policy below
3. Attach the custom policy to the user

### **Step 4: Create Access Keys**
1. After user creation, click on the user name
2. Go to **Security credentials** tab
3. Scroll down to **Access keys** section
4. Click **Create access key**
5. Choose **Application running outside AWS**
6. Click **Next**
7. **Description**: `TaskManager GitHub Actions Deployment`
8. Click **Create access key**

### **Step 5: Save Credentials**
**⚠️ IMPORTANT**: Copy these values immediately (you won't see the secret again):
- **Access Key ID**: `AKIA...` (copy this)
- **Secret Access Key**: `...` (copy this)

## Custom IAM Policy (Option B)

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "cloudformation:*",
                "lambda:*",
                "apigateway:*",
                "rds:*",
                "ec2:*",
                "secretsmanager:*",
                "logs:*",
                "iam:CreateRole",
                "iam:DeleteRole",
                "iam:GetRole",
                "iam:PassRole",
                "iam:AttachRolePolicy",
                "iam:DetachRolePolicy",
                "iam:PutRolePolicy",
                "iam:DeleteRolePolicy",
                "iam:GetRolePolicy",
                "iam:ListRolePolicies",
                "iam:ListAttachedRolePolicies",
                "s3:CreateBucket",
                "s3:DeleteBucket",
                "s3:GetObject",
                "s3:PutObject",
                "s3:DeleteObject",
                "s3:ListBucket"
            ],
            "Resource": "*"
        }
    ]
}
```

## Automated Setup Script

### **AWS CLI Script** (if you have AWS CLI configured)
```bash
#!/bin/bash
# aws-setup-github-user.sh

# Create IAM user
aws iam create-user --user-name taskmanager-github-actions

# Attach managed policies
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/CloudFormationFullAccess
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/AWSLambdaFullAccess
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/AmazonAPIGatewayAdministrator
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/AmazonRDSFullAccess
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/AmazonVPCFullAccess
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/SecretsManagerReadWrite
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/CloudWatchFullAccess
aws iam attach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/IAMFullAccess

# Create access key
aws iam create-access-key --user-name taskmanager-github-actions

echo "✅ User created successfully!"
echo "⚠️  Copy the Access Key ID and Secret Access Key from the output above"
echo "🔐 Add them to GitHub repository secrets"
```

## Security Best Practices

### **IAM User Security**
- ✅ **Programmatic Access Only**: No console access needed
- ✅ **Specific Permissions**: Only permissions needed for deployment
- ✅ **Regular Rotation**: Rotate access keys every 90 days
- ✅ **Monitoring**: Enable CloudTrail to monitor API usage

### **Access Key Management**
- ✅ **Immediate Storage**: Copy keys to GitHub secrets immediately
- ✅ **No Local Storage**: Don't save keys in files or environment variables
- ✅ **Single Use**: Keys only for GitHub Actions, not local development
- ✅ **Deactivation**: Deactivate old keys when rotating

## Verification Steps

### **Test AWS Credentials**
After creating the user and access keys:

```bash
# Configure AWS CLI with new credentials (temporarily)
aws configure set aws_access_key_id YOUR_ACCESS_KEY_ID
aws configure set aws_secret_access_key YOUR_SECRET_ACCESS_KEY
aws configure set region us-east-1

# Test permissions
aws sts get-caller-identity
aws cloudformation list-stacks --stack-status-filter CREATE_COMPLETE

# Clean up test configuration
aws configure set aws_access_key_id ""
aws configure set aws_secret_access_key ""
```

## GitHub Secrets Configuration

### **Add to Repository Secrets**
1. Go to your GitHub repository
2. **Settings** → **Secrets and variables** → **Actions**
3. **Repository secrets** tab
4. Add these secrets:

```
AWS_ACCESS_KEY_ID=AKIA... (from Step 5 above)
AWS_SECRET_ACCESS_KEY=... (from Step 5 above)
DATABASE_PASSWORD=YourSecurePassword123!
GOOGLE_CLIENT_ID=your-google-client-id
GOOGLE_CLIENT_SECRET=your-google-client-secret
```

## Troubleshooting

### **Common Issues**

**1. "Access Denied" Errors**
- Verify all required policies are attached
- Check IAM user has programmatic access
- Ensure access keys are active

**2. "User Already Exists"**
- Choose a different username
- Or delete existing user and recreate

**3. "Policy Not Found"**
- Verify policy ARNs are correct
- Check you're in the right AWS region

### **Cleanup (if needed)**
```bash
# Delete access keys
aws iam list-access-keys --user-name taskmanager-github-actions
aws iam delete-access-key --user-name taskmanager-github-actions --access-key-id YOUR_KEY_ID

# Delete user
aws iam detach-user-policy --user-name taskmanager-github-actions --policy-arn arn:aws:iam::aws:policy/CloudFormationFullAccess
# (repeat for all attached policies)
aws iam delete-user --user-name taskmanager-github-actions
```

## Quick Setup Checklist

- [ ] Sign in to AWS Console
- [ ] Navigate to IAM → Users
- [ ] Create user: `taskmanager-github-actions`
- [ ] Attach required policies (8 policies total)
- [ ] Create access key
- [ ] Copy Access Key ID and Secret Access Key
- [ ] Add both to GitHub repository secrets
- [ ] Test deployment by pushing to main branch

**Result**: Secure AWS credentials ready for automated deployment!