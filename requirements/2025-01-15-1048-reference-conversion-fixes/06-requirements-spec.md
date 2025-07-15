# Requirements Specification: Reference Conversion Fixes

## Problem Statement

The SdkMigrator is incorrectly converting some assembly references to NuGet package references based solely on assembly name matching, without validating public key tokens. This can result in:
- Wrong packages being referenced (unofficial vs official)
- Lost references when no matching package exists
- Version mismatches between original assembly and selected package
- Custom DLL references being dropped entirely

## Solution Overview

Enhance the reference conversion process to:
1. Validate public key tokens when matching assemblies to packages
2. Preserve exact versions when converting to packages
3. Keep unconverted references as-is in the new project format
4. Provide clear logging about conversion decisions

## Functional Requirements

### FR1: Assembly Identity Extraction
- Extract full assembly identity from reference strings including:
  - Assembly name
  - Version
  - Culture
  - PublicKeyToken
- Store this metadata for use during conversion

### FR2: Public Key Token Validation
- When resolving an assembly to a NuGet package:
  - Extract the public key token from the original reference
  - Verify the package contains an assembly with matching token
  - If tokens don't match, skip conversion and preserve as local reference
  - Log warning about token mismatch

### FR3: Version Preservation
- When converting to PackageReference:
  - Attempt to find package version containing the exact assembly version
  - Use the original assembly version as the package version when possible
  - Fall back to version ranges if exact match unavailable
  - Log when significant version changes occur

### FR4: Reference Preservation
- For references that cannot be converted:
  - Preserve as `<Reference>` items in the new project
  - Include full metadata (Version, Culture, PublicKeyToken)
  - Preserve HintPath if present
  - Do not add XML comments in project file

### FR5: Enhanced Return Type
- AssemblyReferenceConverter should return a result containing:
  - Successfully converted PackageReferences
  - Unconverted Reference items with metadata
  - Conversion warnings/issues

### FR6: Logging Requirements
- Log at appropriate levels:
  - INFO: Successful conversions with version changes
  - WARNING: Token mismatches, missing packages, version conflicts
  - DEBUG: Detailed conversion attempts and decisions

## Technical Requirements

### TR1: Modify AssemblyReferenceConverter
- Change `ExtractAssemblyName()` to return structured assembly identity
- Enhance `ConvertReferencesAsync()` to return both converted and unconverted references
- Add public key token validation logic
- Implement version matching logic

### TR2: Update CleanSdkStyleProjectGenerator
- Add logic to write unconverted `<Reference>` items
- Preserve all reference metadata in output
- Handle both PackageReference and Reference items from converter

### TR3: Enhance Models
- Create `AssemblyIdentity` class with properties:
  - Name, Version, Culture, PublicKeyToken
- Create `ReferenceConversionResult` class containing:
  - PackageReferences collection
  - UnconvertedReferences collection
  - Warnings collection

### TR4: Update NuGetPackageResolver
- Add method to validate assembly identity against package contents
- Support version-specific package resolution
- Return detailed match information

## Implementation Hints

### Pattern to Follow
Look at `MigrateCOMReferences()` in CleanSdkStyleProjectGenerator for how to preserve complex reference metadata.

### Key Files to Modify
1. **Services/AssemblyReferenceConverter.cs**
   - Primary conversion logic
   - Assembly identity extraction
   - Return type changes

2. **Services/CleanSdkStyleProjectGenerator.cs**
   - Reference writing logic
   - Handle unconverted references

3. **Models/** (create new models)
   - AssemblyIdentity
   - ReferenceConversionResult

4. **Services/NuGetPackageResolver.cs**
   - Assembly validation methods
   - Version-specific resolution

## Acceptance Criteria

### AC1: Token Validation
- Given a reference with PublicKeyToken
- When converting to package reference
- Then verify the package contains assembly with matching token
- And skip conversion if tokens don't match

### AC2: Version Preservation
- Given a reference with specific version
- When converting to package reference
- Then use the same version for the package
- And log if exact version unavailable

### AC3: Reference Preservation
- Given a reference that cannot be converted
- When generating new project
- Then preserve it as a Reference item
- And maintain all original metadata

### AC4: No Data Loss
- Given any valid legacy project
- When migrating references
- Then no references should be lost
- And all should be either converted or preserved

### AC5: Clear Diagnostics
- Given any reference conversion
- When issues occur
- Then log clear warnings
- And provide actionable information

## Assumptions

1. Focus is on .NET Framework projects (SDK-implicit references less critical)
2. Performance impact of token validation is acceptable
3. Users prefer safety over aggressive conversion
4. Existing behavior for successfully converted references remains unchanged
5. Build logs are the primary diagnostic tool (not XML comments)