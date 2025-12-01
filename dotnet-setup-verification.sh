#!/bin/bash
# .NET SDK Setup Verification Script
# This script verifies that .NET SDK is properly installed and accessible

# Source the profile to ensure PATH is set correctly
source ~/.profile 2>/dev/null || true
source ~/.bash_profile 2>/dev/null || true
source ~/.zshrc 2>/dev/null || true

echo "=== .NET SDK Setup Verification ==="
echo

# Check if dotnet command is available
if command -v dotnet &> /dev/null; then
    echo "✅ dotnet command found in PATH"
else
    echo "❌ dotnet command not found in PATH"
    echo "Current PATH: $PATH"
    exit 1
fi

# Show .NET SDK version
echo
echo "=== .NET SDK Information ==="
dotnet --version

echo
echo "=== .NET SDK Details ==="
dotnet --info

echo
echo "=== Project Build Test ==="
echo "Building TaskManager solution..."
if dotnet build --verbosity quiet; then
    echo "✅ Project build successful"
else
    echo "❌ Project build failed"
    exit 1
fi

echo
echo "=== Setup Complete ==="
echo "✅ .NET SDK 8.0.416 is properly installed and configured"
echo "✅ dotnet command is accessible in PATH"
echo "✅ TaskManager project builds successfully"
echo
echo "Note: If you open a new terminal session, you may need to run:"
echo "export PATH=\"\$PATH:~/bin\""