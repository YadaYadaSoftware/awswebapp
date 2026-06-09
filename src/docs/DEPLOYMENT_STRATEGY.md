# Deployment Strategy — SAM-packaged nested CloudFormation deploying ECS Fargate

> **Authoritative sources:** the templates under
> [infrastructure/](../../infrastructure/) and the deploy workflow
> [.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml). Trust those
> over this summary.

## The model in one sentence

A single deployment model: **AWS SAM packages a tree of nested CloudFormation
templates, and CloudFormation deploys an ECS Fargate + ALB web app backed by
Aurora MySQL Serverless v2.** There is no Lambda Annotations
`serverless.template`, no `infrastructure/cloudformation.yaml`, and no
"CloudFormation-for-infra + SAM-for-Lambda" hybrid.

## Template tree

The env stack is a nested-stack tree rooted at one of two top-level templates,
chosen by branch:

```
master.template            # app / beta / alpha / dev — full environment incl. backend
├── backend.template
│   ├── security.template        # SharedLambdaExecutionRole (ECS tasks assume it; includes SES access)
│   ├── network.template         # VPC, subnets, ALB/ECS/Lambda security groups, flow logs
│   ├── db.template              # Aurora MySQL Serverless v2 (+ Global Cluster on app/beta/alpha)
│   └── infrastructure.template  # ECS cluster, Google OAuth secret
└── application.template
    ├── api.template
    ├── web.template             # ALB + listeners + ACM cert + ECS Fargate task def & service
    └── dns.template             # Route 53 records / failover health checks

application.template       # every other branch — app stack only, imports dev's backend exports
```

- `backend.template` exports VPC/subnet/security-group IDs, the ECS cluster name,
  the shared role ARN, and the Aurora connection details. Exports are
  domain-qualified, named `{Key}-{branch}-{dashed-domain}`.
- `application.template` (when deployed standalone for a feature branch) imports
  those exports from **`dev`** via `Fn::ImportValue`, so feature branches reuse
  dev's backend instead of standing up their own Aurora cluster.

## How the workflow drives it

For each region in the deploy matrix:

1. `sam build` + `sam package` the chosen top-level template to the templates S3
   bucket (`{account}-{dashed-domain}-{region}`), resolving nested `TemplateURL`
   references to S3 URLs.
2. Build/push the `Tjb.Web` Docker image to ECR (content-addressed by SHA256 of
   `src/`; reused if the tag already exists).
3. Deploy the packaged template as CloudFormation stack
   `{branch-leaf}-{dashed-domain}` with capabilities
   `CAPABILITY_NAMED_IAM,CAPABILITY_AUTO_EXPAND`, passing the container image URI
   and the other parameter overrides.

## Branch → strategy

| Branch | Template | Regions | Aurora |
|---|---|---|---|
| `app` | `master.template` | primary + secondary | Global Cluster; capacities 0.5–4 ACU |
| `beta`, `alpha` | `master.template` | primary + secondary | Global Cluster; capacities 0–1 ACU |
| `dev` | `master.template` | primary only | single-region cluster; capacities 0–1 ACU |
| `{type}/{name}` and bare OpenSpec change branches | `application.template` | primary only | none — imports `dev`'s backend |

Multi-region branches deploy the primary region first, then the secondary region
reads the primary's stack outputs (Aurora Global Cluster ID, primary ALB DNS and
hosted-zone) to wire up Route 53 failover and join the secondary Aurora cluster
to the global cluster.

## Region/domain agnosticism

Nothing in the templates or workflow hardcodes a domain or region. The deploy
job derives the dashed domain from `secrets.DOMAIN_NAME`; templates take
`DomainName` in dot form and derive dashed/underscored forms locally with
`Fn::Join`/`Fn::Split`. Regions come from the `AWS_REGION_PRIMARY` /
`AWS_REGION_SECONDARY` repo variables.

## What this strategy explicitly is not

- **Not** AWS Lambda + API Gateway. `Tjb.Api` carries vestigial Lambda glue but
  is not the deployed surface.
- **Not** RDS PostgreSQL. The database is Aurora MySQL Serverless v2 (Pomelo /
  `UseMySql`, engine `8.0.mysql_aurora.3.10.0`).
- **Not** a two-template hybrid. The `serverless.template` /
  `cloudformation.yaml` split described in older docs does not exist.

## See also

- [AWS_DEPLOYMENT_GUIDE.md](AWS_DEPLOYMENT_GUIDE.md) — pipeline stages.
- [AWS_DEPLOYMENT_SETUP.md](AWS_DEPLOYMENT_SETUP.md) — secrets/variables/bootstrap.
- [ARCHITECTURE.md](ARCHITECTURE.md) — system architecture and projects.
