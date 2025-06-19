# Changelog

## [0.2.1] - 2025-01-19

### Fixed
- **AssemblyInfo Compile Items**: Now properly removes AssemblyInfo.cs compile items from the project file during migration
  - Previously only removed the physical files but left dangling references in the project
  - Now excludes these files during SDK-style project generation
- **Package Version Extraction**: Fixed issue extracting version from packages with names ending in numbers
  - Previously failed on packages like `MyPackage2.1.0.0` or `System.Data.SQLite.Core.1.0.118.0`
  - Now uses regex pattern matching to correctly identify package name and version boundaries
- **Visual Studio Import Removal**: Aggressive removal of all Visual Studio and MSBuild imports
  - Removes ALL .targets and .props imports by default
  - Only preserves relative paths that are NOT .targets or .props files
  - Removes common MSBuild targets (BeforeBuild, AfterBuild, etc.)
  - Removes any import containing MSBuild, VisualStudio, VSTools, or related keywords
  - SDK-style projects handle most build logic automatically without these imports

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