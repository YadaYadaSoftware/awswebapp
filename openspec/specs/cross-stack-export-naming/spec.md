# cross-stack-export-naming Specification

## Purpose
Define the naming contract for every CloudFormation `Export` and `Fn::ImportValue` across the infrastructure templates: export/import names are qualified by both the branch leaf AND the dashed deployment domain so two deployment domains can coexist in one AWS account+region without export-name collision — consistent with the domain-derived naming used for SSM paths, IAM scopes, bucket names, and cluster identifiers. It also governs the phased add-before-remove migration rule that an in-use export is never renamed or removed in place.

## Requirements

### Requirement: Cross-stack export names are qualified by branch leaf AND dashed deployment domain

Every CloudFormation `Export` published by the infrastructure templates SHALL name its export `<Name>-${BranchName}-${DomainDashed}`, where `${DomainDashed}` is the deployment domain in dashed form (`appcloud.systems` → `appcloud-systems`), derived locally in the template from the `DomainName` parameter via `!Join ["-", !Split [".", !Ref DomainName]]`. No template SHALL publish a bare `<Name>-${BranchName}` export once the migration is complete.

Correspondingly, every `Fn::ImportValue` SHALL resolve `<Name>-${EnvironmentToImport}-${DomainDashed}`, deriving `${DomainDashed}` the same way. The `EnvironmentToImport` value and the shared-vs-feature-branch import architecture are unchanged.

This guarantees two deployment domains can coexist in the same AWS account and region without export-name collision, consistent with the domain-derived naming used for SSM paths, IAM scopes, bucket names, and cluster identifiers.

#### Scenario: Two domains coexist in one account/region
- **WHEN** both `appcloud.systems` and a second domain `example.com` deploy a `dev` backend into the same account+region
- **THEN** they publish `DatabaseHost-dev-appcloud-systems` and `DatabaseHost-dev-example-com` respectively, with no collision and no `Export … is already exported by stack …` error

#### Scenario: Consumers resolve the qualified name
- **WHEN** a Web/Api/Dns stack (same-env) or a feature-branch app stack (importing `dev`) renders an `Fn::ImportValue`
- **THEN** the imported name carries the `-${DomainDashed}` suffix and resolves to the publishing backend's domain-qualified export

### Requirement: In-use exports are migrated by phased add-before-remove, never renamed in place

Because CloudFormation rejects any attempt to change, rename, or delete an export while another stack imports it, the migration SHALL proceed in three ordered, individually-consistent phases: (1) add the domain-qualified exports alongside the existing ones; (2) repoint every importer — including all live feature-branch app stacks — to the qualified names; (3) only then remove the original un-qualified exports. No phase SHALL be skipped or reordered, and Phase 3 SHALL NOT be forced past an "export in use" error.

#### Scenario: Removal blocked by a stale importer is not forced
- **WHEN** Phase 3 attempts to remove `DatabaseHost-dev` but a feature-branch app stack still imports it
- **THEN** the deploy fails with "export in use" AND the resolution is to repoint or tear down that stack and retry — NOT to delete the consuming stack's reference forcibly or delete the export out-of-band
