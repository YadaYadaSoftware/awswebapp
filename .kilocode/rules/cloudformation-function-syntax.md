## Brief overview
Guidelines for proper CloudFormation function syntax, particularly when using ImportValue with Sub functions to avoid parsing errors.

## ImportValue function syntax
- Always use the full function name `Fn::ImportValue` when the value contains a `!Sub` function
- Avoid using the short form `!ImportValue` when it contains nested short-form functions
- Use the full function name format for better compatibility and to prevent syntax errors

## Correct usage patterns
- Use `Fn::ImportValue` with `!Sub` for parameter references that need string interpolation
- Apply this rule specifically when ImportValue contains Sub function calls
- Maintain consistent function naming in CloudFormation templates

## Error prevention
- Prevents CloudFormation parsing errors when using nested functions
- Ensures compatibility across different CloudFormation template formats
- Maintains readability and explicit function declarations in infrastructure code