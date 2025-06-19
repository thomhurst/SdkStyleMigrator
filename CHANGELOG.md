# Changelog

## [0.4.0] - 2025-01-19

### Added
- **Private NuGet Repository Support**: Automatically discovers and uses all configured NuGet sources
  - Searches system-wide, user-wide, and solution/project-level nuget.config files
  - Loads credentials from configured sources automatically
  - Searches private repositories when packages are not found on nuget.org
  - Added `--nuget-config` option to specify custom NuGet configuration file
  - Logs all discovered sources for transparency
- **Local Package File Cleanup**: Automatically removes obsolete local package files after migration
  - Deletes DLLs, PDBs, XML docs, and related files replaced by PackageReference
  - Cleans package directories (packages, lib, libs, references, etc.)
  - Removes empty directories after cleanup
  - Tracks hint paths during migration for targeted cleanup
  - Cleans global packages folder when all projects migrated successfully
  - Integrates with backup and audit services for safe operation

## [0.3.0] - 2025-01-19

### Changed
- **BREAKING CHANGE**: Backup creation is now enabled by default for in-place migrations
  - Use `--no-backup` flag to disable backup creation (not recommended)
  - This change prioritizes data safety and prevents accidental loss of original project files
  - Backup files are created with `.legacy` extension (e.g., `MyProject.legacy.csproj`)

## [0.2.2] - 2025-01-19

### Added
- **Enhanced Project Reference Path Resolution**: Automatically fixes broken project reference paths during migration
  - Searches parent directories for referenced projects
  - Simplifies overly complex relative paths
  - **NEW**: Searches entire repository when other methods fail
  - **NEW**: Intelligently matches projects when multiple candidates exist using path similarity scoring
  - Provides warnings for references that cannot be resolved
  - Logs all path corrections for transparency

### Fixed
- **Assembly Reference Migration**: Fixed missing assembly references in migrated projects
  - Now properly preserves framework extension references like System.Windows.Forms
  - Preserves third-party assembly references with HintPath
  - Skips implicit framework references that are automatically included
  - Preserves important metadata (HintPath, Private, SpecificVersion)
- **Assembly to Package Reference Conversion**: Automatically converts common assembly references to NuGet packages
  - Microsoft.VisualStudio.QualityTools.UnitTestFramework → MSTest.TestFramework + MSTest.TestAdapter
  - nunit.framework → NUnit + NUnit3TestAdapter
  - xunit/xunit.core → xunit + xunit.runner.visualstudio
  - EntityFramework → EntityFramework package
  - Newtonsoft.Json → Newtonsoft.Json package
  - System.Net.Http.Formatting → Microsoft.AspNet.WebApi.Client
  - log4net → log4net package
  - System.Web.Mvc → Microsoft.AspNet.Mvc
  - System.Web.Http → Microsoft.AspNet.WebApi.Core
  - System.Web.Http.WebHost → Microsoft.AspNet.WebApi.WebHost
  - System.Data.SqlClient → Microsoft.Data.SqlClient
  - System.Configuration.ConfigurationManager → System.Configuration.ConfigurationManager package

## [0.2.1] - 2025-01-19

### Fixed
- **NuGet DLL References**: Skip None/Content items pointing to DLLs in NuGet cache
  - Filters out any DLL references from packages or .nuget folders
  - Prevents unnecessary file references to NuGet cache being added to projects
- **Directory.Build.props Creation**: Fixed issue where Directory.Build.props was not being created
  - Now always creates the file even if no common assembly properties exist
  - Ensures binding redirect settings are always added
- **AssemblyInfo Compile Items**: Now properly removes AssemblyInfo.cs compile items from the project file during migration
  - Previously only removed the physical files but left dangling references in the project
  - Now excludes these files during SDK-style project generation
- **Package Version Extraction**: Fixed issue extracting version from packages with names ending in numbers
  - Previously failed on packages like `MyPackage2.1.0.0` or `System.Data.SQLite.Core.1.0.118.0`
  - Now uses regex pattern matching to correctly identify package name and version boundaries
- **Visual Studio Import Removal**: Complete removal of all imports and enhanced project loading
  - Removes ALL imports without exception - SDK-style projects don't need them
  - Enhanced defensive project loading to handle missing Visual Studio/MSBuild paths
  - Automatically strips all imports when projects fail to load due to missing targets
  - Removes common MSBuild targets (BeforeBuild, AfterBuild, etc.)
  - Adds warnings for potentially custom imports that were removed

### Added
- **Automatic Binding Redirects**: Directory.Build.props now includes AutoGenerateBindingRedirects
  - Sets `AutoGenerateBindingRedirects` to `true` for automatic binding redirect generation
  - Sets `GenerateBindingRedirectsOutputType` to `true` for better compatibility
  - Removes manual assembly binding redirects from app.config files
  - Deletes app.config files that contain only binding redirects

### Changed
- **Backup Files**: Legacy backup files (*.legacy.csproj) are now opt-in instead of default
  - Use `--backup` or `-b` flag to create backup files
  - Reduces file clutter in projects by default
  - Applies to both project files and AssemblyInfo files

## [0.2.0] - 2025-01-18

### Added
- **ProjectGuid Preservation**: Now preserves ProjectGuid for solution file compatibility
- **Implicit File Include Handling**: Properly handles SDK-style implicit includes for .cs, .vb, .resx files
  - Only includes files with special metadata (Link, DependentUpon, etc.)
  - Handles files outside project directory
  - Supports file exclusions with Remove attribute
- **WPF/WinForms Support**: Handles special item types (ApplicationDefinition, Page, Resource, etc.)
- **Content to None Conversion**: Converts Content items to None with CopyToOutputDirectory
- **Development Dependencies**: Maps packages.config developmentDependency="true" to PrivateAssets="all"
- **Assembly Signing**: Preserves assembly signing properties (SignAssembly, AssemblyOriginatorKeyFile, etc.)
- **Custom MSBuild Logic**: Preserves custom imports and targets with warnings for manual review
- **COM Reference Handling**: Preserves COM references with warnings about compatibility issues
- **Detailed Migration Report**: Generates comprehensive text file report with:
  - All warnings that need manual review
  - Removed elements list
  - Migrated packages
  - Error details for failed projects

### Changed
- Assembly properties now extracted to Directory.Build.props (from v0.1.0)
- Improved warning system for items requiring manual review

### Migration Warnings Added For
- COM references (compatibility issues with SDK-style projects)
- Custom MSBuild targets (especially BeforeBuild/AfterBuild)
- Conditional PropertyGroups
- EmbeddedResource items with generators

## [0.1.0] - Initial Release

### Features
- Scans directories recursively for legacy project files
- Removes unnecessary legacy properties
- Converts packages.config to PackageReference
- Detects and removes transitive dependencies
- Extracts assembly properties to Directory.Build.props
- Removes AssemblyInfo files with backups
- Creates .legacy backup files