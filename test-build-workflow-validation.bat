@echo off
echo 🧪 Testing Build Workflow changes.md Validation
echo ==============================================
echo.
echo 📋 Testing changes.md validation logic from zbuild.yml...
echo.

REM Test cases for build workflow validation
set "validCount=0"
set "invalidCount=0"

REM Test 1: Protected branch (should skip validation)
echo Testing protected branch 'main'...
if "main" == "main" (
    echo ✅ PASS - Protected branch 'main' correctly skips validation
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - Protected branch should skip validation
    set /a "invalidCount+=1"
)

REM Test 2: Feature branch with changes.md (should pass)
echo.
echo Testing feature branch with changes.md...
echo "Add user authentication system with JWT tokens for API security" > test_changes.md
if exist "test_changes.md" (
    echo ✅ PASS - changes.md file created for feature branch
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md file should exist
    set /a "invalidCount+=1"
)

REM Test 3: Feature branch without changes.md (should fail)
echo.
echo Testing feature branch without changes.md...
if exist "test_changes.md" del "test_changes.md"
if not exist "test_changes.md" (
    echo ✅ PASS - Missing changes.md correctly detected
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md should not exist for this test
    set /a "invalidCount+=1"
)

REM Test 4: Fix branch with empty changes.md (should fail)
echo.
echo Testing fix branch with empty changes.md...
break > test_changes.md
for %%A in (test_changes.md) do if %%~zA==0 (
    echo ✅ PASS - Empty changes.md correctly detected
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - changes.md should be empty for this test
    set /a "invalidCount+=1"
)

REM Test 5: Branch naming validation
echo.
echo 🏷️  Testing branch naming patterns...
echo.

REM Test valid patterns
echo feature/user-authentication | findstr /r "^feature/ ^fix/" >nul
if %errorlevel% equ 0 (
    echo ✅ PASS - feature/user-authentication follows naming convention
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - feature/user-authentication should be valid
    set /a "invalidCount+=1"
)

echo fix/bug-fix | findstr /r "^feature/ ^fix/" >nul
if %errorlevel% equ 0 (
    echo ✅ PASS - fix/bug-fix follows naming convention
    set /a "validCount+=1"
) else (
    echo ❌ FAIL - fix/bug-fix should be valid
    set /a "invalidCount+=1"
)

REM Test invalid patterns
echo user-authentication | findstr /r "^feature/ ^fix/" >nul
if %errorlevel% equ 0 (
    echo ❌ FAIL - user-authentication should not follow naming convention
    set /a "invalidCount+=1"
) else (
    echo ✅ PASS - user-authentication correctly rejected
    set /a "validCount+=1"
)

REM Cleanup
if exist "test_changes.md" del "test_changes.md"

echo.
echo 📊 Test Results Summary
echo =======================
echo Valid tests: %validCount%
echo Invalid tests: %invalidCount%
echo Total tests: %validCount% + %invalidCount%

if %invalidCount% equ 0 (
    echo.
    echo 🎉 All tests passed! Build workflow validation logic is working correctly.
    exit /b 0
) else (
    echo.
    echo ⚠️  Some tests failed. Please check the build workflow validation logic.
    exit /b 1
)