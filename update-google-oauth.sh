#!/bin/bash

# Script to update Google OAuth redirect URIs for production deployment
# Run this after deploying to AWS to get the correct API Gateway URL

API_ENDPOINT=${1}
WEB_ENDPOINT=${2}

if [ -z "$API_ENDPOINT" ]; then
  echo "Usage: $0 <api-endpoint> [web-endpoint]"
  echo "Example: $0 https://abc123.execute-api.us-east-1.amazonaws.com/Prod https://myapp.example.com"
  exit 1
fi

echo "🔍 Using API Gateway URL: $API_ENDPOINT"
if [ -n "$WEB_ENDPOINT" ]; then
  echo "🔍 Using Web Application URL: $WEB_ENDPOINT"
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
if [ -n "$WEB_ENDPOINT" ]; then
  # Extract domain from web endpoint
  WEB_DOMAIN=$(echo $WEB_ENDPOINT | sed 's|https://||' | sed 's|http://||' | sed 's|/.*||')
  echo "Authorized JavaScript origins:"
  echo "  https://$WEB_DOMAIN"
else
  echo "Authorized JavaScript origins:"
  echo "  https://$API_GATEWAY_ID.execute-api.us-east-1.amazonaws.com"
fi
echo ""
echo "🔗 Google Cloud Console: https://console.cloud.google.com/apis/credentials"
echo "   → Select your OAuth 2.0 Client ID"
echo "   → Add the URIs listed above"
echo ""
echo "🎉 After updating Google Console, try logging in again!"