# GitHub Secrets Configuration Guide

## 🔐 **IMPORTANT: Repository Secrets are SECURE for Public Repos**

### **Repository Secrets** ✅ **SAFE & RECOMMENDED**
**Location**: Your specific repository (awswebapp)
**Path**: Repository → Settings → Secrets and variables → Actions → Repository secrets

### **🛡️ Security Guarantee for Public Repositories**
- ✅ **NEVER EXPOSED**: Secrets are encrypted and never visible in public repo
- ✅ **GITHUB ACTIONS ONLY**: Only accessible to your GitHub Actions workflows
- ✅ **NO PUBLIC ACCESS**: Public repo visitors cannot see or access secrets
- ✅ **ENCRYPTED STORAGE**: GitHub encrypts secrets with repository-specific keys
- ✅ **AUDIT TRAIL**: GitHub logs secret usage for security monitoring

### **Why Repository Secrets are Safe**
- **Encrypted at Rest**: GitHub uses strong encryption for all secrets
- **Access Control**: Only repository admins can view/edit secrets
- **Workflow Isolation**: Secrets only available during workflow execution
- **No Logs**: Secret values never appear in workflow logs (shown as ***)
- **Fork Protection**: Secrets not available to forked repositories

## Step-by-Step Setup

### **1. Navigate to Repository Secrets**
1. Go to your GitHub repository: `https://github.com/yourusername/awswebapp`
2. Click **Settings** (top menu bar)
3. In left sidebar, click **Secrets and variables**
4. Click **Actions**
5. Click **Repository secrets** tab

### **2. Add Required Secrets**
Click **New repository secret** for each of these:

#### **AWS Credentials**
```
Name: AWS_ACCESS_KEY_ID
Value: your-aws-access-key-id-here
```

```
Name: AWS_SECRET_ACCESS_KEY
Value: your-aws-secret-access-key-here
```

#### **Database Configuration**
```
Name: DATABASE_PASSWORD
Value: your-secure-database-password-here
```
**Requirements**: Minimum 8 characters, mix of letters and numbers

#### **Google OAuth Credentials**
```
Name: GOOGLE_CLIENT_ID
Value: your-google-oauth-client-id-here
```

```
Name: GOOGLE_CLIENT_SECRET
Value: your-google-oauth-client-secret-here
```

### **3. Verify Secrets**
After adding all secrets, you should see:
- ✅ AWS_ACCESS_KEY_ID
- ✅ AWS_SECRET_ACCESS_KEY  
- ✅ DATABASE_PASSWORD
- ✅ GOOGLE_CLIENT_ID
- ✅ GOOGLE_CLIENT_SECRET

## Alternative Options (Not Recommended for This Project)

### **Environment Secrets** ❌ **NOT RECOMMENDED**
**Why Not**: 
- More complex setup
- Requires environment configuration
- Overkill for single-environment deployment

### **Organization Secrets** ❌ **NOT RECOMMENDED**
**Why Not**:
- Shared across all repositories
- Security risk if you have multiple projects
- Less granular control

## Security Best Practices

### **Secret Management**
- ✅ **Use Repository Secrets**: Isolated to this project
- ✅ **Rotate Regularly**: Change secrets periodically
- ✅ **Minimum Permissions**: AWS IAM user with least privilege
- ✅ **Monitor Usage**: Check GitHub Actions logs for secret usage

### **AWS IAM User Setup**
Create a dedicated IAM user for GitHub Actions with these policies:
- `CloudFormationFullAccess`
- `AWSLambdaFullAccess`
- `AmazonAPIGatewayAdministrator`
- `AmazonRDSFullAccess`
- `AmazonVPCFullAccess`
- `SecretsManagerReadWrite`
- `CloudWatchFullAccess`

### **Google OAuth Security**
- ✅ **Separate Credentials**: Use different credentials for production
- ✅ **Authorized Domains**: Restrict to your production domain
- ✅ **Regular Rotation**: Change credentials periodically

## Testing Secrets Configuration

### **Verify Secrets Work**
1. **Push to main branch** after configuring secrets
2. **Check GitHub Actions** → Actions tab
3. **Monitor deployment** - should succeed if secrets are correct
4. **Check logs** for any authentication errors

### **Common Secret Issues**

**1. Secret Not Found**
```
Error: Secret AWS_ACCESS_KEY_ID not found
```
**Solution**: Verify secret name matches exactly (case-sensitive)

**2. Invalid AWS Credentials**
```
Error: The security token included in the request is invalid
```
**Solution**: Check AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY values

**3. Database Password Requirements**
```
Error: Password does not meet requirements
```
**Solution**: Ensure password is 8+ characters with letters and numbers

## Quick Setup Checklist

- [ ] Navigate to Repository → Settings → Secrets and variables → Actions
- [ ] Add AWS_ACCESS_KEY_ID secret
- [ ] Add AWS_SECRET_ACCESS_KEY secret
- [ ] Add DATABASE_PASSWORD secret (8+ chars, letters + numbers)
- [ ] Add GOOGLE_CLIENT_ID secret
- [ ] Add GOOGLE_CLIENT_SECRET secret
- [ ] Verify all 5 secrets are listed
- [ ] Push to main branch to test deployment

**Location**: Repository-level secrets in your awswebapp repository
**Access**: Available to GitHub Actions workflows in this repository only
**Security**: Isolated to this project with proper access controls