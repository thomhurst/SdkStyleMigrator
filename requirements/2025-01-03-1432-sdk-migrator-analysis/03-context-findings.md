# Context Findings - SdkMigrator Analysis

## Key Insights Based on Discovery Answers

### 1. .NET Framework to SDK-Style Migration Focus
Since users are staying on .NET Framework (not migrating to .NET Core/5+), the tool needs to:
- Support SDK-style projects targeting net4x frameworks
- **Current Status**: ✅ Good support - properly converts v4.5.2 → net452, v4.8 → net48
- **Files**: CleanSdkStyleProjectGenerator.cs handles this correctly

### 2. Custom MSBuild Extensions Support
Users need better support for external build tools and custom MSBuild:
- **Critical Gap**: ❌ Custom Import statements are removed during migration
- **Files Affected**: 
  - LegacyProjectElements.cs (lines 71-78) - only handles standard imports
  - CleanSdkStyleProjectGenerator.cs - doesn't preserve custom imports
- **Impact**: Projects using custom .targets/.props files lose critical build logic

### 3. Multi-targeting Support
Users need multi-targeting capabilities:
- **Critical Gap**: ❌ No support for <TargetFrameworks> (plural)
- **Files Affected**:
  - MigrationOptions.cs has unused TargetFrameworks property
  - CleanSdkStyleProjectGenerator.cs only generates single TargetFramework
- **Impact**: Cannot migrate libraries that need to target multiple frameworks

## Major Gaps Identified

### 1. Missing Project Type Support
- **Razor Class Libraries** - No Microsoft.NET.Sdk.Razor detection
- **Blazor Projects** - No Microsoft.NET.Sdk.BlazorWebAssembly handling
- **Files**: ProjectTypeDetector.cs needs extension

### 2. Code Analysis Migration
- **Ruleset Files** - No migration for .ruleset to .editorconfig
- **FxCop/StyleCop** - Legacy code analysis settings not migrated
- **Impact**: Teams lose code quality configurations

### 3. Configuration Transformations
- **Web.config transforms** - Web.Debug.config, Web.Release.config not handled
- **SlowCheetah** - XML transformation files ignored
- **Files**: ConfigurationMigrationAnalyzer.cs only provides warnings

### 4. Build Infrastructure
- **global.json** - SDK version pinning not considered
- **Solution filters (.slnf)** - Could break partial solution migrations
- **Shared projects (.shproj)** - Excluded with no alternative path

### 5. NuGet Behavioral Changes
- **Content files** - Different behavior between packages.config and PackageReference
- **Install scripts** - install.ps1/uninstall.ps1 don't run with PackageReference
- **Tools packages** - Work differently in SDK-style projects

### 6. Performance Issues
- **No caching** - Package lookups, dependency resolution not cached (except in clean-deps)
- **No progress reporting** - Large solutions provide no feedback
- **Files**: NuGetAssetsResolver.cs, MigrationOrchestrator.cs

## Technical Implementation Details

### Working Well:
1. **COM References** - Preserves all metadata correctly
2. **Strong Naming** - Handles key files properly
3. **Assembly References** - Smart conversion to packages
4. **Backup System** - Comprehensive with manifests
5. **Entity Framework** - Special handling implemented

### Needs Improvement:
1. **Custom MSBuild** - Must preserve custom imports
2. **Multi-targeting** - Need conditional ItemGroup support
3. **SDK Detection** - Add Razor/Blazor SDK types
4. **Performance** - Add caching like clean-deps command
5. **Migration Report** - Generate detailed post-migration guidance

## Files Requiring Modification

For custom MSBuild support:
- LegacyProjectElements.cs - Preserve custom imports
- CleanSdkStyleProjectGenerator.cs - Add import generation logic

For multi-targeting:
- CleanSdkStyleProjectGenerator.cs - Support TargetFrameworks
- ProjectParser.cs - Parse multi-targeting scenarios

For performance:
- Create CachingNuGetVersionProvider.cs
- Update MigrationOrchestrator.cs for progress reporting

For missing SDKs:
- ProjectTypeDetector.cs - Add Razor/Blazor detection logic