## ADDED Requirements

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

### Requirement: AWS SES configuration from environment
The system SHALL read AWS SES configuration from environment variables.

#### Scenario: Load SES region from environment
- **WHEN** the application starts
- **THEN** the AWS region for SES is loaded from the `AWS_REGION` environment variable

#### Scenario: Load sender email from environment
- **WHEN** the application starts
- **THEN** the sender email address is loaded from the `AWS_SES_SENDER_EMAIL` environment variable

#### Scenario: Use default configuration if not provided
- **WHEN** environment variables are not set
- **THEN** the application uses configured defaults or throws a startup error
