# "Hybrid" Deployment — there isn't one

> This document previously described a "CloudFormation-for-infrastructure +
> SAM-for-Lambda" hybrid built around `infrastructure/cloudformation.yaml` and a
> Lambda Annotations `serverless.template`. **None of that exists in this
> repository.** It has been corrected to point at the real, single deployment
> model.

## There is one deployment model, not two

The application is **not** split across a CloudFormation infrastructure stack and
a separate SAM/Lambda application stack. There is no `serverless.template`, no
`infrastructure/cloudformation.yaml`, and no Lambda function serving traffic.

What actually happens:

- **AWS SAM** is used only as a *packager* — `sam build` + `sam package` resolve
  the nested CloudFormation templates under
  [infrastructure/](../../infrastructure/) and upload them to S3.
- **CloudFormation** then deploys one env stack
  (`{branch-leaf}-{dashed-domain}`) made of nested stacks
  (`master.template` → `backend.template` + `application.template` → their
  children).
- The deployed surface is the **`Tjb.Web` Blazor Server container on ECS Fargate
  behind an ALB**, backed by **Aurora MySQL Serverless v2**.

So SAM and CloudFormation are not two competing stacks for two concerns — they
are two stages of one pipeline: SAM packages, CloudFormation deploys.

## Where to read the real thing

- [DEPLOYMENT_STRATEGY.md](DEPLOYMENT_STRATEGY.md) — the nested-stack template
  tree, branch → template/region mapping, and multi-region behavior.
- [AWS_DEPLOYMENT_GUIDE.md](AWS_DEPLOYMENT_GUIDE.md) — what each stage of the
  pipeline does on a push.
- [AWS_DEPLOYMENT_SETUP.md](AWS_DEPLOYMENT_SETUP.md) — GitHub secrets/variables
  and the per-region bootstrap stack.
- [.github/workflows/zbuild.yml](../../.github/workflows/zbuild.yml) — the
  authoritative implementation.

## If you came here looking for Lambda

`Tjb.Api` still contains Lambda hosting glue (`Amazon.Lambda.AspNetCoreServer`,
`LambdaEntryPoint`), but it is **not** deployed as the application and serves no
production traffic. Treat it as vestigial. All real authentication and the live
web surface live in `Tjb.Web`.
