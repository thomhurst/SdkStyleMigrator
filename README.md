# SDK Migrator

A .NET 9 console application that migrates legacy MSBuild project files to the modern SDK-style format.

## Features

- **Automatic Project Discovery**: Scans directories recursively for .csproj, .vbproj, and .fsproj files
- **Legacy Property Removal**: Removes unnecessary and problematic legacy MSBuild properties
- **Package Migration**: Converts packages.config to PackageReference format
- **Transitive Dependency Detection**: Identifies and removes transitive package dependencies to keep projects clean
- **Backup Creation**: Creates .legacy backup files before migration
- **Feature Parity**: Maintains all functionality while modernizing the project format

## Architecture

The application follows SOLID principles with a clean architecture:

- **Abstractions**: Interfaces for all major components ensuring testability and flexibility
- **Services**: Concrete implementations with single responsibilities
- **Dependency Injection**: Uses Microsoft.Extensions.DependencyInjection for IoC
- **Separation of Concerns**: Each service handles a specific aspect of the migration

### Key Components

1. **IProjectFileScanner**: Discovers project files in directories
2. **IProjectParser**: Parses MSBuild projects and identifies legacy formats
3. **IPackageReferenceMigrator**: Handles package.config to PackageReference conversion
4. **ITransitiveDependencyDetector**: Identifies transitive dependencies that can be removed
5. **ISdkStyleProjectGenerator**: Generates clean SDK-style project files
6. **IMigrationOrchestrator**: Coordinates the entire migration process

## Usage

```bash
# Build the project
dotnet build

# Run the migrator on a directory
dotnet run -- /path/to/your/solution

# Get help
dotnet run -- --help
```

## What Gets Migrated

### Removed Elements
- Legacy properties (ProjectGuid, ProjectTypeGuids, etc.)
- Old MSBuild imports
- Problematic targets (BeforeBuild, AfterBuild)
- Unnecessary item groups

### Migrated Elements
- Target framework declarations
- Package references (from packages.config and HintPath references)
- Project references
- Essential properties (OutputType, RootNamespace, etc.)
- Content items with special handling

### Intelligent Features
- Detects and removes common transitive dependencies
- Preserves only necessary properties
- Maintains minimal project file structure
- Creates backups with .legacy extension

## Requirements

- .NET 9 SDK
- Microsoft.Build libraries (included via NuGet)

## Building from Source

```bash
git clone <repository>
cd SdkMigrator
dotnet restore
dotnet build
```

## Design Principles

- **SOLID**: Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion
- **DRY**: Don't Repeat Yourself - shared logic is centralized
- **KISS**: Keep It Simple, Stupid - straightforward implementations
- **Testability**: All services are interface-based for easy mocking