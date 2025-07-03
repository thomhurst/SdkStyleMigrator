# SdkMigrator Requirements Specification

## Problem Statement

The SdkMigrator tool helps convert legacy MSBuild projects to SDK-style format. While it handles basic migrations well, users need enhanced support for:
1. **Complex build scenarios** with custom MSBuild extensions
2. **Multi-targeting projects** that need to support multiple frameworks
3. **Performance optimization** for large enterprise solutions
4. **.NET Framework-focused migrations** (staying on .NET Framework, not migrating to .NET Core/5+)

## Functional Requirements

### FR1: Enhanced Custom MSBuild Support
- **Current State**: Custom Import statements are removed during migration
- **Requirement**: Intelligent handling of custom build logic
  - Analyze custom .targets/.props files to determine if they're truly needed
  - Provide warnings when removing imports that may contain custom logic
  - Document which imports were removed in migration output
  - Since SDK handles most standard imports, focus on preserving only non-standard custom imports

### FR2: Multi-Targeting Support
- **Current State**: Only single TargetFramework supported
- **Requirement**: Full multi-targeting capability
  - Support `<TargetFrameworks>` (plural) generation
  - Create conditional ItemGroups for framework-specific dependencies
  - Handle framework-specific compilation symbols
  - Support common scenarios like net48;net6.0 targeting
  - Preserve framework-specific package references

### FR3: Performance Optimization
- **Current State**: No caching, repeated API calls for package versions
- **Requirement**: Implement caching strategy similar to clean-deps command
  - Cache NuGet package version lookups
  - Cache MSBuild project evaluations
  - Add progress reporting for long-running migrations
  - Implement batch API calls where possible
  - Consider in-memory caching with configurable TTL

### FR4: Better .NET Framework Support
- **Current State**: Some assumptions about .NET Core migration
- **Requirement**: First-class support for Framework-to-Framework SDK-style migrations
  - Ensure all .NET Framework versions are properly handled
  - Don't add .NET Core/5+ specific features to Framework projects
  - Properly handle Framework-specific package quirks
  - Support Framework-specific project types correctly

## Technical Requirements

### TR1: Custom Import Analysis (Services/LegacyProjectElements.cs)
- Modify import removal logic to categorize imports:
  - Standard MSBuild imports (safe to remove)
  - NuGet imports (safe to remove)
  - Custom/unknown imports (analyze and warn)
- Add configuration option to preserve specific import patterns

### TR2: Multi-Targeting Generator (Services/CleanSdkStyleProjectGenerator.cs)
- Extend generator to support TargetFrameworks property
- Implement logic to group dependencies by target framework
- Generate conditional ItemGroups with Condition attributes
- Handle framework-specific PropertyGroups

### TR3: Caching Infrastructure
- Create ICacheService abstraction
- Implement MemoryCacheService with configurable TTL
- Integrate caching into:
  - NuGetAssetsResolver
  - NuGetVersionProvider
  - MSBuildProjectEvaluator
- Add cache statistics to verbose logging

### TR4: Migration Progress Reporting
- Add IProgressReporter interface
- Implement console-based progress reporter
- Report progress at project and solution levels
- Include time estimates for large solutions

## Implementation Hints

### Custom Import Handling
```csharp
// In LegacyProjectElements.cs
public static class ImportCategories
{
    public static bool IsStandardImport(string importPath) =>
        importPath.Contains("Microsoft.Common.props") ||
        importPath.Contains("Microsoft.CSharp.targets") ||
        // ... other standard imports
        
    public static bool IsCustomImport(string importPath) =>
        !IsStandardImport(importPath) && !IsNuGetImport(importPath);
}
```

### Multi-Targeting Structure
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net48;net6.0</TargetFrameworks>
  </PropertyGroup>
  
  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
    <PackageReference Include="System.ValueTuple" Version="4.5.0" />
  </ItemGroup>
  
  <ItemGroup Condition="'$(TargetFramework)' == 'net6.0'">
    <!-- .NET 6 specific packages -->
  </ItemGroup>
</Project>
```

### Caching Pattern
```csharp
// Similar to existing TransitiveDependencyCache in clean-deps
public interface IPackageVersionCache
{
    Task<string?> GetVersionAsync(string packageId, string targetFramework);
    Task SetVersionAsync(string packageId, string targetFramework, string version);
}
```

## Acceptance Criteria

### AC1: Custom Build Support
- Tool warns when removing potentially important custom imports
- Users can configure which import patterns to preserve
- Migration succeeds for projects with complex build customizations

### AC2: Multi-Targeting
- Projects can target multiple frameworks after migration
- Framework-specific dependencies are properly segregated
- Build succeeds for all target frameworks

### AC3: Performance
- Large solution (100+ projects) migration time reduced by 40%+
- Package version lookups are cached across projects
- Progress indication provided during migration

### AC4: Framework Focus
- .NET Framework projects migrate without .NET Core assumptions
- Framework-specific features are preserved
- No unnecessary modern .NET features added

## Assumptions

1. Most custom imports can be safely removed (SDK handles them)
2. Users want intelligent analysis rather than blind preservation
3. Multi-targeting primarily between .NET Framework and .NET Core/5+
4. Performance is critical for enterprise adoption
5. Migration reports not needed (based on user feedback)

## Out of Scope

1. Automatic SDK type detection for Razor/Blazor (Framework-only focus)
2. Detailed migration report generation (user indicated not needed)
3. Preserving ALL custom imports (user wants intelligent handling)
4. Rollback enhancements (user indicated not needed)