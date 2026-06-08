# Design — domain-qualified-stack-exports

## D1. The constraint that forces a phased migration

CloudFormation will not let you **change the value of, rename, or delete** an export while another stack imports it:

> `Cannot update export <Name> as it is in use by <stack…>` (on value change or removal)
> `Export <Name> cannot be updated as it is in use` (on rename — a rename is a remove + add)

A rename is therefore not a single edit. The only safe path is to introduce the new name **before** retiring the old one, with importers cut over in between. Hence three ordered waves:

```
Phase 1 (ADD):     backend exports BOTH  <Name>-<branch>  AND  <Name>-<branch>-<domain>   (same value)
Phase 2 (REPOINT): every Fn::ImportValue switches  <Name>-…  ->  <Name>-…-<domain>
Phase 3 (REMOVE):  backend drops the old  <Name>-<branch>  export
```

Each phase leaves the system fully consistent:
- After P1, both names resolve; nothing imports the new one yet — pure addition.
- After P2, every consumer reads the new name; the old name is exported-but-unused.
- After P3, the old name is gone and nothing references it.

A failure or pause between phases is safe — you can sit at P1 or P2 indefinitely.

## D2. Deriving the domain suffix (no new parameter)

Every template that exports/imports already receives `DomainName` in dot form (`appcloud.systems`). The dashed form is derived locally, exactly as the existing convention does elsewhere:

```yaml
- DomainDashed: !Join ["-", !Split [".", !Ref DomainName]]
```

So an export becomes:

```yaml
Export:
  Name: !Sub
    - "DatabaseHost-${BranchName}-${DomainDashed}"
    - DomainDashed: !Join ["-", !Split [".", !Ref DomainName]]
```

and an importer:

```yaml
Fn::ImportValue: !Sub
  - "DatabaseHost-${EnvironmentToImport}-${DomainDashed}"
  - DomainDashed: !Join ["-", !Split [".", !Ref DomainName]]
```

No change to `EnvironmentToImport` plumbing (master → backend `!Ref BranchName`; application → nested `!Ref EnvironmentToImport`). Only the literal string gains a suffix.

## D3. The two import audiences

`Fn::ImportValue` consumers fall into two groups, both must be repointed in Phase 2:

1. **Same-env consumers** (master.template path): Web/Api/Dns importing their own backend's exports, `EnvironmentToImport = BranchName`.
2. **Feature-branch app stacks** (application.template path): every `{type}/{name}` branch's app stack imports **dev's** backend exports (`EnvironmentToImport = dev`). These are separate live CloudFormation stacks. Phase 1 must be deployed to dev's backend **before** any feature branch is redeployed against the new names, and Phase 3 must not run until **all** feature-branch app stacks have been redeployed past Phase 2 (or torn down). This is the riskiest ordering dependency — a stale feature stack still importing the old name will block Phase 3's removal.

## D4. Export inventory (~33 names)

| Template | Exports |
|---|---|
| db.template | AuroraClusterId, DatabaseEndpoint, DatabaseReaderEndpoint, DatabaseUsername, DatabaseEngine, **DatabaseHost**, DatabasePort, DatabaseName, DatabaseClusterArn, DatabasePasswordSecretArn, AuroraGlobalClusterId |
| network.template | VPCId, PrivateSubnet1Id, PrivateSubnet2Id, PublicSubnet1Id, PublicSubnet2Id, LambdaSecurityGroupId, ALBSecurityGroupId, ECSTaskSecurityGroupId, RDSSecurityGroupId, VpcFlowLogsRoleArn |
| web.template | WebEndpoint, WebLoadBalancerDNS, WebLoadBalancerHostedZoneId, WebTaskDefinition, WebServiceName |
| infrastructure.template | ECSClusterName, GoogleOAuthSecretArn |
| api.template | ApiEndpoint, ApiLambdaFunctionName |
| security.template | SharedLambdaRoleArn |

Importers: api.template (8), web.template (14), dns.template (5), db.template (3).

## D5. Alternatives considered

- **Single rename (rejected):** impossible while imported — the core constraint above.
- **Blue/green via a parallel export set kept forever (rejected):** leaves dead exports; the whole point is convention cleanliness.
- **Scope by region only, ignore cross-domain collisions (rejected):** that's the status quo; it silently breaks the second domain — the exact property this repo claims to have.
