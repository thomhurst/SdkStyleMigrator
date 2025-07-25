using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using SdkMigrator.Abstractions;
using SdkMigrator.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SdkMigrator.Services;

public class DatabaseProjectHandler : IDatabaseProjectHandler
{
    private readonly ILogger<DatabaseProjectHandler> _logger;

    public DatabaseProjectHandler(ILogger<DatabaseProjectHandler> logger)
    {
        _logger = logger;
    }

    public async Task<DatabaseProjectInfo> DetectDatabaseConfigurationAsync(Project project, CancellationToken cancellationToken = default)
    {
        var info = new DatabaseProjectInfo
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = Path.GetDirectoryName(project.FullPath) ?? string.Empty
        };

        // Comprehensive project analysis
        await AnalyzeProjectStructure(info, project, cancellationToken);

        // Detect database type and version
        await DetectDatabaseTypeAndVersion(info, project, cancellationToken);

        // Comprehensive SQL files and schema detection
        await DetectSqlFiles(info, cancellationToken);

        // Analyze database schema and structure
        await AnalyzeDatabaseSchema(info, cancellationToken);

        // Detect deployment configuration and targets
        await AnalyzeDeploymentConfiguration(info, cancellationToken);

        // Check for modern database development patterns
        await AnalyzeModernDatabasePatterns(info, cancellationToken);

        // Detect CI/CD and DevOps integration
        await AnalyzeCiCdIntegration(info, cancellationToken);

        // Analyze security and compliance patterns
        await AnalyzeSecurityPatterns(info, cancellationToken);

        // Check migration feasibility and recommendations
        await AnalyzeMigrationFeasibility(info, cancellationToken);

        _logger.LogInformation("Detected Database project: Type={Type}, Version={Version}, SqlFiles={Count}, Tables={Tables}, CanMigrate={CanMigrate}, ModernPatterns={Modern}",
            info.DatabaseType, GetDatabaseVersion(info), info.SqlFiles.Count, GetTableCount(info), info.CanMigrate, HasModernPatterns(info));

