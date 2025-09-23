## Brief overview
Guidelines for managing CloudFormation stack names in GitHub Actions workflows to ensure consistency, maintainability, and avoid naming inconsistencies across deployments and verifications.

## Stack name variable creation
- Generate stack name variables at the beginning of each job using environment variables
- Use a consistent prefix pattern: `BRANCH_PREFIX = taskmanager-${{ needs.get-branch-name.outputs.branch-name }}`
- Define specific stack name variables for each component (e.g., REGIONAL_VPC_DB_STACK_NAME, GLOBAL_INFRA_STACK_NAME)

## Usage in deployments
- Always use variables in `aws-actions/aws-cloudformation-github-deploy` action `name` parameter
- Ensure regional stacks include region suffix for uniqueness across matrix runs
- Update all cleanup and verification steps to use the same variables

## Consistency checks
- Verify that deploy actions and verification commands use identical stack name references
- Fix any mismatches between deployment names and verification stack names
- Standardize naming patterns across all jobs (global vs regional)

## Maintenance benefits
- Single source of truth for stack names reduces errors from typos
- Easier to update naming conventions across the entire workflow
- Clear separation between branch-specific and region-specific components