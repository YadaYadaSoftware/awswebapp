# Lambda Annotations Implementation Notes

## Current Status
The project is configured with Amazon.Lambda.Annotations packages but the Lambda functions are not yet implemented due to dependency injection configuration challenges.

## Issue Encountered
The Lambda Annotations source generator creates Lambda function handlers that expect parameterless constructors, but our Functions class requires a `TaskManagerDbContext` parameter for dependency injection.

## Error Details
```
error CS7036: There is no argument given that corresponds to the required parameter 'context' of 'Functions.Functions(TaskManagerDbContext)'
```

## Solution Approaches to Try

### Option 1: Use Lambda Startup Class
Create a proper Lambda startup class that configures dependency injection:

```csharp
[assembly: LambdaStartup(typeof(Startup))]

public class Startup : LambdaStartup
{
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<TaskManagerDbContext>(options =>
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            options.UseNpgsql(connectionString);
        });
    }
}
```

### Option 2: Use Static Service Provider
Configure a static service provider in the Lambda function:

```csharp
public class Functions
{
    private static IServiceProvider? _serviceProvider;
    
    static Functions()
    {
        var services = new ServiceCollection();
        // Configure services...
        _serviceProvider = services.BuildServiceProvider();
    }
    
    [LambdaFunction]
    public async Task<string> GetProjects(ILambdaContext context)
    {
        using var scope = _serviceProvider!.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskManagerDbContext>();
        // Implementation...
    }
}
```

### Option 3: Use Lambda Annotations with Dependency Injection Framework
Follow the official AWS Lambda Annotations documentation for proper DI setup.

## Next Steps
1. Research the latest Lambda Annotations documentation
2. Implement proper dependency injection configuration
3. Test with a simple Lambda function first
4. Gradually add more complex functions

## Current Workaround
For now, the Lambda Annotations packages are installed and configured, but no Lambda functions are implemented. The project builds successfully and can be deployed as a regular ASP.NET Core application.

## References
- [AWS Lambda Annotations Documentation](https://github.com/aws/aws-lambda-dotnet/tree/master/Libraries/src/Amazon.Lambda.Annotations)
- [Lambda Dependency Injection](https://docs.aws.amazon.com/lambda/latest/dg/csharp-handler.html#csharp-handler-dependency-injection)