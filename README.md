# TaskManager - C# Web Application for AWS

A full-stack task and project management web application built with C# that deploys to AWS Lambda, featuring Google OAuth authentication, PostgreSQL database, and Blazor Server frontend.

## 🏗️ Architecture Overview

This application is designed as a modern, cloud-native solution using:

- **Backend**: .NET 8 Minimal Web API
- **Frontend**: Blazor Server
- **Database**: PostgreSQL (AWS RDS)
- **Authentication**: Google OAuth with extensible provider pattern
- **Hosting**: AWS Lambda with API Gateway
- **Infrastructure**: AWS (RDS, Lambda, API Gateway, CloudWatch)

## 📋 Features

### Core Functionality
- ✅ User authentication via Google OAuth
- ✅ Project creation and management
- ✅ Task creation, assignment, and tracking
- ✅ Project member management with role-based access
- ✅ Real-time updates with Blazor Server
- ✅ Responsive design for mobile and desktop

### Technical Features
- ✅ Entity Framework Core with PostgreSQL
- ✅ Minimal Web API with clean architecture
- ✅ JWT-based authentication
- ✅ Role-based authorization
- ✅ AWS Lambda deployment
- ✅ CloudWatch monitoring and logging
- ✅ Automated CI/CD pipeline

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- PostgreSQL 15+ (for local development)
- AWS CLI configured
- Google Cloud Console account (for OAuth)
- Visual Studio 2022 or VS Code

### Local Development Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd awswebapp
   ```

2. **Set up local PostgreSQL database**
   ```bash
   # Create database
   createdb taskmanager_dev
   ```

3. **Configure user secrets**
   ```bash
   cd src/TaskManager.Api
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=taskmanager_dev;Username=your_username;Password=your_password"
   dotnet user-secrets set "Authentication:Google:ClientId" "your-google-client-id"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "your-google-client-secret"
   ```

4. **Run database migrations**
   ```bash
   dotnet ef database update
   ```

5. **Start the application**
   ```bash
   dotnet run --project src/TaskManager.Web
   ```

## 📁 Project Structure

```
src/
├── TaskManager.Api/              # Minimal Web API
│   ├── Program.cs               # API configuration and endpoints
│   ├── Endpoints/               # API endpoint definitions
│   ├── Services/                # Business logic services
│   └── TaskManager.Api.csproj
├── TaskManager.Data/             # Data access layer
│   ├── TaskManagerDbContext.cs  # EF Core DbContext
│   ├── Entities/                # Database entities
│   ├── Configurations/          # EF Core configurations
│   ├── Migrations/              # Database migrations
│   └── TaskManager.Data.csproj
├── TaskManager.Web/              # Blazor Server application
│   ├── Program.cs               # Web app configuration
│   ├── Pages/                   # Blazor pages
│   ├── Components/              # Reusable components
│   ├── Services/                # Client-side services
│   └── TaskManager.Web.csproj
├── TaskManager.Shared/           # Shared models and DTOs
│   ├── Models/                  # Data transfer objects
│   ├── Enums/                   # Shared enumerations
│   └── TaskManager.Shared.csproj
└── TaskManager.sln              # Solution file
```

## 🗄️ Database Schema

### Core Entities

**Users**
- Id, Email, FirstName, LastName
- GoogleId (for OAuth integration)
- CreatedAt, UpdatedAt, IsActive

**Projects**
- Id, Name, Description
- OwnerId (foreign key to Users)
- CreatedAt, UpdatedAt, IsActive

**Tasks**
- Id, Title, Description
- ProjectId (foreign key to Projects)
- AssignedToId (foreign key to Users)
- Status (Todo, InProgress, Done)
- Priority (Low, Medium, High)
- DueDate, CreatedAt, UpdatedAt

**ProjectMembers** (Many-to-Many)
- ProjectId, UserId
- Role (Owner, Admin, Member, Viewer)
- JoinedAt

## 🔐 Authentication & Authorization

### Google OAuth Integration
- Extensible authentication provider pattern
- Support for multiple OAuth providers (future)
- JWT token-based API authentication
- Role-based authorization for projects

### Security Features
- HTTPS enforcement
- CSRF protection
- Input validation and sanitization
- SQL injection prevention via EF Core
- Secure credential storage in AWS Secrets Manager

## ☁️ AWS Deployment

### Infrastructure Components
- **RDS PostgreSQL**: Primary database
- **Lambda Function**: Application hosting
- **API Gateway**: HTTP API routing
- **CloudWatch**: Logging and monitoring
- **Secrets Manager**: Secure credential storage

### Deployment Process
1. Set up AWS infrastructure using CloudFormation
2. Build and package .NET application
3. Deploy Lambda function
4. Configure API Gateway routing
5. Run database migrations
6. Configure monitoring and alerts

See [`AWS_DEPLOYMENT_GUIDE.md`](AWS_DEPLOYMENT_GUIDE.md) for detailed deployment instructions.

## 📊 Monitoring & Logging

### CloudWatch Integration
- Application logs with structured logging
- Performance metrics and alarms
- Error tracking and alerting
- Database performance monitoring

### Health Checks
- Database connectivity monitoring
- Lambda function health checks
- API endpoint availability monitoring

## 🧪 Testing Strategy

### Unit Testing
- Service layer unit tests
- API endpoint testing
- Business logic validation
- Authentication flow testing

### Integration Testing
- Database operation testing
- API integration testing
- End-to-end workflow testing

## 📈 Performance Considerations

### Lambda Optimization
- Connection pooling for database
- Cold start mitigation strategies
- Memory allocation tuning
- Async/await patterns

### Database Optimization
- Proper indexing strategy
- Query optimization with EF Core
- Connection pooling
- Read replica support (future)

## 🔄 CI/CD Pipeline

### GitHub Actions Workflow
- Automated testing on pull requests
- Build and package validation
- Automated deployment to staging/production
- Database migration automation

### Deployment Stages
1. **Development**: Local development environment
2. **Staging**: AWS staging environment for testing
3. **Production**: AWS production environment

## 📚 Documentation

- [`ARCHITECTURE.md`](ARCHITECTURE.md) - Detailed system architecture
- [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) - Step-by-step implementation guide
- [`AWS_DEPLOYMENT_GUIDE.md`](AWS_DEPLOYMENT_GUIDE.md) - AWS infrastructure and deployment
- API documentation (generated via Swagger/OpenAPI)

## 🛠️ Development Workflow

### Getting Started with Development
1. Review the architecture documentation
2. Set up local development environment
3. Follow the implementation plan phase by phase
4. Test locally before deploying to AWS
5. Use the deployment guide for AWS setup

### Recommended Development Order
1. **Phase 1**: Project setup and foundation
2. **Phase 2**: Data layer implementation
3. **Phase 3**: Authentication system
4. **Phase 4**: API development
5. **Phase 5**: Blazor Server frontend
6. **Phase 6**: AWS infrastructure setup
7. **Phase 7**: Deployment and DevOps
8. **Phase 8**: Testing and quality assurance

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

For questions, issues, or contributions:
- Review the documentation in this repository
- Check the implementation plan for guidance
- Refer to the AWS deployment guide for infrastructure questions

---

**Next Steps**: Ready to start implementation? Switch to Code mode and begin with Phase 1 of the implementation plan!