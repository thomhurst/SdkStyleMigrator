# Discovery Questions for SdkMigrator Analysis

## Q1: Are you currently experiencing specific migration failures or errors with certain project types?
**Default if unknown:** Yes (most users encounter issues with complex legacy projects)

## Q2: Do you need better support for migrating projects with external build tools or custom MSBuild extensions?
**Default if unknown:** Yes (many enterprise projects have custom build processes)

## Q3: Are your users primarily migrating from .NET Framework to modern .NET (5+)?
**Default if unknown:** Yes (this is the most common migration path)

## Q4: Do you need the tool to handle multi-targeting projects (projects that target multiple frameworks)?
**Default if unknown:** Yes (many libraries need to support both legacy and modern frameworks)

## Q5: Would automatic rollback and recovery features be valuable for failed migrations?
**Default if unknown:** Yes (migrations can fail partway through large solutions)