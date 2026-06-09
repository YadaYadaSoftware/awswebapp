# Lambda Annotations Notes

## Status: Lambda hosting was abandoned

Lambda was an early hosting idea for this application. It is **not** how the app runs
today. The deployed surface is `Tjb.Web` as a Docker container on **ECS Fargate behind
an Application Load Balancer** (see [WEB_DEPLOYMENT_STRATEGY.md](WEB_DEPLOYMENT_STRATEGY.md)).
There is no Lambda function in the deployment pipeline.

## Why this file still exists

`Tjb.Api` is a secondary, largely vestigial project. Its
[Tjb.Api.csproj](../Tjb.Api/Tjb.Api.csproj) still references the Lambda packages:

```xml
<PackageReference Include="Amazon.Lambda.Annotations" Version="1.7.0" />
<PackageReference Include="Amazon.Lambda.AspNetCoreServer" Version="8.1.0" />
```

These are leftover hosting glue. `Tjb.Api` exposes only `/health`, Swagger (in dev),
and a no-op stub `AuthController` — it is not deployed and adding endpoints there does
not reach users. All real functionality lives in `Tjb.Web`.

The DbContext is `TjbDbContext` (in `Tjb.Data`), which extends
`IdentityDbContext<IdentityUser>` and is configured with **MySQL** via
`UseMySql` (`Pomelo.EntityFrameworkCore.MySql`) — not PostgreSQL/Npgsql.

## If Lambda is ever revisited

The original blocker was wiring dependency injection through the Lambda Annotations
source generator (it expects parameterless handler constructors, while a Functions
class would need the `TjbDbContext` injected). The standard fix is a Lambda startup
class that registers services, e.g.:

```csharp
[assembly: LambdaStartup(typeof(Startup))]

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        services.AddDbContext<TjbDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
    }
}
```

This is documented only for completeness. The current and intended hosting model is
the ECS Fargate container described in
[WEB_DEPLOYMENT_STRATEGY.md](WEB_DEPLOYMENT_STRATEGY.md); prefer it over reintroducing
Lambda.

## References

- [AWS Lambda Annotations](https://github.com/aws/aws-lambda-dotnet/tree/master/Libraries/src/Amazon.Lambda.Annotations)
