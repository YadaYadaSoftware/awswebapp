<#
.SYNOPSIS
    TaskManager Database Connection Script for Windows
    Sets up SSH tunnel or SSM port forwarding and connects to Aurora MySQL database

.DESCRIPTION
    This script automates the process of connecting to your TaskManager Aurora MySQL Global Database
    through the bastion host. It supports both SSH tunnels and AWS Systems Manager port forwarding.

.PARAMETER StackName
    CloudFormation stack name (default: taskmanager-regional-infrastructure-us-east-1)

.PARAMETER KeyPath
    Path to your SSH private key file (optional if using -UseSSM)

.PARAMETER Database
    Database name to connect to (default: taskmanager)

.PARAMETER OpenClient
    Open database client after setting up tunnel (default: false)

.PARAMETER UseSSM
    Use AWS Systems Manager port forwarding instead of SSH (default: false)

.EXAMPLE
    # Using SSH tunnel
    .\Connect-TaskManagerDB.ps1 -KeyPath "C:\path\to\your\key.pem"

.EXAMPLE
    # Using SSM port forwarding (no SSH key needed)
    .\Connect-TaskManagerDB.ps1 -UseSSM

.EXAMPLE
    # Connect to specific database and open client
    .\Connect-TaskManagerDB.ps1 -UseSSM -Database "taskmanager_main" -OpenClient
#>

param(
    [string]$StackName = "taskmanager-regional-infrastructure-us-east-1",
    [string]$KeyPath,
    [string]$Database = "taskmanager",
    [switch]$OpenClient,
    [switch]$UseSSM
)

# Configuration
$REGION = "us-east-1"
$LOCAL_PORT = 3306
$REMOTE_PORT = 3306
$DB_USER = "taskmanager_admin"

# Colors for output
$Green = "Green"
$Yellow = "Yellow"
$Red = "Red"
$Cyan = "Cyan"

function Write-ColorOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Test-Prerequisites {
    Write-ColorOutput "[INFO] Checking prerequisites..." $Cyan

    # Check AWS CLI
    try {
        $awsVersion = aws --version 2>$null
        Write-ColorOutput "[OK] AWS CLI found: $awsVersion" $Green
    } catch {
        Write-ColorOutput "[ERROR] AWS CLI not found. Please install from: https://aws.amazon.com/cli/" $Red
        exit 1
    }

    # Check if using SSH key or SSM
    if ($KeyPath) {
        # Check SSH
        try {
            $sshVersion = ssh -V 2>&1
            Write-ColorOutput "[OK] SSH client found" $Green
        } catch {
            Write-ColorOutput "[ERROR] SSH client not found. Please install OpenSSH" $Red
            exit 1
        }

        # Check key file
        if (!(Test-Path $KeyPath)) {
            Write-ColorOutput "[ERROR] SSH key file not found: $KeyPath" $Red
            Write-ColorOutput "[TIP] Try using -UseSSM instead for passwordless connection" $Yellow
            exit 1
        }
    } else {
        Write-ColorOutput "[INFO] No SSH key provided - will use AWS Systems Manager" $Cyan
    }

    Write-ColorOutput "[OK] All prerequisites met!" $Green
}

function Get-ConnectionDetails {
    Write-ColorOutput "`n[INFO] Getting connection details from CloudFormation..." $Cyan

    try {
        # Get bastion host IP
        $bastionIP = aws cloudformation describe-stacks `
            --stack-name $StackName `
            --query 'Stacks[0].Outputs[?OutputKey==`BastionHostIP`].OutputValue' `
            --output text `
            --region $REGION

        if ($LASTEXITCODE -ne 0 -or $bastionIP -eq "None") {
            throw "Failed to get bastion IP"
        }

        # Get database endpoint
        $dbEndpoint = aws cloudformation describe-stacks `
            --stack-name $StackName `
            --query 'Stacks[0].Outputs[?OutputKey==`DatabaseEndpoint`].OutputValue' `
            --output text `
            --region $REGION

        if ($LASTEXITCODE -ne 0 -or $dbEndpoint -eq "None") {
            throw "Failed to get database endpoint"
        }

        Write-ColorOutput "[OK] Bastion IP: $bastionIP" $Green
        Write-ColorOutput "[OK] Database Endpoint: $dbEndpoint" $Green

        return @{
            BastionIP = $bastionIP
            DBEndpoint = $dbEndpoint
        }
    } catch {
        Write-ColorOutput "[ERROR] Failed to get connection details: $($_.Exception.Message)" $Red
        exit 1
    }
}

function Get-DatabasePassword {
    Write-ColorOutput "`n[INFO] Getting database password from Secrets Manager..." $Cyan

    try {
        $secretValue = aws secretsmanager get-secret-value `
            --secret-id "taskmanager/database/shared" `
            --query 'SecretString' `
            --output text `
            --region $REGION

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to get secret"
        }

        $secretObj = $secretValue | ConvertFrom-Json
        $password = $secretObj.password

        Write-ColorOutput "[OK] Database password retrieved" $Green
        return $password
    } catch {
        Write-ColorOutput "[ERROR] Failed to get database password: $($_.Exception.Message)" $Red
        Write-ColorOutput "   You can also get it manually from AWS Secrets Manager" $Yellow
        return $null
    }
}

