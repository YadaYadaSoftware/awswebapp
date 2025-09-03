#!/bin/bash

# Script to update Google OAuth redirect URIs for production deployment
# Run this after deploying to AWS to get the correct API Gateway URL

echo "🔍 Getting API Gateway URL from CloudFormation..."

API_ENDPOINT=$(aws cloudformation describe-stacks \
  --stack-name taskmanager-main \
  --query 'Stacks[0].Outputs[?OutputKey==`ApiEndpoint`].OutputValue' \
  --output text)

if [ -z "$API_ENDPOINT" ] || [ "$API_ENDPOINT" = "None" ]; then
  echo "❌ Failed to get API Gateway URL. Make sure the stack is deployed."
  exit 1
fi

# Extract the API Gateway ID from the URL
API_GATEWAY_ID=$(echo $API_ENDPOINT | sed 's|https://||' | sed 's|\.execute-api.*||')

echo "✅ API Gateway URL: $API_ENDPOINT"
echo "✅ API Gateway ID: $API_GATEWAY_ID"
echo ""
echo "📋 Add these to Google Cloud Console:"
echo ""
echo "Authorized redirect URIs:"
echo "  https://$API_GATEWAY_ID.execute-api.us-east-1.amazonaws.com/Prod/signin-google"
echo ""
echo "Authorized JavaScript origins:"
echo "  https://$API_GATEWAY_ID.execute-api.us-east-1.amazonaws.com"
echo ""
echo "🔗 Google Cloud Console: https://console.cloud.google.com/apis/credentials"
echo "   → Select your OAuth 2.0 Client ID"
echo "   → Add the URIs listed above"
echo ""
echo "🎉 After updating Google Console, try logging in again!"