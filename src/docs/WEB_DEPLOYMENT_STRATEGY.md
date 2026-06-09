# Web Deployment Strategy

`Tjb.Web` (Blazor Server + Razor Pages + ASP.NET Identity + Google OAuth) **is the
deployed application**. It runs as a Docker container on **ECS Fargate behind an
Application Load Balancer (ALB)**. An earlier idea to deploy via AWS Lambda was
abandoned; the Lambda packages that remain on `Tjb.Api` are vestigial (see
[LAMBDA_ANNOTATIONS_NOTES.md](LAMBDA_ANNOTATIONS_NOTES.md)).

## Architecture

```
User browser → ALB (HTTPS :443) → ECS Fargate task (Tjb.Web container :80)
                                        └→ Aurora MySQL (private subnets, :3306)
```

- The ALB terminates HTTPS using an ACM certificate for `{branch}.{domain}` and
  redirects HTTP→HTTPS. `Tjb.Web` configures `ForwardedHeaders` so the
  `/signin-google` OAuth callback sees HTTPS behind the proxy.
- The Fargate task runs in private subnets; the ALB sits in public subnets.
- Health checks hit `/health` (excluded from authentication).

## How it's built and deployed

**Image build** — [../Tjb.Web/Dockerfile](../Tjb.Web/Dockerfile) is a multi-stage
build on `mcr.microsoft.com/dotnet/sdk:10.0` → runtime
`mcr.microsoft.com/dotnet/aspnet:10.0`, publishing `Tjb.Web` and exposing port 80
(`ENTRYPOINT ["dotnet", "Tjb.Web.dll"]`).

**Pipeline** — [../../.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml)
builds and tests, builds the Docker image, pushes it to **ECR**, then deploys via
SAM/CloudFormation. The image is content-addressed by a SHA256 of `src/`; if a tag
already exists in ECR the build step is skipped. UI tests
([../Tjb.UiTests](../Tjb.UiTests)) then run against the deployed URL.

**Infrastructure** — [../../infrastructure/web.template](../../infrastructure/web.template)
defines the web tier:

- `AWS::ECS::TaskDefinition` — Fargate, `Cpu: 512` / `Memory: 1024`, one container
  named `web` on port 80.
- `AWS::ECS::Service` — `LaunchType: FARGATE`, `DesiredCount: 1`, registered with the
  target group; `HealthCheckGracePeriodSeconds: 300`.
- `AWS::ElasticLoadBalancingV2::LoadBalancer` + HTTP listener (301 redirect to HTTPS)
  + HTTPS listener forwarding to the target group (`TargetType: ip`, health check
  `/health`).
- `AWS::CertificateManager::Certificate` for `{BranchName}.{DomainName}` (DNS
  validation).

VPC, subnets, security groups, the ECS cluster, the execution/task role, and the
database host/name/username/password-secret are **imported** from the backend stack
via `Fn::ImportValue` (export names are domain-and-branch qualified).

## Container configuration (environment variables)

Set in the task definition in
[../../infrastructure/web.template](../../infrastructure/web.template):

- `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS=http://*:80`.
- `ConnectionStrings__DefaultConnection` — built from imported `DatabaseHost`,
  `DatabaseName`, `DatabaseUsername`, and the password resolved from Secrets Manager
  (`{{resolve:secretsmanager:...}}`), with `SslMode=Required` (Aurora MySQL, port
  3306).
- `Authentication__Google__ClientId` / `ClientSecret` — resolved from the
  `{dashed-domain}/google-oauth/{branch}` secret.
- `AwsSes__SenderEmail=noreply@appcloud.systems`.
- CloudWatch logging via the `awslogs` driver to a per-deploy log group.

## Deploy targets

- `app` / `test` — multi-region shared infrastructure (full
  `master.template`, Aurora Global Cluster).
- `dev` — single-region, also uses `master.template`.
- Other `{type}/{name}` branches — deploy `application.template` (app stack only,
  importing backend exports from `dev`) into a per-branch stack named
  `{branch-leaf}-{dashed-domain}`, served at `https://{branch}.{domain}`.

See [../../BRANCH_MANAGEMENT_README.md](../../BRANCH_MANAGEMENT_README.md) for the full
branch model.
