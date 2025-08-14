using Microsoft.Build.Evaluation;
using SdkMigrator.Models;
using System.Xml.Linq;

namespace SdkMigrator.Abstractions;

/// <summary>
/// Handles gRPC Service project migration specifics
/// </summary>
public interface IGrpcServiceHandler
{
    /// <summary>
    /// Detects gRPC configuration and .proto files
    /// </summary>
    Task<GrpcProjectInfo> DetectGrpcConfigurationAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates gRPC specific elements to SDK-style format
    /// </summary>
    Task MigrateGrpcProjectAsync(
        GrpcProjectInfo info, 
        XElement projectElement,
        List<PackageReference> packageReferences,
        MigrationResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures .proto files are properly configured for compilation
    /// </summary>
    void ConfigureProtoFiles(string projectDirectory, XElement projectElement, GrpcProjectInfo info);

    /// <summary>
    /// Sets up gRPC specific build properties and tooling
    /// </summary>
    void ConfigureGrpcProperties(XElement projectElement, GrpcProjectInfo info);
    
    /// <summary>
    /// Sets whether to generate modern Program.cs files during migration
    /// </summary>
    void SetGenerateModernProgramCs(bool enabled);
}