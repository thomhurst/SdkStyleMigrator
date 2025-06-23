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

1. **IProjectFileScanner** (Services/ProjectFileScanner.cs:14) - Discovers project files
2. **IProjectParser** (Services/ProjectParser.cs:17) - Parses and validates legacy projects
3. **IPackageReferenceMigrator** (Services/PackageReferenceMigrator.cs:29) - Converts packages.config
4. **ITransitiveDependencyDetector** (Services/TransitiveDependencyDetector.cs:49) - Identifies removable packages
5. **ISdkStyleProjectGenerator** (Services/SdkStyleProjectGenerator.cs:48) - Generates new format
6. **IMigrationOrchestrator** (Services/MigrationOrchestrator.cs:47) - Coordinates the process

### Critical Services

- **BackupService** (Services/BackupService.cs:29) - Creates centralized backups with manifest
- **LockService** (Services/LockService.cs:13) - Prevents concurrent migrations
- **NuGetAssetsResolver** (Services/NuGetAssetsResolver.cs:56) - Resolves package dependencies
- **PostMigrationValidator** (Services/PostMigrationValidator.cs:16) - Validates migration results

### Edge Case Handlers

- **ClickOnceHandler** (Services/ClickOnceHandler.cs:15) - Detects ClickOnce projects
- **NativeDependencyHandler** (Services/NativeDependencyHandler.cs:14) - Manages native dependencies
- **EntityFrameworkMigrationHandler** (Services/EntityFrameworkMigrationHandler.cs:16) - EF-specific handling
- **ServiceReferenceHandler** (Services/ServiceReferenceHandler.cs:13) - WCF service references

## Important Implementation Details

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
2. Modify `SdkStyleProjectGenerator.cs` for generation logic
3. Add edge case handler if needed (implement interface, register in DI)

### Adding New Commands
1. Define command in Program.cs (follow existing pattern)
2. Create handler method (e.g., RunNewCommand)
3. Add necessary services and register in DI container

### Modifying Package Resolution
1. Update `NuGetAssetsResolver.cs` for online resolution
2. Modify `NuGetVersionProvider.cs` for offline/hardcoded versions
3. Consider `CentralPackageManagementMigrator.cs` for CPM scenarios

## Key Files to Understand

- **Program.cs** - Entry point and CLI structure
- **MigrationOrchestrator.cs** - Main migration workflow
- **LegacyProjectElements.cs** - Defines what to remove/keep
- **SdkStyleProjectGenerator.cs** - Creates new project format
- **TransitiveDependencyDetector.cs** - Complex dependency analysis

## Development Tips

1. Always test migrations with --dry-run first
2. Use --log-level Debug for troubleshooting
3. Check backup manifests in _sdkmigrator_backup_* directories
4. For large solutions, use --parallel for better performance
5. When debugging package issues, check --offline mode behavior