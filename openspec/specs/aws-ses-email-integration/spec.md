# aws-ses-email-integration Specification

## Purpose
TBD - created by archiving change implement-aws-email-confirmation. Update Purpose after archive.
## Requirements
### Requirement: AWS SES email sending service
The system SHALL provide an abstraction layer for sending emails via AWS Simple Email Service (SES).

#### Scenario: Send email via AWS SES
- **WHEN** the application calls `IEmailService.SendEmailAsync()` with a valid email request
- **THEN** the email is sent through AWS SES and the method returns success

#### Scenario: Handle SES connection failure
- **WHEN** AWS SES is unreachable or credentials are invalid
- **THEN** the service throws an exception with details about the SES error

### Requirement: Email service dependency injection
The system SHALL register the email service in the application's dependency injection container.

#### Scenario: Resolve email service from DI container
- **WHEN** a controller or service requests `IEmailService` from the DI container
- **THEN** an instance of `AwsSesEmailService` is provided with AWS SES configured

### Requirement: Email request structure
The system SHALL accept email requests with To, Subject, HTML body, and plain text body.

#### Scenario: Send email with full content
- **WHEN** an email request includes recipient, subject, HTML content, and plain text
- **THEN** all content is preserved and sent to the recipient

#### Scenario: Send email with HTML body only
- **WHEN** an email request includes only HTML body without plain text
- **THEN** the system sends the email with HTML body and empty plain text body

### Requirement: AWS SES configuration from the `AwsSes` options section
The system SHALL read AWS SES configuration from the `AwsSes` configuration section, bound to `AwsSesOptions` via `IOptions<>`. The section may be supplied by environment variables (`AwsSes__Region`, `AwsSes__SenderEmail`), `appsettings.json`, or user-secrets.

#### Scenario: Load SES region from configuration
- **WHEN** the application starts
- **THEN** the AWS region for SES is read from `AwsSes:Region` (env var `AwsSes__Region`); in deployed envs this is left empty so the AWS SDK auto-detects the region from Fargate task metadata

#### Scenario: Load sender email from configuration
- **WHEN** the application starts
- **THEN** the sender email address is read from `AwsSes:SenderEmail` (env var `AwsSes__SenderEmail`)

#### Scenario: Use default configuration if not provided
- **WHEN** environment variables are not set
- **THEN** the application uses configured defaults or throws a startup error

