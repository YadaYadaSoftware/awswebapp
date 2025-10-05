## Brief overview
Project-wide requirement to always validate YAML file syntax after any modifications to prevent deployment failures and maintain CI/CD reliability.

## YAML file handling procedure
- Lint all YAML files after any modification using yamllint
- Validate syntax correctness before committing changes
- Apply consistent formatting across all YAML files in the project
- Prevent deployment failures caused by YAML syntax errors

## GitHub Actions workflow requirements
- Run yamllint validation on all modified .yml and .yaml files
- Include .yamllint.yml configuration file for consistent formatting rules
- Fail the validation step if any YAML syntax issues are detected
- Provide clear error messages indicating specific line numbers and issues

## Validation scope
- Apply to all YAML files in the repository including workflows, templates, and configuration files
- Run validation in both local development and CI/CD environments
- Include validation in pre-commit hooks when available
- Check for both syntax errors and formatting inconsistencies

## Error handling approach
- Provide specific error messages with file names and line numbers
- Suggest corrections for common YAML syntax issues
- Include examples of correct YAML formatting in error messages
- Block deployment workflows if YAML validation fails