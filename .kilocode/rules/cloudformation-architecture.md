## Brief overview
Project-specific guidelines for CloudFormation infrastructure architecture, fully embracing Export/ImportValue for maximum reusability and interconnected infrastructure.

## CloudFormation stack communication
- **All CloudFormation outputs MUST be exported** using `Export` with branch and region-specific names
- **All CloudFormation references MUST use `Fn::ImportValue`** instead of `!GetAtt` or parameters
- Use **branch and region-specific export names** (e.g., `taskmanager-${BranchName}-ResourceName-${AWS::Region}`) to avoid namespace conflicts
- This approach creates a fully interconnected infrastructure with implicit dependencies and maximum reusability

## Infrastructure deployment pattern
- Deploy infrastructure stacks with full Export/ImportValue integration
- Feature branches share dev infrastructure through imports
- All cross-stack references use Fn::ImportValue for clean, implicit dependencies
- This pattern enables seamless resource sharing across branches and regions

## Stack output usage
- All outputs are exported for potential reuse by other stacks
- Use Fn::ImportValue for all inter-stack references
- Embrace inter-stack dependencies through exports for better infrastructure cohesion
- Infrastructure is highly interconnected and reusable

## Deployment workflow
- Infrastructure stacks export all outputs for cross-stack availability
- Application stacks import required infrastructure resources
- Full Export/ImportValue usage within and across CloudFormation templates
- Dependencies managed through CloudFormation's native import/export mechanism