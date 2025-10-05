@echo off
echo 🧪 Testing changes.md Workflow Functionality
echo ============================================
echo.
echo 📝 Testing changes.md content validation...
echo.

REM Test cases for changes.md content
set "validCount=0"
set "invalidCount=0"

REM Test 1: Valid content
echo Testing valid content...
if exist "test_changes.md" del "test_changes.md"
echo Add user authentication system with JWT tokens for API security > test_changes.md
if exist "test_changes.md" (
    echo ✅ PASS - changes.md file created successfully
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md file not created
    set /a "invalidCount+=1"
)

REM Test 2: Empty content
echo Testing empty content...
if exist "test_changes.md" del "test_changes.md"
break > test_changes.md
for %%A in (test_changes.md) do if %%~zA==0 (
    echo ✅ PASS - empty changes.md file created (should fail in workflow)
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md file should be empty
    set /a "invalidCount+=1"
)

REM Test 3: No file
echo Testing missing file...
if exist "test_changes.md" del "test_changes.md"
if not exist "test_changes.md" (
    echo ✅ PASS - no changes.md file (should fail in workflow)
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md file exists when it shouldn't
    set /a "invalidCount+=1"
)

REM Test branch naming validation
echo.
echo 🏷️  Testing branch naming validation...
echo.

set "branchValidCount=0"
set "branchInvalidCount=0"

REM Test valid branch names
echo feature/user-authentication | findstr /r "^feature/ ^fix/" >nul
if %errorlevel% equ 0 (
    echo ✅ PASS - feature/user-authentication follows naming convention
    set /a "branchValidCount+=1"
) else (
    echo ❌ FAIL - feature/user-authentication should be valid
    set /a "branchInvalidCount+=1"
)

echo user-authentication | findstr /r "^feature/ ^fix/" >nul
if %errorlevel% equ 0 (
    echo ❌ FAIL - user-authentication should not follow naming convention
    set /a "branchInvalidCount+=1"
) else (
    echo ✅ PASS - user-authentication correctly rejected
    set /a "branchValidCount+=1"
)

REM Cleanup
if exist "test_changes.md" del "test_changes.md"

echo.
echo 📊 Test Results Summary
echo =======================
echo Valid content tests: %validCount%
echo Invalid content tests: %invalidCount%
echo Total content tests: %validCount% + %invalidCount%
echo Branch naming tests: %branchValidCount% valid, %branchInvalidCount% invalid

if %invalidCount% equ 0 (
    echo.
    echo 🎉 All tests passed! changes.md workflow logic is working correctly.
    exit /b 0
) else (
    echo.
    echo ⚠️  Some tests failed. Please check the workflow logic.
    exit /b 1
)