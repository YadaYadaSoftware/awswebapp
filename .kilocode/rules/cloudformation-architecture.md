## Brief overview
Project-specific guidelines for CloudFormation infrastructure architecture, emphasizing parameter-based stack composition over export/import mechanisms.

## Infrastructure deployment pattern
- Deploy infrastructure stacks in sequence, passing outputs as parameters to dependent stacks
- Use GitHub Actions job outputs to transfer values between deployment steps
- Prefer explicit parameter passing over implicit cross-stack references
- This pattern supports branch-based deployments without export name collisions

## Stack output usage
- Design CloudFormation templates to accept all required values as parameters
- Use stack outputs for visibility and integration with external systems
- Avoid creating inter-stack dependencies through exports
- Keep infrastructure modular and independently deployable

## Deployment workflow
- Infrastructure stacks deploy first, producing outputs
- Application stacks consume infrastructure outputs as parameters
- No cross-stack references within CloudFormation templates
- All dependencies managed through deployment pipeline orchestration