function Setup-SSHTunnel {
    param($ConnectionDetails, $Password)

    if ($UseSSM -or !$KeyPath) {
        Write-ColorOutput "`n[INFO] Setting up SSM port forwarding..." $Cyan

        # Get bastion instance ID
        $instanceId = aws ec2 describe-instances `
            --filters "Name=tag:Name,Values=TaskManager-Bastion" "Name=instance-state-name,Values=running" `
            --query 'Reservations[0].Instances[0].InstanceId' `
            --output text `
            --region $REGION

        if ($LASTEXITCODE -ne 0 -or $instanceId -eq "None") {
            Write-ColorOutput "[ERROR] Could not find running bastion instance" $Red
            exit 1
        }

        Write-ColorOutput "[OK] Found bastion instance: $instanceId" $Green

        # Start SSM port forwarding
        $ssmCommand = "aws ssm start-session --target $instanceId --document-name AWS-StartPortForwardingSession --parameters portNumber='3306',localPortNumber='$LOCAL_PORT' --region $REGION"

        Write-ColorOutput "[CMD] SSM command:" $Yellow
        Write-ColorOutput "   $ssmCommand" $Yellow
        Write-ColorOutput "" $Yellow

        Write-ColorOutput "[INFO] Starting SSM port forwarding..." $Green
        Write-ColorOutput "   Local port: $LOCAL_PORT" $Green
        Write-ColorOutput "   Remote: $($ConnectionDetails.DBEndpoint):$REMOTE_PORT" $Green
        Write-ColorOutput "" $Green

        # Start SSM session in background
        $tunnelProcess = Start-Process -FilePath "aws" `
            -ArgumentList "ssm", "start-session", "--target", $instanceId, "--document-name", "AWS-StartPortForwardingSession", "--parameters", "portNumber='3306',localPortNumber='$LOCAL_PORT'", "--region", $REGION `
            -NoNewWindow `
            -PassThru

        Start-Sleep -Seconds 3

        if (!$tunnelProcess.HasExited) {
            Write-ColorOutput "[OK] SSM port forwarding established!" $Green
            Write-ColorOutput "   Process ID: $($tunnelProcess.Id)" $Green
        } else {
            Write-ColorOutput "[ERROR] Failed to establish SSM port forwarding" $Red
            Write-ColorOutput "[INFO] This is likely due to missing AWS Session Manager plugin" $Yellow
            Write-ColorOutput "[TIP] Install from: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html" $Cyan
            Write-ColorOutput "[TIP] Or try using SSH: .\Connect-TaskManagerDB.ps1 -KeyPath 'C:\path\to\key.pem'" $Cyan
            exit 1
        }

    } else {
        Write-ColorOutput "`n[INFO] Setting up SSH tunnel..." $Cyan

        $tunnelCommand = "ssh -i `"$KeyPath`" -L ${LOCAL_PORT}:$($ConnectionDetails.DBEndpoint):${REMOTE_PORT} ec2-user@$($ConnectionDetails.BastionIP) -N"

        Write-ColorOutput "[CMD] Tunnel command:" $Yellow
        Write-ColorOutput "   $tunnelCommand" $Yellow
        Write-ColorOutput "" $Yellow

        # Test SSH connection first
        Write-ColorOutput "[INFO] Testing SSH connection..." $Cyan
        $testCommand = "ssh -i `"$KeyPath`" -o ConnectTimeout=10 ec2-user@$($ConnectionDetails.BastionIP) `"echo 'SSH connection successful'`""

        try {
            $testResult = Invoke-Expression $testCommand 2>$null
            if ($LASTEXITCODE -eq 0) {
                Write-ColorOutput "[OK] SSH connection successful" $Green
            } else {
                throw "SSH test failed"
            }
        } catch {
            Write-ColorOutput "[ERROR] SSH connection failed. Please check:" $Red
            Write-ColorOutput "   - SSH key path: $KeyPath" $Red
            Write-ColorOutput "   - Bastion IP: $($ConnectionDetails.BastionIP)" $Red
            Write-ColorOutput "   - Security group allows SSH from your IP" $Red
            Write-ColorOutput "[TIP] Try using -UseSSM for passwordless connection" $Yellow
            exit 1
        }

        # Start tunnel in background
        Write-ColorOutput "[INFO] Starting SSH tunnel..." $Green
        Write-ColorOutput "   Local port: $LOCAL_PORT" $Green
        Write-ColorOutput "   Remote: $($ConnectionDetails.DBEndpoint):$REMOTE_PORT" $Green
        Write-ColorOutput "" $Green

        # Start tunnel process
        $tunnelProcess = Start-Process -FilePath "ssh" `
            -ArgumentList "-i", "`"$KeyPath`"", "-L", "${LOCAL_PORT}:$($ConnectionDetails.DBEndpoint):${REMOTE_PORT}", "ec2-user@$($ConnectionDetails.BastionIP)", "-N" `
            -NoNewWindow `
            -PassThru

        Start-Sleep -Seconds 2

        if (!$tunnelProcess.HasExited) {
            Write-ColorOutput "[OK] SSH tunnel established!" $Green
            Write-ColorOutput "   Process ID: $($tunnelProcess.Id)" $Green
        } else {
            Write-ColorOutput "[ERROR] Failed to establish SSH tunnel" $Red
            exit 1
        }
    }

    return $tunnelProcess
}

function Show-ConnectionInfo {
    param($ConnectionDetails, $Password)

    Write-ColorOutput "`n[INFO] Database Connection Information" $Cyan
    Write-ColorOutput "==================================================" $Cyan

    Write-ColorOutput "Host: localhost" $Green
    Write-ColorOutput "Port: $LOCAL_PORT" $Green
    Write-ColorOutput "Database: $Database" $Green
    Write-ColorOutput "Username: $DB_USER" $Green
    if ($Password) {
        Write-ColorOutput "Password: $Password" $Green
    } else {
        Write-ColorOutput "Password: [Get from AWS Secrets Manager]" $Yellow
    }

    Write-ColorOutput "`n[INFO] Connection Commands:" $Cyan

    Write-ColorOutput "mysql command:" $Yellow
    Write-ColorOutput "  mysql -h localhost -P $LOCAL_PORT -u $DB_USER -p -D $Database" $Yellow

    Write-ColorOutput "`nMySQL Workbench/DBeaver:" $Yellow
    Write-ColorOutput "  Host: localhost" $Yellow
    Write-ColorOutput "  Port: $LOCAL_PORT" $Yellow
    Write-ColorOutput "  Database: $Database" $Yellow
    Write-ColorOutput "  Username: $DB_USER" $Yellow

    Write-ColorOutput "`n[INFO] Useful Queries:" $Cyan
    Write-ColorOutput "  SHOW DATABASES;      # List all databases" $Yellow
    Write-ColorOutput "  USE $Database;       # Connect to database" $Yellow
    Write-ColorOutput "  SHOW TABLES;         # List tables" $Yellow
    Write-ColorOutput "  SELECT * FROM Users; # View users" $Yellow
}

function Open-DatabaseClient {
    param($Password)

    if (!$OpenClient) { return }

    Write-ColorOutput "`n[INFO] Opening database client..." $Cyan

    # Try to open MySQL Workbench if installed
    $mysqlWorkbenchPaths = @(
        "C:\Program Files\MySQL\MySQL Workbench 8.0 CE\MySQLWorkbench.exe",
        "C:\Program Files (x86)\MySQL\MySQL Workbench 8.0 CE\MySQLWorkbench.exe",
        "${env:ProgramFiles}\MySQL\MySQL Workbench 8.0 CE\MySQLWorkbench.exe"
    )

    foreach ($path in $mysqlWorkbenchPaths) {
        if (Test-Path $path) {
            Write-ColorOutput "[OK] Found MySQL Workbench, opening..." $Green
            Start-Process $path
            return
        }
    }

    # Try to open DBeaver
    $dbeaverPaths = @(
        "C:\Program Files\DBeaver\dbeaver.exe",
        "${env:ProgramFiles}\DBeaver\dbeaver.exe"
    )

    foreach ($path in $dbeaverPaths) {
        if (Test-Path $path) {
            Write-ColorOutput "[OK] Found DBeaver, opening..." $Green
            Start-Process $path
            return
        }
    }

    Write-ColorOutput "[INFO] No database client found. Please open your preferred client manually." $Yellow
    Write-ColorOutput "   Use the connection details above to configure your client." $Yellow
}

# Main execution
Write-ColorOutput "[START] TaskManager Database Connection Script" $Cyan
Write-ColorOutput "==================================================" $Cyan

try {
    # Run all steps
    Test-Prerequisites
    $connectionDetails = Get-ConnectionDetails
    $password = Get-DatabasePassword
    $tunnelProcess = Setup-SSHTunnel -ConnectionDetails $connectionDetails -Password $password
    Show-ConnectionInfo -ConnectionDetails $connectionDetails -Password $password
    Open-DatabaseClient -Password $password

    Write-ColorOutput "`n[SUCCESS] Setup complete!" $Green
    Write-ColorOutput "   Tunnel is running in the background (PID: $($tunnelProcess.Id))" $Green
    Write-ColorOutput "   Press Ctrl+C to stop the tunnel when done" $Green
    Write-ColorOutput "`n[TIP] Keep this PowerShell window open to maintain the tunnel" $Cyan

    # Wait for user input to keep tunnel alive
    Write-ColorOutput "`n[WAIT] Press Enter to stop the tunnel and exit..." $Yellow
    Read-Host

} catch {
    Write-ColorOutput "[ERROR] Script failed: $($_.Exception.Message)" $Red
    exit 1
} finally {
    # Cleanup
    if ($tunnelProcess -and !$tunnelProcess.HasExited) {
        Write-ColorOutput "`n[CLEANUP] Cleaning up tunnel..." $Cyan
        Stop-Process -Id $tunnelProcess.Id -Force
        Write-ColorOutput "[OK] Tunnel stopped" $Green
    }
}