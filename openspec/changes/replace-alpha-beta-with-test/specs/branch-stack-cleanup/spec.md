## MODIFIED Requirements

### Requirement: Protect shared-infrastructure branches

The system SHALL NOT delete any CloudFormation stack when the deleted branch's leaf name is `app`, `test`, or `dev`. The workflow SHALL exit successfully in this case so that protected-branch deletion (which should never happen but might) does not produce a red CI signal of its own. There SHALL be no `alpha` or `beta` in the protected set.

#### Scenario: Protected branch leaf is detected
- **WHEN** the deleted ref is `dev` (or its leaf segment is `app` or `test`)
- **THEN** the workflow logs a "protected branch — skipping" message and exits with status 0 before any AWS API call is made

#### Scenario: Feature branch nested under a protected name
- **WHEN** the deleted ref is `feature/dev` (leaf segment `dev`)
- **THEN** the workflow treats it as protected and skips deletion, matching the conservative behavior of the deploy job which would have created or updated the shared `dev` stack for this branch
