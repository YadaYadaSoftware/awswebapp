
# Web Project Deployment Strategy

## Current Situation

### **What's Currently Deployed**
- ✅ **API Project**: `TaskManager.Api` deployed to AWS Lambda
- ❌ **Web Project**: `TaskManager.Web` (Blazor Server) NOT deployed to AWS
- ✅ **Database**: PostgreSQL with automatic migrations
- ✅ **Authentication**: Google OAuth with invitation system

### **Current Architecture**
```
GitHub Actions → AWS Lambda (API only)
Local Development → Blazor Server Web Project
```

## Deployment Options for Blazor Server

### **Option 1: Lambda + API Gateway (Recommended)**
**Deploy Blazor Server to Lambda alongside the API**

#### **Benefits**
- ✅ **Unified Deployment**: Single Lambda function hosts both API and web
- ✅ **Cost Effective**: No additional infrastructure needed
- ✅ **Shared Authentication**: Same authentication system
- ✅ **Simple Architecture**: One deployment, one endpoint

#### **Implementation**
```yaml
# Update branch-template.yaml
TaskManagerWebFunction:
  Type: AWS::Serverless::Function
  Properties:
    FunctionName: !Sub 'TaskManagerWeb-${Environment}'
    CodeUri: ../TaskManager.Web
    Handler: TaskManager.Web::TaskManager.Web.LambdaEntryPoint::FunctionHandlerAsync
    Runtime: dotnet8
    Events:
      WebProxy:
        Type: Api
        Properties:
          Path: /web/{proxy+}
          Method: ANY
```

### **Option 2: Elastic Beanstalk**
**Deploy Blazor Server to Elastic Beanstalk**

#### **Benefits**
- ✅ **Traditional Web Hosting**: More familiar deployment model
- ✅ **Auto Scaling**: Automatic scaling based on traffic
- ✅ **Load Balancing**: Built-in load balancer
- ✅ **Health Monitoring**: Application health checks

#### **Considerations**
- ❌ **Higher Cost**: ~$25-50/month minimum
- ❌ **More Complex**: Additional infrastructure to manage
- ❌ **Separate Deployment**: Different pipeline from API

### **Option 3: ECS Fargate**
**Deploy Blazor Server as containerized application**

#### **Benefits**
- ✅ **Container-Based**: Modern deployment approach
- ✅ **Scalable**: Pay-per-use scaling
- ✅ **Flexible**: Full control over runtime environment

#### **Considerations**
- ❌ **Complexity**: Requires Docker containerization
- ❌ **Cost**: More expensive than Lambda for low traffic
- ❌ **Setup Time**: Additional configuration required

### **Option 4: Static Site (Blazor WebAssembly)**
**Convert to Blazor WebAssembly and deploy to S3 + CloudFront**

#### **Benefits**
- ✅ **Very Low Cost**: S3 + CloudFront ~$1-5/month
- ✅ **High Performance**: CDN distribution
- ✅ **Scalable**: Handles any traffic level

#### **Considerations**
- ❌ **Architecture Change**: Requires converting from Server to WebAssembly
- ❌ **API Calls**: All data access through API calls
- ❌ **Limited Features**: Some Blazor Server features not available

## Recommended Approach

### **Option 1: Unified Lambda Deployment** ⭐ **RECOMMENDED**

#### **Why This is Best**
- ✅ **Simplest**: Minimal changes to current architecture
- ✅ **Cost Effective**: No additional infrastructure
- ✅ **Unified**: Single endpoint for both API and web
- ✅ **Shared Auth**: Same authentication and session management

#### **Implementation Steps**
1. **Update branch-template.yaml** to include web function
2. **Create LambdaEntryPoint** for Blazor Server
3. **Configure routing** between API and web endpoints
4. **Update GitHub Actions** to deploy both projects

### **Current Web Project Status**
**TaskManager.Web Features**:
- ✅ **Google OAuth**: Configured and working
- ✅ **Authorization**: 15-minute sessions, invitation checking
- ✅ **Blazor Components**: Login/logout UI
- ✅ **Authentication State**: Proper cascading auth state

## Implementation Plan

### **To Deploy Web Project to Lambda**

#### **Step 1: Add Web Lambda to Branch Template**
```yaml
# Add to branch-template.yaml
TaskManagerWebFunction:
  Type: AWS::Serverless::Function
  Properties:
    FunctionName: !Sub 'TaskManagerWeb-${Environment}'
    CodeUri: ../TaskManager.Web
    Handler: TaskManager.Web::TaskManager.Web.LambdaEntryPoint::FunctionHandlerAsync
    Runtime: dotnet8
    Environment:
      Variables:
        ASPNETCORE_ENVIRONMENT: !Ref Environment
        ConnectionStrings__DefaultConnection: !Sub 
          - 'Host=${DatabaseEndpoint};Database=taskmanager_${DatabaseName};Username=taskmanager_admin;Password=${DatabasePassword}'
          - DatabaseEndpoint: 
              Fn::ImportValue: 'TaskManager-DatabaseEndpoint'
            DatabaseName: !Sub '${Environment}'
            DatabasePassword: !Sub '{{resolve:secretsmanager:taskmanager/database/shared:SecretString:password}}'
        Authentication__Google__ClientId: !Ref GoogleClientId
        Authentication__Google__ClientSecret: !Ref GoogleClientSecret
    VpcConfig:
      SecurityGroupIds:
        - Fn::ImportValue: 'TaskManager-LambdaSecurityGroup'
      SubnetIds:
        - Fn::ImportValue: 'TaskManager-PrivateSubnet1'
        - Fn::ImportValue: 'TaskManager-PrivateSubnet2'
    Events:
      WebRoot:
        Type: Api
        Properties:
          Path: /
          Method: ANY
      WebProxy:
        Type: Api
        Properties:
          Path: /{proxy+}
          Method: ANY
```

#### **Step 2: Create Web Lambda Entry Point**
```csharp
// Add to TaskManager.Web/LambdaEntryPoint.cs
public class LambdaEntryPoint : Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
{
    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Program>();
    }
}
```

#### **Step 3: Update GitHub Actions**
- Build both API and Web projects
- Deploy both to Lambda functions
- Configure routing between them

## Current Recommendation

### **For Now: Keep Web Local, Deploy API**
**Current setup is actually good for development**:
- ✅ **API on AWS**: Production-ready API with database
- ✅ **Web Local**: Fast development cycle for UI changes
- ✅ **Shared Database**: Web can connect to AWS database for testing

### **When Ready for Production**
**Add web deployment using Option 1 (Unified Lambda)**

**Would you like me to implement the web deployment to Lambda, or keep the current API-only deployment for now?**
</result>
</attempt_completion>