# Context Findings

## Current Implementation Analysis

### 1. Reference Conversion Flow
The main conversion happens in `AssemblyReferenceConverter.cs`:
- **Input**: Legacy project with `<Reference>` items
- **Process**: Extracts assembly names, attempts to find matching NuGet packages
- **Output**: Collection of `PackageReference` objects

### 2. Critical Implementation Gaps

#### A. Public Key Token Validation Missing
- `ExtractAssemblyName()` (line 215) strips all metadata including PublicKeyToken
- No validation that the resolved package contains the correctly signed assembly
- Can lead to wrong package substitution (e.g., unofficial vs official packages)

#### B. Lost References
- `CleanSdkStyleProjectGenerator` only adds successfully converted references
- References that can't be converted to packages are completely dropped
- No fallback mechanism to preserve them as `<Reference>` items

#### C. Version Mismatch
- Original assembly version is discarded
- Package version is resolved independently using latest stable
- No attempt to match the original assembly version

### 3. Specific Code Locations

#### Files That Need Modification:
1. **Services/AssemblyReferenceConverter.cs**
   - `ExtractAssemblyName()` method needs to preserve metadata
   - `ConvertReferencesAsync()` needs to return unconverted references
   - Need to add PublicKeyToken validation

2. **Services/CleanSdkStyleProjectGenerator.cs**
   - Missing logic to preserve unconverted references
   - No handling for custom DLL references with HintPath

3. **Models/PackageReference.cs** (needs checking)
   - May need additional properties for version matching

4. **Services/NuGetPackageResolver.cs**
   - Resolution logic doesn't consider assembly metadata
   - Needs PublicKeyToken aware matching

### 4. Existing Patterns to Follow

#### COM Reference Handling (Good Pattern)
In `CleanSdkStyleProjectGenerator.MigrateCOMReferences()`:
- Preserves all metadata (Guid, VersionMajor, etc.)
- Writes complete reference information to new project

#### Built-in Framework Assembly List
`AssemblyReferenceConverter` has comprehensive list (lines 15-101) but:
- Only checks assembly name, not full identity
- Doesn't handle SDK-implicit references properly

### 5. Technical Constraints

#### .NET Framework Focus
- Primary target is legacy .NET Framework apps
- SDK-implicit references less critical for now
- Must preserve exact reference specifications

#### Backward Compatibility
- Must not break existing migrations
- Should add warnings without failing the process
- Preserve all existing functionality

### 6. Integration Points

#### Logging System
- Already has structured logging via ILogger
- Can add warnings for conversion issues
- Should log when PublicKeyToken mismatch detected

#### Validation System
- `PostMigrationValidator` could check for missing references
- Add validation for unconverted references

## Key Implementation Requirements

### 1. Enhanced Reference Model
Need to capture:
- AssemblyName
- Version
- Culture
- PublicKeyToken
- OriginalInclude (full reference string)

### 2. Conversion Result Model
Should include:
- Successfully converted PackageReferences
- Unconverted References (with metadata)
- Warnings/issues encountered

### 3. Reference Preservation Logic
For unconverted references:
- Keep as `<Reference>` with Include attribute
- Preserve HintPath if present
- Add comment explaining why not converted

### 4. Version Matching
When converting to package:
- Try to find package version matching assembly version
- Fall back to version range if exact match not found
- Log when version changes significantly

### 5. PublicKeyToken Validation
- Extract token from original reference
- Verify package contains assembly with matching token
- Warn or skip conversion if tokens don't match