        return info;
    }

    public async Task MigrateDatabaseProjectAsync(
        DatabaseProjectInfo info, 
        XElement projectElement,
        MigrationResult result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine optimal migration strategy based on project analysis
            if (IsModernSqlProject(info))
            {
                // Modernize existing SQL project with latest tooling
                await ModernizeSqlProject(info, projectElement, result, cancellationToken);
            }
            else if (CanMigrateToSqlProject(info))
            {
                // Migrate legacy database project to modern SQL project
                await MigrateToModernSqlProject(info, projectElement, result, cancellationToken);
            }
            else if (ShouldMigrateToEntityFramework(info))
            {
                // Provide guidance for EF Core migration
                await ProvideEntityFrameworkMigrationGuidance(info, result, cancellationToken);
            }
            else
            {
                // Provide alternative migration strategies
                await ProvideAlternativeMigrationStrategies(info, result, cancellationToken);
            }

            // Apply common database project optimizations
            await ApplyDatabaseProjectOptimizations(info, projectElement, result, cancellationToken);
            
            _logger.LogInformation("Successfully processed Database project: {ProjectPath}", info.ProjectPath);
        }
        catch (Exception ex)
        {
            var error = $"Failed to migrate Database project: {ex.Message}";
            result.Errors.Add(error);
            _logger.LogError(ex, "Database migration failed for {ProjectPath}", info.ProjectPath);
        }
    }

    public void EnsureSqlFilesIncluded(string projectDirectory, XElement projectElement, DatabaseProjectInfo info)
    {
        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        // Include SQL files with appropriate build actions
        foreach (var sqlFile in info.SqlFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, sqlFile);
            var fileName = Path.GetFileName(sqlFile);
            
            string buildAction = "Build";
            if (fileName.Contains("Script.PostDeployment"))
                buildAction = "PostDeploy";
            else if (fileName.Contains("Script.PreDeployment"))
                buildAction = "PreDeploy";
            else if (fileName.EndsWith(".sql"))
                buildAction = "Build";

            EnsureItemIncluded(itemGroup, buildAction, relativePath);
        }

        // Include schema files
        foreach (var schemaFile in info.SchemaFiles)
        {
            var relativePath = Path.GetRelativePath(projectDirectory, schemaFile);
            EnsureItemIncluded(itemGroup, "Build", relativePath);
        }
    }

    public void MigrateDatabaseReferences(XElement projectElement, DatabaseProjectInfo info)
    {
        if (!info.DatabaseReferences.Any())
            return;

        var itemGroup = projectElement.Elements("ItemGroup").FirstOrDefault() ??
                       new XElement("ItemGroup");
        
        if (itemGroup.Parent == null)
            projectElement.Add(itemGroup);

        foreach (var reference in info.DatabaseReferences)
        {
            EnsureItemIncluded(itemGroup, "ArtifactReference", reference, new Dictionary<string, string>
            {
                ["SuppressMissingDependenciesErrors"] = "False"
            });
        }
    }

    public bool CanMigrateToModernFormat(DatabaseProjectInfo info)
    {
        // Comprehensive analysis to determine migration feasibility
        var migrationScore = 0;
        var totalFactors = 0;

        // Positive factors (support migration)
        if (info.DeploymentSettings.ContainsKey("SqlServerVersion"))
        {
            var version = info.DeploymentSettings["SqlServerVersion"];
            if (version.Contains("2017") || version.Contains("2019") || version.Contains("2022"))
            {
                migrationScore += 2;
            }
            totalFactors += 2;
        }

        if (info.SqlFiles.Count < 500) // Reasonable project size
        {
            migrationScore += 1;
        }
        totalFactors += 1;

        if (info.DatabaseReferences.Count < 5) // Simple reference structure
        {
            migrationScore += 1;
        }
        totalFactors += 1;

        // Negative factors (complicate migration)
        if (info.DeploymentSettings.ContainsKey("HasComplexTriggers") && info.DeploymentSettings["HasComplexTriggers"] == "true")
        {
            migrationScore -= 1;
        }
        totalFactors += 1;

        if (info.DeploymentSettings.ContainsKey("UsesLegacyFeatures") && info.DeploymentSettings["UsesLegacyFeatures"] == "true")
        {
            migrationScore -= 2;
        }
        totalFactors += 2;

        // Calculate migration feasibility
        return totalFactors > 0 && (migrationScore / (double)totalFactors) >= 0.6;
    }

    private async Task DetectSqlFiles(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        try
        {
            // Comprehensive SQL file detection and categorization
            await DetectAndCategorizeSqlFiles(info, cancellationToken);

            // Detect schema and metadata files
            await DetectSchemaFiles(info, cancellationToken);

            // Parse project files for references and configuration
            await ParseProjectReferences(info, cancellationToken);

            // Detect database deployment artifacts
            await DetectDeploymentArtifacts(info, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect SQL files: {Error}", ex.Message);
        }
    }

    private async Task DetectAndCategorizeSqlFiles(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var sqlFiles = Directory.GetFiles(info.ProjectDirectory, "*.sql", SearchOption.AllDirectories);
        info.SqlFiles = sqlFiles.ToList();

        var tableCount = 0;
        var viewCount = 0;
        var sprocCount = 0;
        var functionCount = 0;
        var triggerCount = 0;

        foreach (var sqlFile in sqlFiles.Take(50)) // Analyze first 50 files for performance
        {
            try
            {
                var content = await File.ReadAllTextAsync(sqlFile, cancellationToken);
                var fileName = Path.GetFileName(sqlFile).ToLowerInvariant();
                var relativePath = Path.GetRelativePath(info.ProjectDirectory, sqlFile);

                // Categorize SQL files by content and naming patterns
                if (content.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("table") || relativePath.Contains("Tables", StringComparison.OrdinalIgnoreCase))
                {
                    tableCount++;
                    info.DeploymentSettings["Tables"] = tableCount.ToString();
                }
                else if (content.Contains("CREATE VIEW", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("view") || relativePath.Contains("Views", StringComparison.OrdinalIgnoreCase))
                {
                    viewCount++;
                    info.DeploymentSettings["Views"] = viewCount.ToString();
                }
                else if (content.Contains("CREATE PROCEDURE", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("CREATE PROC", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("proc") || fileName.Contains("sp_") ||
                         relativePath.Contains("Procedures", StringComparison.OrdinalIgnoreCase))
                {
                    sprocCount++;
                    info.DeploymentSettings["StoredProcedures"] = sprocCount.ToString();
                }
                else if (content.Contains("CREATE FUNCTION", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("function") || fileName.Contains("fn_") ||
                         relativePath.Contains("Functions", StringComparison.OrdinalIgnoreCase))
                {
                    functionCount++;
                    info.DeploymentSettings["Functions"] = functionCount.ToString();
                }
                else if (content.Contains("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("trigger") ||
                         relativePath.Contains("Triggers", StringComparison.OrdinalIgnoreCase))
                {
                    triggerCount++;
                    info.DeploymentSettings["Triggers"] = triggerCount.ToString();
                }

                // Detect deployment scripts
                if (fileName.Contains("postdeployment") || fileName.Contains("post-deployment"))
                {
                    info.DeploymentSettings["HasPostDeployment"] = "true";
                }
                else if (fileName.Contains("predeployment") || fileName.Contains("pre-deployment"))
                {
                    info.DeploymentSettings["HasPreDeployment"] = "true";
                }

                // Detect data scripts
                if (content.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("data") || fileName.Contains("seed"))
                {
                    info.DeploymentSettings["HasDataScripts"] = "true";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze SQL file {File}: {Error}", sqlFile, ex.Message);
            }
        }

        info.DeploymentSettings["TotalSqlFiles"] = sqlFiles.Length.ToString();
    }

    private async Task DetectSchemaFiles(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        // XSD schema files
        var schemaFiles = Directory.GetFiles(info.ProjectDirectory, "*.xsd", SearchOption.AllDirectories);
        info.SchemaFiles = schemaFiles.ToList();

        // DACPAC files
        var dacpacFiles = Directory.GetFiles(info.ProjectDirectory, "*.dacpac", SearchOption.AllDirectories);
        if (dacpacFiles.Any())
        {
            info.DeploymentSettings["HasDacpac"] = "true";
        }

        // SQL scripts for schema comparison
        var schemaCompareFiles = Directory.GetFiles(info.ProjectDirectory, "*.scmp", SearchOption.AllDirectories);
        if (schemaCompareFiles.Any())
        {
            info.DeploymentSettings["HasSchemaCompare"] = "true";
        }
    }

    private async Task ParseProjectReferences(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var projectFiles = Directory.GetFiles(info.ProjectDirectory, "*.sqlproj", SearchOption.AllDirectories);
        foreach (var projectFile in projectFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(projectFile, cancellationToken);
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(content);

                // Extract database references
                var artifactReferenceNodes = xmlDoc.SelectNodes("//ArtifactReference");
                if (artifactReferenceNodes != null)
                {
                    foreach (XmlNode node in artifactReferenceNodes)
                    {
                        var includeAttr = node.Attributes?["Include"]?.Value;
                        if (!string.IsNullOrEmpty(includeAttr))
                        {
                            info.DatabaseReferences.Add(includeAttr);
                        }
                    }
                }

                // Extract SQL server version and configuration
                var dspNode = xmlDoc.SelectSingleNode("//DSP");
                if (dspNode != null)
                {
                    var dspValue = dspNode.InnerText;
                    if (dspValue.Contains("Sql160"))
                        info.DeploymentSettings["SqlServerVersion"] = "SQL Server 2019+";
                    else if (dspValue.Contains("Sql150"))
                        info.DeploymentSettings["SqlServerVersion"] = "SQL Server 2019";
                    else if (dspValue.Contains("Sql140"))
                        info.DeploymentSettings["SqlServerVersion"] = "SQL Server 2017";
                    else if (dspValue.Contains("Sql130"))
                        info.DeploymentSettings["SqlServerVersion"] = "SQL Server 2016";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse project file {File}: {Error}", projectFile, ex.Message);
            }
        }
    }

    private async Task DetectDeploymentArtifacts(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        // Publish profiles
        var publishProfiles = Directory.GetFiles(info.ProjectDirectory, "*.publish.xml", SearchOption.AllDirectories);
        if (publishProfiles.Any())
        {
            info.DeploymentSettings["HasPublishProfiles"] = "true";
            info.DeploymentSettings["PublishProfileCount"] = publishProfiles.Length.ToString();
        }

        // SQL CMD variables files
        var sqlCmdFiles = Directory.GetFiles(info.ProjectDirectory, "*.sqlcmdvars", SearchOption.AllDirectories);
        if (sqlCmdFiles.Any())
        {
            info.DeploymentSettings["UsesSqlCmdVariables"] = "true";
        }

        // Deployment scripts
        var deploymentScripts = Directory.GetFiles(info.ProjectDirectory, "*.DeploymentScript.sql", SearchOption.AllDirectories);
        if (deploymentScripts.Any())
        {
            info.DeploymentSettings["HasDeploymentScripts"] = "true";
        }
    }

    private static void SetOrUpdateProperty(XElement propertyGroup, string name, string value)
    {
        var existingProperty = propertyGroup.Element(name);
        if (existingProperty != null)
        {
            existingProperty.Value = value;
        }
        else
        {
            propertyGroup.Add(new XElement(name, value));
        }
    }

    // New comprehensive analysis methods
    private async Task AnalyzeProjectStructure(DatabaseProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Analyze target framework
        var targetFramework = project.GetPropertyValue("TargetFramework");
        if (!string.IsNullOrEmpty(targetFramework))
        {
            info.TargetFramework = targetFramework;
        }

        // Check for modern SDK style
        var sdk = project.GetPropertyValue("Sdk");
        if (!string.IsNullOrEmpty(sdk))
        {
            info.DeploymentSettings["UsesModernSdk"] = "true";
            info.DeploymentSettings["SdkType"] = sdk;
        }

        // Analyze project type GUIDs
        var projectTypeGuids = project.GetPropertyValue("ProjectTypeGuids");
        if (!string.IsNullOrEmpty(projectTypeGuids))
        {
            info.DeploymentSettings["ProjectTypeGuids"] = projectTypeGuids;
        }
    }

    private async Task DetectDatabaseTypeAndVersion(DatabaseProjectInfo info, Project project, CancellationToken cancellationToken)
    {
        // Default to SQL Server
        info.DatabaseType = "SqlServer";

        // Detect database type from project properties and content
        var projectTypeGuids = project.GetPropertyValue("ProjectTypeGuids");
        if (projectTypeGuids.Contains("00d1a9c2-b5f0-4af3-8072-f6c62b433612"))
        {
            info.DatabaseType = "SqlServer";
        }
        else if (projectTypeGuids.Contains("a9acf3da-c0b0-4f06-8b0e-dc95d96e0b6e"))
        {
            info.DatabaseType = "Oracle";
        }
        else if (projectTypeGuids.Contains("b0b0f4a0-1234-5678-9abc-def012345678"))
        {
            info.DatabaseType = "MySQL";
        }

        // Check for Azure SQL-specific features
        var sqlFiles = Directory.GetFiles(info.ProjectDirectory, "*.sql", SearchOption.AllDirectories);
        foreach (var sqlFile in sqlFiles.Take(10))
        {
            try
            {
                var content = await File.ReadAllTextAsync(sqlFile, cancellationToken);
                if (content.Contains("WITH (DISTRIBUTION", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("CLUSTERED COLUMNSTORE", StringComparison.OrdinalIgnoreCase))
                {
                    info.DatabaseType = "AzureSql";
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze database type from {File}: {Error}", sqlFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeDatabaseSchema(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var tableCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("Tables", "0"));
        var viewCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("Views", "0"));
        var sprocCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("StoredProcedures", "0"));
        var functionCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("Functions", "0"));

        // Analyze schema complexity
        var totalObjects = tableCount + viewCount + sprocCount + functionCount;
        if (totalObjects > 200)
        {
            info.DeploymentSettings["SchemaComplexity"] = "High";
        }
        else if (totalObjects > 50)
        {
            info.DeploymentSettings["SchemaComplexity"] = "Medium";
        }
        else
        {
            info.DeploymentSettings["SchemaComplexity"] = "Low";
        }

        // Check for advanced SQL features
        await DetectAdvancedSqlFeatures(info, cancellationToken);
    }

    private async Task DetectAdvancedSqlFeatures(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var sqlFiles = info.SqlFiles.Take(20);
        
        foreach (var sqlFile in sqlFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sqlFile, cancellationToken);
                
                if (content.Contains("CREATE SEQUENCE", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesSequences"] = "true";
                    
                if (content.Contains("CREATE TYPE", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesUserDefinedTypes"] = "true";
                    
                if (content.Contains("MERGE ", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesMergeStatements"] = "true";
                    
                if (content.Contains("WITH (", StringComparison.OrdinalIgnoreCase) && 
                    content.Contains("MEMORY_OPTIMIZED", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesInMemoryOLTP"] = "true";
                    
                if (content.Contains("CREATE COLUMNSTORE", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesColumnstore"] = "true";
                    
                if (content.Contains("JSON_VALUE", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("JSON_QUERY", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesJsonFeatures"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to detect advanced features in {File}: {Error}", sqlFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeDeploymentConfiguration(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        // Already handled in DetectDeploymentArtifacts
        
        // Check for environment-specific configurations
        var configFolders = Directory.GetDirectories(info.ProjectDirectory)
            .Where(d => Path.GetFileName(d).ToLowerInvariant().Contains("env") ||
                       Path.GetFileName(d).ToLowerInvariant().Contains("config"))
            .ToList();
            
        if (configFolders.Any())
        {
            info.DeploymentSettings["HasEnvironmentConfigs"] = "true";
        }
    }

    private async Task AnalyzeModernDatabasePatterns(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var modernPatternCount = 0;
        
        // Check for Entity Framework migrations
        var migrationFiles = Directory.GetFiles(info.ProjectDirectory, "*Migration*.cs", SearchOption.AllDirectories);
        if (migrationFiles.Any())
        {
            info.DeploymentSettings["HasEfMigrations"] = "true";
            modernPatternCount++;
        }
        
        // Check for DACPAC deployment
        if (info.DeploymentSettings.ContainsKey("HasDacpac") && info.DeploymentSettings["HasDacpac"] == "true")
        {
            modernPatternCount++;
        }
        
        // Check for automated testing
        var testFiles = Directory.GetFiles(info.ProjectDirectory, "*Test*.sql", SearchOption.AllDirectories);
        if (testFiles.Any())
        {
            info.DeploymentSettings["HasDatabaseTests"] = "true";
            modernPatternCount++;
        }
        
        info.DeploymentSettings["ModernPatternCount"] = modernPatternCount.ToString();
    }

    private async Task AnalyzeCiCdIntegration(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        // Check for CI/CD pipeline files
        var pipelineFiles = new[]
        {
            "azure-pipelines.yml", "azure-pipelines.yaml",
            ".github/workflows/*.yml", ".github/workflows/*.yaml",
            "Jenkinsfile", "gitlab-ci.yml"
        };
        
        foreach (var pattern in pipelineFiles)
        {
            var files = Directory.GetFiles(info.ProjectDirectory, pattern, SearchOption.AllDirectories);
            if (files.Any())
            {
                info.DeploymentSettings["HasCiCdPipeline"] = "true";
                break;
            }
        }
        
        // Check for deployment scripts
        var deploymentScripts = Directory.GetFiles(info.ProjectDirectory, "deploy*.ps1", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(info.ProjectDirectory, "deploy*.sh", SearchOption.AllDirectories))
            .ToList();
            
        if (deploymentScripts.Any())
        {
            info.DeploymentSettings["HasDeploymentAutomation"] = "true";
        }
    }

    private async Task AnalyzeSecurityPatterns(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        var sqlFiles = info.SqlFiles.Take(20);
        
        foreach (var sqlFile in sqlFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(sqlFile, cancellationToken);
                
                if (content.Contains("CREATE ROLE", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("ALTER ROLE", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesRoleBasedSecurity"] = "true";
                    
                if (content.Contains("GRANT ", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("DENY ", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesPermissions"] = "true";
                    
                if (content.Contains("ENCRYPTION", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesEncryption"] = "true";
                    
                if (content.Contains("ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("CREATE SECURITY POLICY", StringComparison.OrdinalIgnoreCase))
                    info.DeploymentSettings["UsesRowLevelSecurity"] = "true";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze security patterns in {File}: {Error}", sqlFile, ex.Message);
            }
        }
    }

    private async Task AnalyzeMigrationFeasibility(DatabaseProjectInfo info, CancellationToken cancellationToken)
    {
        info.CanMigrate = CanMigrateToModernFormat(info);
        
        // Provide detailed migration assessment
        var migrationBlockers = new List<string>();
        
        if (info.SqlFiles.Count > 1000)
        {
            migrationBlockers.Add("Large number of SQL files may require staged migration");
        }
        
        if (info.DatabaseReferences.Count > 10)
        {
            migrationBlockers.Add("Complex reference structure may require manual review");
        }
        
        if (info.DeploymentSettings.ContainsKey("UsesLegacyFeatures") && 
            info.DeploymentSettings["UsesLegacyFeatures"] == "true")
        {
            migrationBlockers.Add("Legacy SQL features may not be supported in modern tooling");
        }
        
        info.DeploymentSettings["MigrationBlockers"] = string.Join("; ", migrationBlockers);
        info.DeploymentSettings["MigrationComplexity"] = migrationBlockers.Count > 2 ? "High" : 
                                                         migrationBlockers.Count > 0 ? "Medium" : "Low";
    }

    // Migration methods
    private async Task ModernizeSqlProject(DatabaseProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Modernizing existing SQL project with latest tooling");

        // Update to latest SQL Server Data Tools
        await UpdateSqlProjectToLatest(info, projectElement, result, cancellationToken);

        result.Warnings.Add("SQL project modernized with latest SSDT tooling and best practices.");
    }

    private async Task MigrateToModernSqlProject(DatabaseProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migrating legacy database project to modern SQL project format");

        // Set modern SQL project SDK
        projectElement.SetAttributeValue("Sdk", "Microsoft.Build.Sql");

        // Configure modern SQL project properties
        await ConfigureModernSqlProperties(info, projectElement, result, cancellationToken);

        // Include SQL files with proper build actions
        EnsureSqlFilesIncluded(info.ProjectDirectory, projectElement, info);

        result.Warnings.Add("Database project migrated to modern SQL project format. Verify deployment settings.");
        result.Warnings.Add("Test database deployment in non-production environment before proceeding.");
    }

    private async Task ProvideEntityFrameworkMigrationGuidance(DatabaseProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Providing Entity Framework Core migration guidance");

        result.Warnings.Add("Entity Framework Core Migration Recommendation:");
        result.Warnings.Add("1. Consider migrating to EF Core Code-First approach for better .NET integration");
        result.Warnings.Add("2. Use 'Scaffold-DbContext' to generate models from existing database");
        result.Warnings.Add("3. Implement EF Core migrations for schema versioning");
        result.Warnings.Add("4. Maintain database project for deployment and reference data");
        
        if (int.Parse(info.DeploymentSettings.GetValueOrDefault("Tables", "0")) > 50)
        {
            result.Warnings.Add("Large schema detected - consider incremental migration approach");
        }
    }

    private async Task ProvideAlternativeMigrationStrategies(DatabaseProjectInfo info, MigrationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Providing alternative migration strategies");

        result.Warnings.Add("Alternative Database Development Strategies:");
        result.Warnings.Add("1. Database-first with Entity Framework Core for .NET integration");
        result.Warnings.Add("2. Continue with SQL Server Data Tools (SSDT) for complex database projects");
        result.Warnings.Add("3. Consider containerized database development with Docker");
        result.Warnings.Add("4. Implement database CI/CD with Azure DevOps or GitHub Actions");
        
        if (info.DeploymentSettings.ContainsKey("MigrationBlockers"))
        {
            result.Warnings.Add($"Migration considerations: {info.DeploymentSettings["MigrationBlockers"]}");
        }
    }

    private async Task ApplyDatabaseProjectOptimizations(DatabaseProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Performance and deployment optimizations
        SetOrUpdateProperty(propertyGroup, "TreatWarningsAsErrors", "true");
        SetOrUpdateProperty(propertyGroup, "SuppressTSqlWarnings", "71502;71504");
        
        // Modern compilation settings
        SetOrUpdateProperty(propertyGroup, "ValidateCasingOnIdentifiers", "true");
        SetOrUpdateProperty(propertyGroup, "ValidateIdentifierValues", "true");
        
        result.Warnings.Add("Applied database project optimizations for better code quality and deployment reliability.");
    }

    private async Task UpdateSqlProjectToLatest(DatabaseProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Update to latest SQL Server version
        SetOrUpdateProperty(propertyGroup, "DSP", "Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider");
        SetOrUpdateProperty(propertyGroup, "ModelCollation", "1033, CI");
    }

    private async Task ConfigureModernSqlProperties(DatabaseProjectInfo info, XElement projectElement, MigrationResult result, CancellationToken cancellationToken)
    {
        var propertyGroup = projectElement.Elements("PropertyGroup").FirstOrDefault() ?? new XElement("PropertyGroup");
        if (propertyGroup.Parent == null) projectElement.Add(propertyGroup);

        // Essential modern properties
        SetOrUpdateProperty(propertyGroup, "TargetFramework", "net8.0");
        SetOrUpdateProperty(propertyGroup, "ProjectGuid", Guid.NewGuid().ToString());
        SetOrUpdateProperty(propertyGroup, "DSP", "Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider");
        SetOrUpdateProperty(propertyGroup, "OutputType", "Database");
        
        // SQL-specific properties based on detected database type
        if (info.DatabaseType == "AzureSql")
        {
            SetOrUpdateProperty(propertyGroup, "TargetDatabaseSet", "True");
            SetOrUpdateProperty(propertyGroup, "DefaultCollation", "SQL_Latin1_General_CP1_CI_AS");
        }
    }

    // Helper methods
    private bool IsModernSqlProject(DatabaseProjectInfo info)
    {
        return info.DeploymentSettings.ContainsKey("UsesModernSdk") && 
               info.DeploymentSettings["UsesModernSdk"] == "true";
    }

    private bool CanMigrateToSqlProject(DatabaseProjectInfo info)
    {
        return info.CanMigrate && 
               info.DeploymentSettings.GetValueOrDefault("MigrationComplexity", "High") != "High";
    }

    private bool ShouldMigrateToEntityFramework(DatabaseProjectInfo info)
    {
        var tableCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("Tables", "0"));
        var sprocCount = int.Parse(info.DeploymentSettings.GetValueOrDefault("StoredProcedures", "0"));
        
        // EF is suitable for table-heavy, procedure-light schemas
        return tableCount > 10 && sprocCount < tableCount * 0.5;
    }

    private string GetDatabaseVersion(DatabaseProjectInfo info)
    {
        return info.DeploymentSettings.GetValueOrDefault("SqlServerVersion", "Unknown");
    }

    private int GetTableCount(DatabaseProjectInfo info)
    {
        return int.Parse(info.DeploymentSettings.GetValueOrDefault("Tables", "0"));
    }

    private bool HasModernPatterns(DatabaseProjectInfo info)
    {
        return int.Parse(info.DeploymentSettings.GetValueOrDefault("ModernPatternCount", "0")) >= 2;
    }

    private static void EnsureItemIncluded(XElement itemGroup, string itemType, string include, Dictionary<string, string>? metadata = null)
    {
        var existingItem = itemGroup.Elements(itemType)
            .FirstOrDefault(e => e.Attribute("Include")?.Value == include);

        if (existingItem == null)
        {
            var item = new XElement(itemType, new XAttribute("Include", include));
            
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    item.Add(new XElement(kvp.Key, kvp.Value));
                }
            }
            
            itemGroup.Add(item);
        }
    }
}