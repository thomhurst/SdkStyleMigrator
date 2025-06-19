# SDK Migrator - Edge Cases Action Plan

## Summary of Critical Gaps Addressed

### 1. ✅ **Enhanced Project Type Detection**
- **File**: `Services/ProjectTypeDetector.cs`
- **Features**:
  - Detects C++/CLI projects and blocks migration
  - Identifies Azure Functions, Worker Services, Test projects
  - Suggests correct SDK based on project characteristics
  - Uses both GUIDs and package references for detection

### 2. ✅ **Build Events Migration**
- **File**: `Services/BuildEventMigrator.cs`
- **Features**:
  - Converts PreBuildEvent/PostBuildEvent to MSBuild targets
  - Migrates Exec tasks with proper BeforeTargets/AfterTargets
  - Provides specific examples for common patterns
  - Warns about path and environment variable compatibility

### 3. ✅ **ClickOnce Detection and Warning**
- **File**: `Services/DeploymentDetector.cs`
- **Features**:
  - Detects all ClickOnce properties
  - Provides clear warnings about limited .NET 5+ support
  - Suggests MSIX and other alternatives
  - Lists specific ClickOnce features in use

### 4. ✅ **Native Dependencies Handler**
- **File**: `Services/NativeDependencyHandler.cs`
- **Features**:
  - Detects native DLLs via References and Content items
  - Scans for P/Invoke DllImport declarations
  - Ensures native files are copied to output
  - Adds platform-specific conditions

### 5. ✅ **Enhanced Configuration Analysis**
- **File**: `Services/ConfigurationMigrationAnalyzer.cs` (enhanced)
- **Features**:
  - WCF client vs service detection with specific guidance
  - Entity Framework 6 migration options
  - Code examples for both scenarios
  - Clear migration paths

### 6. ✅ **Service Reference Detection**
- **File**: `Services/ServiceReferenceDetector.cs`
- **Features**:
  - Finds legacy Service References and .svcmap files
  - Extracts WSDL endpoints when possible
  - Provides dotnet-svcutil commands
  - Lists required NuGet packages

## Integration Requirements

### 1. Update SdkStyleProjectGenerator

```csharp
// Add new dependencies via constructor injection
private readonly ProjectTypeDetector _projectTypeDetector;
private readonly BuildEventMigrator _buildEventMigrator;
private readonly DeploymentDetector _deploymentDetector;
private readonly NativeDependencyHandler _nativeDependencyHandler;
private readonly ServiceReferenceDetector _serviceReferenceDetector;

// In GenerateSdkStyleProjectAsync method:

// 1. Detect project type first
var projectTypeInfo = _projectTypeDetector.DetectProjectType(legacyProject);
if (!projectTypeInfo.CanMigrate)
{
    result.Success = false;
    result.Errors.Add(projectTypeInfo.MigrationBlocker!);
    return result;
}

// 2. Use detected SDK
var sdk = projectTypeInfo.SuggestedSdk ?? DetermineSdk(legacyProject);

// 3. Check deployment method
var deploymentInfo = _deploymentDetector.DetectDeploymentMethod(legacyProject);
_deploymentDetector.AddDeploymentWarnings(deploymentInfo, result);

// 4. Detect and handle native dependencies
var nativeDeps = _nativeDependencyHandler.DetectNativeDependencies(legacyProject);
_nativeDependencyHandler.MigrateNativeDependencies(nativeDeps, projectElement, result);

// 5. Migrate build events
_buildEventMigrator.MigrateBuildEvents(legacyProject, projectElement, result);

// 6. Detect service references
var serviceRefInfo = _serviceReferenceDetector.DetectServiceReferences(legacyProject);
_serviceReferenceDetector.AddServiceReferenceWarnings(serviceRefInfo, result);
```

### 2. Update DI Registration in Program.cs

```csharp
services.AddSingleton<ProjectTypeDetector>();
services.AddSingleton<BuildEventMigrator>();
services.AddSingleton<DeploymentDetector>();
services.AddSingleton<NativeDependencyHandler>();
services.AddSingleton<ServiceReferenceDetector>();
```

### 3. Update MigrationResult Model

```csharp
public class MigrationResult
{
    // ... existing properties ...
    
    // Add new properties for edge case tracking
    public ProjectTypeInfo? DetectedProjectType { get; set; }
    public DeploymentInfo? DeploymentInfo { get; set; }
    public List<NativeDependency> NativeDependencies { get; set; } = new();
    public ServiceReferenceInfo? ServiceReferences { get; set; }
    public bool HasCriticalBlockers { get; set; }
}
```

## Testing Strategy

### 1. Unit Tests for Each Detector
- Mock various project configurations
- Test detection accuracy
- Verify warning messages

### 2. Integration Tests
- Test projects with multiple edge cases
- Verify migration output
- Check warning comprehensiveness

### 3. Test Cases to Cover
- C++/CLI project (should fail)
- Azure Functions project
- WPF + ClickOnce project
- Console app with native P/Invoke
- Web app with WCF services
- Class library with EF6

## Risk Mitigation

### 1. **False Positives**
- Conservative detection (prefer warnings over assumptions)
- Allow user override via command-line flags
- Detailed logging of detection logic

### 2. **Breaking Changes**
- Always backup before migration
- Dry-run mode shows all warnings
- Clear documentation of manual steps

### 3. **Performance**
- Lazy evaluation of expensive checks
- Parallel processing where safe
- Caching of detection results

## Future Enhancements

### 1. **Custom MSBuild Task Detection**
- Scan for UsingTask elements
- Warn about .NET Framework dependencies
- Suggest NuGet packaging for tasks

### 2. **T4 Template Support**
- Detect .tt files
- Suggest TextTemplating.Targets
- Warn about design-time generation

### 3. **Shared Project Support**
- Detect .shproj imports
- Suggest migration strategies
- Multi-targeting as alternative

### 4. **Advanced Scenarios**
- Code Contracts
- PostSharp aspects
- Custom versioning schemes
- Complex build pipelines

## Success Metrics

1. **Detection Coverage**: 95%+ of edge cases detected
2. **False Positive Rate**: <5% incorrect warnings
3. **User Satisfaction**: Clear, actionable guidance
4. **Migration Success**: 80%+ projects migrate without manual intervention
5. **Time Saved**: 50%+ reduction in manual migration effort