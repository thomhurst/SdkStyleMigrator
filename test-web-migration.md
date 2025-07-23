# MSBuild.SDK.SystemWeb Integration Test

## What we implemented:

1. **Fixed SDK Type Detection Bug** - Previously, .NET Framework web projects were forced to use basic `Microsoft.NET.Sdk`, losing web-specific functionality.

2. **Added MSBuild.SDK.SystemWeb Support** - Now .NET Framework web projects automatically use `MSBuild.SDK.SystemWeb` SDK.

3. **SDK-Only Integration** - The `MSBuild.SDK.SystemWeb` is set as the SDK attribute only, no package reference needed.

4. **Build Validation Warnings** - Users are informed about msbuild requirements and compatibility notes.

## Before (Bug):
```xml
<!-- .NET Framework web project was incorrectly generated as: -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>
  <!-- Missing web-specific functionality -->
</Project>
```

## After (Fixed):
```xml
<!-- .NET Framework web project is now correctly generated as: -->
<Project Sdk="MSBuild.SDK.SystemWeb/4.0.104">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>
  <!-- Inherits web-specific functionality from SystemWeb SDK -->
</Project>
```

## Key Benefits:

✅ **Fixed Critical Bug**: .NET Framework web projects now get proper SDK support  
✅ **Community Standard**: Uses the established MSBuild.SDK.SystemWeb solution  
✅ **Minimal Changes**: Only project file format changes, no code refactoring required  
✅ **Modern Features**: Gets SDK-style benefits while staying on .NET Framework  
✅ **Auto-Included Content**: wwwroot, Views, Areas, web.config handled by SystemWeb SDK  
✅ **Build Guidance**: Clear warnings about msbuild requirements  

## Project Types Supported:

- **Web Application Projects** (GUID: {349c5851-65df-11da-9384-00065b846f21})
- **Web Site Projects** (GUID: {E24C65DC-7377-472B-9ABA-BC803B73C61A})

## Generated Warnings:

When migrating .NET Framework web projects, users will see helpful warnings:
- "This project uses MSBuild.SDK.SystemWeb and requires 'msbuild' for building (not 'dotnet build')"
- "Publishing requires 'msbuild' with standard msdeploy scripts (not 'dotnet publish')"
- "Ensure your CI/CD pipeline uses 'msbuild' commands for .NET Framework web projects"
- "MSBuild.SDK.SystemWeb provides modern SDK-style project format while maintaining .NET Framework compatibility"