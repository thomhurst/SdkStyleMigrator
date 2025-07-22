namespace SdkMigrator.Abstractions;

public interface IConfigurationFileGenerator
{
    Task<bool> GenerateAppSettingsFromConfigAsync(string configPath, string outputDir, CancellationToken cancellationToken);
    Task<bool> GenerateStartupMigrationCodeAsync(string projectDir, string targetFramework, CancellationToken cancellationToken);
}