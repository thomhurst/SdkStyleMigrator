using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class EntityFrameworkMigrationHandler
{
    private readonly ILogger<EntityFrameworkMigrationHandler> _logger;
    private readonly INuGetPackageResolver _nugetResolver;

    public EntityFrameworkMigrationHandler(
        ILogger<EntityFrameworkMigrationHandler> logger,
        INuGetPackageResolver nugetResolver)
    {
        _logger = logger;
        _nugetResolver = nugetResolver;
    }

    public async Task<EntityFrameworkInfo> DetectEntityFrameworkAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new EntityFrameworkInfo();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";

        // Check for EF references
        var efReferences = project.Items
            .Where(i => i.ItemType == "Reference" && 
                       i.EvaluatedInclude.StartsWith("EntityFramework", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (efReferences.Any())
        {
            info.UsesEntityFramework = true;
            info.IsEF6 = efReferences.Any(r => r.EvaluatedInclude.StartsWith("EntityFramework, Version=6", StringComparison.OrdinalIgnoreCase));
        }

        // Check packages
        var packages = await GetPackagesAsync(projectDir, cancellationToken);
        var efPackage = packages.FirstOrDefault(p => p.Id.Equals("EntityFramework", StringComparison.OrdinalIgnoreCase));
        
        if (efPackage != null)
        {
            info.UsesEntityFramework = true;
            info.EntityFrameworkVersion = efPackage.Version;
            info.IsEF6 = efPackage.Version.StartsWith("6.");
        }

        // Check for EF Core packages
        var efCorePackage = packages.FirstOrDefault(p => 
            p.Id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        
        if (efCorePackage != null)
        {
            info.UsesEntityFramework = true;
            info.IsEFCore = true;
            info.EntityFrameworkVersion = efCorePackage.Version;
        }

        // Check for migrations folder
        var migrationsPath = Path.Combine(projectDir, "Migrations");
        if (Directory.Exists(migrationsPath))
        {
            info.HasMigrations = true;
            info.MigrationsPath = migrationsPath;
            
            // Count migration files
            var migrationFiles = Directory.GetFiles(migrationsPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(f), 
                    @"^\d{14}_.*\.cs$")) // EF migration pattern: 202312011234567_MigrationName.cs
                .ToList();
                
            info.MigrationCount = migrationFiles.Count;
        }

        // Check for DbContext classes
        var contextFiles = await FindDbContextFilesAsync(project, cancellationToken);
        info.DbContextFiles.AddRange(contextFiles);

        // Check app.config for EF configuration
        var configPath = Path.Combine(projectDir, "app.config");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(projectDir, "App.config");
        }

        if (File.Exists(configPath))
        {
            try
            {
                var doc = XDocument.Load(configPath);
                var efSection = doc.Root?.Element("entityFramework");
                if (efSection != null)
                {
                    info.HasEFConfiguration = true;
                    
                    // Check for code first configuration
                    var contexts = efSection.Element("contexts");
                    if (contexts != null)
                    {
                        info.UsesCodeFirst = true;
                    }
                    
                    // Check for connection factory
                    var defaultConnectionFactory = efSection.Element("defaultConnectionFactory");
                    if (defaultConnectionFactory != null)
                    {
                        info.ConnectionFactoryType = defaultConnectionFactory.Attribute("type")?.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading EF configuration from app.config");
            }
        }

        return info;
    }

    public void AddEntityFrameworkSupport(EntityFrameworkInfo efInfo, XElement projectElement, MigrationResult result)
    {
        if (!efInfo.UsesEntityFramework)
            return;

        var itemGroup = new XElement("ItemGroup");
        var hasItems = false;

        if (efInfo.IsEF6)
        {
            // EF6 specific handling
            _logger.LogInformation("Adding Entity Framework 6 support");
            
            // Add EF6 package reference
            var efPackage = new XElement("PackageReference",
                new XAttribute("Include", "EntityFramework"),
                new XAttribute("Version", efInfo.EntityFrameworkVersion ?? "6.4.4"));
            itemGroup.Add(efPackage);
            hasItems = true;
            
            // Add provider packages if needed
            if (efInfo.ConnectionFactoryType?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                var sqlPackage = new XElement("PackageReference",
                    new XAttribute("Include", "System.Data.SqlClient"),
                    new XAttribute("Version", "4.8.6"));
                itemGroup.Add(sqlPackage);
            }
            
            result.Warnings.Add("Entity Framework 6 detected. Ensure the following:");
            result.Warnings.Add("- EF6 works on .NET Core 3.0+ but with limitations (Windows only for some features)");
            result.Warnings.Add("- Consider migrating to EF Core for better .NET Core/5+ support");
            result.Warnings.Add("- Test migrations commands: Add-Migration, Update-Database");
        }
        else if (efInfo.IsEFCore)
        {
            // EF Core specific handling
            _logger.LogInformation("Entity Framework Core support detected");
            
            // Add design-time tools if migrations exist
            if (efInfo.HasMigrations)
            {
                var designPackage = new XElement("PackageReference",
                    new XAttribute("Include", "Microsoft.EntityFrameworkCore.Design"));
                    
                // Add PrivateAssets to prevent transitive dependency
                var privateAssets = new XElement("PrivateAssets", "all");
                var includeAssets = new XElement("IncludeAssets", 
                    "runtime; build; native; contentfiles; analyzers; buildtransitive");
                    
                designPackage.Add(privateAssets);
                designPackage.Add(includeAssets);
                itemGroup.Add(designPackage);
                hasItems = true;
                
                result.Warnings.Add("EF Core migrations detected. The Design package has been added.");
                result.Warnings.Add("Use 'dotnet ef' commands for migrations (not Package Manager Console)");
            }
        }

        if (hasItems)
        {
            projectElement.Add(itemGroup);
        }

        // Add additional warnings and guidance
        if (efInfo.HasMigrations)
        {
            result.Warnings.Add($"Found {efInfo.MigrationCount} EF migrations in {efInfo.MigrationsPath}");
            result.Warnings.Add("After migration, test the following commands:");
            
            if (efInfo.IsEF6)
            {
                result.Warnings.Add("- Package Manager Console: Add-Migration, Update-Database");
                result.Warnings.Add("- Or install: dotnet tool install --global EntityFramework.Commands");
            }
            else
            {
                result.Warnings.Add("- dotnet ef migrations add <name>");
                result.Warnings.Add("- dotnet ef database update");
                result.Warnings.Add("- Ensure you have: dotnet tool install --global dotnet-ef");
            }
        }

        if (efInfo.HasEFConfiguration)
        {
            if (efInfo.IsEF6)
            {
                result.Warnings.Add("EF6 configuration detected in app.config - this will still be used");
            }
            else
            {
                result.Warnings.Add("EF configuration in app.config detected - migrate to code-based configuration");
                result.Warnings.Add("Configure DbContext in OnConfiguring method or via dependency injection");
            }
        }
    }

    private async Task<List<Package>> GetPackagesAsync(string projectDir, CancellationToken cancellationToken)
    {
        var packages = new List<Package>();
        var packagesConfigPath = Path.Combine(projectDir, "packages.config");
        
        if (File.Exists(packagesConfigPath))
        {
            try
            {
                var doc = XDocument.Load(packagesConfigPath);
                var packageElements = doc.Root?.Elements("package") ?? Enumerable.Empty<XElement>();
                
                foreach (var package in packageElements)
                {
                    var id = package.Attribute("id")?.Value;
                    var version = package.Attribute("version")?.Value;
                    
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                    {
                        packages.Add(new Package { Id = id, Version = version });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading packages.config");
            }
        }
        
        return packages;
    }

    private async Task<List<string>> FindDbContextFilesAsync(Project project, CancellationToken cancellationToken)
    {
        var contextFiles = new List<string>();
        var projectDir = Path.GetDirectoryName(project.FullPath) ?? "";
        
        // Look for files that might contain DbContext
        var csFiles = project.Items
            .Where(i => i.ItemType == "Compile" && i.EvaluatedInclude.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(i => Path.Combine(projectDir, i.EvaluatedInclude))
            .Where(File.Exists)
            .ToList();
        
        foreach (var file in csFiles.Take(100)) // Limit to avoid performance issues
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                if (content.Contains(": DbContext", StringComparison.Ordinal) || 
                    content.Contains(":DbContext", StringComparison.Ordinal) ||
                    content.Contains("class") && content.Contains("DbContext"))
                {
                    contextFiles.Add(Path.GetRelativePath(projectDir, file));
                }
            }
            catch
            {
                // Skip files we can't read
            }
        }
        
        return contextFiles;
    }

    private class Package
    {
        public string Id { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}

public class EntityFrameworkInfo
{
    public bool UsesEntityFramework { get; set; }
    public bool IsEF6 { get; set; }
    public bool IsEFCore { get; set; }
    public string? EntityFrameworkVersion { get; set; }
    public bool HasMigrations { get; set; }
    public string? MigrationsPath { get; set; }
    public int MigrationCount { get; set; }
    public bool HasEFConfiguration { get; set; }
    public bool UsesCodeFirst { get; set; }
    public string? ConnectionFactoryType { get; set; }
    public List<string> DbContextFiles { get; set; } = new();
}