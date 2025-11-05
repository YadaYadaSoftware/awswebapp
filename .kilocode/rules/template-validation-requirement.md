## Brief overview
Project-wide requirement to run yamllint and validate-template on CloudFormation templates after any modifications to ensure syntax correctness, prevent deployment failures, and maintain CI/CD reliability.

## Template modification procedure
- Run yamllint on all modified CloudFormation template files (.template files) after any changes
- Run aws cloudformation validate-template on all modified CloudFormation template files to verify template syntax and structure
- Validate both YAML syntax and CloudFormation-specific requirements before committing changes
- Prevent deployment failures caused by template syntax errors or invalid CloudFormation constructs

## Validation scope
- Apply to all CloudFormation template files in the infrastructure/ directory
- Run validation in both local development and CI/CD environments
- Include validation in pre-commit hooks when available
- Check for both YAML syntax errors and CloudFormation validation issues

## Error handling approach
- Provide specific error messages with file names and line numbers for yamllint issues
- Display CloudFormation validation errors with detailed descriptions
- Suggest corrections for common YAML and CloudFormation syntax issues
- Block deployment workflows if template validation fails