#!/bin/bash

# Test script for branch naming validation
# This script tests the same logic used in the GitHub workflow

echo "🧪 Testing Branch Naming Validation"
echo "=================================="

# Test cases
test_cases=(
    "feature/user-authentication:valid"
    "fix/memory-leak-issue:valid"
    "feature/add-dark-mode:valid"
    "fix/database-connection-bug:valid"
    "main:valid"
    "dev:valid"
    "app:valid"
    "beta:valid"
    "user-authentication:invalid"
    "bugfix:invalid"
    "new-feature:invalid"
    "random-branch-name:invalid"
)

valid_count=0
invalid_count=0

for test_case in "${test_cases[@]}"; do
    branch_name="${test_case%:*}"
    expected="${test_case#*:}"

    echo ""
    echo "Testing branch: '$branch_name'"

    # Apply the same validation logic as the workflow
    if [[ "$branch_name" =~ ^(main|dev|app|beta)$ ]]; then
        result="valid"
        reason="protected branch"
    elif [[ "$branch_name" =~ ^feature/ ]] || [[ "$branch_name" =~ ^fix/ ]]; then
        result="valid"
        reason="follows naming convention"
    else
        result="invalid"
        reason="does not follow naming convention"
    fi

    echo "Expected: $expected, Got: $result"

    if [ "$result" = "$expected" ]; then
        echo "✅ PASS - $reason"
        if [ "$result" = "valid" ]; then
            ((valid_count++))
        else
            ((invalid_count++))
        fi
    else
        echo "❌ FAIL - Expected $expected but got $result"
    fi
done

echo ""
echo "📊 Test Results Summary"
echo "======================="
echo "Valid branches: $valid_count"
echo "Invalid branches: $invalid_count"
echo "Total tests: $(($valid_count + $invalid_count))"

if [ $invalid_count -eq 0 ]; then
    echo ""
    echo "🎉 All tests passed! Branch validation logic is working correctly."
    exit 0
else
    echo ""
    echo "⚠️  Some tests failed. Please check the validation logic."
    exit 1
fi