#!/bin/bash
# Cleanup script for failed CloudFormation stacks

STACK_NAME="taskmanager-prod"
REGION="us-east-1"

echo "🧹 Checking for existing stack: $STACK_NAME"

# Check if stack exists and get its status
STACK_STATUS=$(aws cloudformation describe-stacks \
  --stack-name $STACK_NAME \
  --region $REGION \
  --query 'Stacks[0].StackStatus' \
  --output text 2>/dev/null || echo "STACK_NOT_FOUND")

echo "Stack status: $STACK_STATUS"

if [ "$STACK_STATUS" = "ROLLBACK_COMPLETE" ] || [ "$STACK_STATUS" = "CREATE_FAILED" ] || [ "$STACK_STATUS" = "DELETE_FAILED" ]; then
    echo "🗑️  Deleting failed stack: $STACK_NAME"
    aws cloudformation delete-stack \
      --stack-name $STACK_NAME \
      --region $REGION
    
    echo "⏳ Waiting for stack deletion to complete..."
    aws cloudformation wait stack-delete-complete \
      --stack-name $STACK_NAME \
      --region $REGION
    
    echo "✅ Stack deleted successfully"
elif [ "$STACK_STATUS" = "STACK_NOT_FOUND" ]; then
    echo "✅ No existing stack found - ready for deployment"
else
    echo "⚠️  Stack exists with status: $STACK_STATUS"
    echo "Manual intervention may be required"
fi