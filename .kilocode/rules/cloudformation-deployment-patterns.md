## Brief overview
Guidelines for CloudFormation infrastructure architecture and deployment patterns in AWS multi-region applications, emphasizing parameter-based communication and proper sequencing of infrastructure components.

## CloudFormation stack communication
- This approach provides better control over stack dependencies and deployment ordering
- Avoids global namespace conflicts that can occur with exports across accounts/regions

## Infrastructure deployment pattern
- Deploy infrastructure stacks in sequence, passing outputs as parameters to dependent stacks
- Use GitHub Actions job outputs to transfer values between deployment steps
- Prefer explicit parameter passing over implicit cross-stack references
- This pattern supports branch-based deployments without export name collisions

## KMS key management
- Deploy primary multi-region KMS keys first in the primary region
- Deploy KMS key replicas in all regions using the primary key ARN
- Use separate CloudFormation templates for primary keys and replicas
- Ensure primary region deploys first with max-parallel: 1 in matrix jobs

## GitHub Actions workflow patterns
- Generate stack name variables at the beginning of each deployment job
- Use consistent prefix patterns: `taskmanager-${branch-name}-component-name`
- Define specific stack name variables for each component type
- Use matrix jobs with max-parallel: 1 for sequential regional deployments

## Parameter validation
- Ensure all CloudFormation template parameters are specified in workflow parameter-overrides
- Use comma-separated format for parameter-overrides (not YAML multiline)
- Validate that all specified parameters exist in the corresponding templates
- Include BranchName parameter in templates that need branch-specific naming

## Stack output usage
- Design CloudFormation templates to accept all required values as parameters
- Use stack outputs for visibility and integration with external systems
- Avoid creating inter-stack dependencies through exports
- Keep infrastructure modular and independently deployable