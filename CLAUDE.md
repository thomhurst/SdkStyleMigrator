# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SdkMigrator is a .NET 9 console application that automates the migration of legacy MSBuild project files (.csproj, .vbproj, .fsproj) to the modern SDK-style format. It handles complex scenarios including package migration, transitive dependency removal, and Central Package Management support.

## Common Development Commands

### Building
```bash
# Build the project
dotnet build

# Build in Release mode
dotnet build -c Release

# Clean and rebuild
dotnet clean && dotnet build
```

### Running the Application
```bash
# Run migration on a directory (default command)
dotnet run -- /path/to/solution

# Analyze projects without making changes
dotnet run -- analyze /path/to/solution

# Dry run to preview changes
dotnet run -- /path/to/solution --dry-run

# Rollback a migration
dotnet run -- rollback /path/to/solution

# Remove transitive dependencies from SDK-style projects
dotnet run -- clean-deps /path/to/solution

# Clean unused packages from Central Package Management
dotnet run -- clean-cpm /path/to/solution
```

### Code Quality
```bash
# Format code using dotnet format
dotnet format

# Check for formatting issues without fixing
dotnet format --verify-no-changes

# Build with warnings as errors
dotnet build -warnaserror
```

### Testing
Note: This project currently lacks unit tests. When implementing tests:
- Use xUnit as the test framework (standard for .NET projects)
- Place tests in a SdkMigrator.Tests project
- Follow the AAA pattern (Arrange, Act, Assert)

## Architecture and Code Structure

### Service-Oriented Architecture
The codebase follows SOLID principles with dependency injection:

1. **Interfaces** (Abstractions/) - All services have corresponding interfaces
2. **Services** (Services/) - Concrete implementations with single responsibilities
3. **Models** (Models/) - Domain entities and options
4. **Program.cs** - CLI setup using System.CommandLine

### Key Service Flow for Migration

1. **IProjectFileScanner** (Services/ProjectFileScanner.cs) - Discovers project files
2. **IProjectParser** (Services/ProjectParser.cs) - Parses and validates legacy projects
3. **IPackageReferenceMigrator** (Services/PackageReferenceMigrator.cs) - Converts packages.config
4. **ITransitiveDependencyDetector** (Services/TransitiveDependencyDetector.cs) - Identifies removable packages
5. **CleanSdkStyleProjectGenerator** (Services/CleanSdkStyleProjectGenerator.cs) - Generates new format (replaced legacy 2300-line class)
6. **IMigrationOrchestrator** (Services/MigrationOrchestrator.cs) - Coordinates the process

### Critical Services

- **BackupService** (Services/BackupService.cs) - Creates centralized backups with manifest
- **LockService** (Services/LockService.cs) - Prevents concurrent migrations
- **NuGetAssetsResolver** (Services/NuGetAssetsResolver.cs) - Resolves package dependencies
- **PostMigrationValidator** (Services/PostMigrationValidator.cs) - Validates migration results
- **DirectoryBuildPropsReader** (Services/DirectoryBuildPropsReader.cs) - Reads inherited MSBuild properties
- **DirectoryBuildPropsGenerator** (Services/DirectoryBuildPropsGenerator.cs) - Creates Directory.Build.props

### Edge Case Handlers

- **ClickOnceHandler** (Services/ClickOnceHandler.cs) - Detects ClickOnce projects
- **NativeDependencyHandler** (Services/NativeDependencyHandler.cs) - Manages native dependencies
- **EntityFrameworkMigrationHandler** (Services/EntityFrameworkMigrationHandler.cs) - EF-specific handling
- **ServiceReferenceHandler** (Services/ServiceReferenceHandler.cs) - WCF service references
- **T4TemplateHandler** (Services/T4TemplateHandler.cs) - Handles T4 text templates
- **BuildEventMigrator** (Services/BuildEventMigrator.cs) - Migrates pre/post build events

## Important Implementation Details

### Recent Major Refactoring
The project recently underwent significant cleanup:
- Replaced a 2300-line God class (`SdkStyleProjectGenerator`) with `CleanSdkStyleProjectGenerator` (300 lines)
- Removed over-engineered clean architecture patterns (CQRS, Domain layers, etc.)
- Now follows KISS principle while maintaining SOLID principles

### Critical Migration Features
1. **COM Reference Support** - Preserves all COM metadata including Guid, VersionMajor, EmbedInteropTypes
2. **Strong Naming** - Handles SignAssembly and AssemblyOriginatorKeyFile with path resolution
3. **Directory.Build.props Awareness** - Reads inherited properties to avoid duplication
4. **Compile Exclusions** - Detects files that exist but weren't originally compiled
5. **AssemblyInfo Conflicts** - Prevents duplicate attribute errors by detecting existing AssemblyInfo files
6. **WPF/WinForms Detection** - Correctly sets SDK type based on target framework

### Package Version Resolution
The system uses multiple strategies for resolving package versions:
1. NuGet API queries (online mode)
2. Hardcoded versions (offline mode)
3. Central Package Management resolution
4. Wildcard version handling

### Backup System
- Creates timestamped backup directories (_sdkmigrator_backup_*)
- Generates JSON manifests for rollback
- Supports session-based rollback

### Parallel Processing
- Configurable via --parallel flag
- Uses SemaphoreSlim for thread safety
- Particularly important for large solutions

### Logging
- Uses Microsoft.Extensions.Logging
- Configurable log levels (Trace through Error)
- Structured logging for better debugging

## Common Modifications

### Adding New Migration Rules
1. Update `LegacyProjectElements.cs` for elements to remove/preserve
2. Modify `CleanSdkStyleProjectGenerator.cs` for generation logic
3. Add edge case handler if needed (implement interface, register in DI)

### Adding New Commands
1. Define command in Program.cs (follow existing pattern)
2. Create handler method (e.g., RunNewCommand)
3. Add necessary services and register in DI container

### Modifying Package Resolution
1. Update `NuGetAssetsResolver.cs` for online resolution
2. Modify `NuGetVersionProvider.cs` for offline/hardcoded versions
3. Consider `CentralPackageManagementGenerator.cs` for CPM scenarios

## Key Files to Understand

- **Program.cs** - Entry point and CLI structure
- **MigrationOrchestrator.cs** - Main migration workflow
- **LegacyProjectElements.cs** - Defines what to remove/keep
- **CleanSdkStyleProjectGenerator.cs** - Creates new project format (replaced legacy God class)
- **NuGetTransitiveDependencyDetector.cs** - Complex dependency analysis
- **DirectoryBuildPropsReader.cs** - Reads inherited MSBuild properties

## Development Tips

1. Always test migrations with --dry-run first
2. Use --log-level Debug for troubleshooting
3. Check backup manifests in _sdkmigrator_backup_* directories
4. For large solutions, use --parallel for better performance
5. When debugging package issues, check --offline mode behavior