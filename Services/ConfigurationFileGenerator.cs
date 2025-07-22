using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

using SdkMigrator.Abstractions;

namespace SdkMigrator.Services;

public class ConfigurationFileGenerator : IConfigurationFileGenerator
{
    private readonly ILogger<ConfigurationFileGenerator> _logger;

    public ConfigurationFileGenerator(ILogger<ConfigurationFileGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<bool> GenerateAppSettingsFromConfigAsync(string configPath, string outputDir, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("Configuration file not found: {ConfigPath}", configPath);
                return false;
            }

            var doc = XDocument.Load(configPath);
            var root = doc.Root;
            if (root?.Name != "configuration")
            {
                _logger.LogWarning("Invalid configuration file format: {ConfigPath}", configPath);
                return false;
            }

            var appSettingsContent = new Dictionary<string, object>();

            // Convert appSettings
            ConvertAppSettings(root, appSettingsContent);

            // Convert connectionStrings
            ConvertConnectionStrings(root, appSettingsContent);

            // Add logging configuration for .NET Core
            AddDefaultLoggingConfiguration(appSettingsContent);

            // Only generate file if there's meaningful content
            if (appSettingsContent.Count == 0 || (appSettingsContent.Count == 1 && appSettingsContent.ContainsKey("Logging")))
            {
                _logger.LogInformation("No meaningful configuration found to migrate from {ConfigPath}", configPath);
                return false;
            }

            var appSettingsPath = Path.Combine(outputDir, "appsettings.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var jsonContent = JsonSerializer.Serialize(appSettingsContent, jsonOptions);
            await File.WriteAllTextAsync(appSettingsPath, jsonContent, cancellationToken);

            _logger.LogInformation("Generated appsettings.json from {ConfigPath} at {OutputPath}", configPath, appSettingsPath);

            // Generate development-specific settings if applicable
            await GenerateDevelopmentSettingsAsync(outputDir, appSettingsContent, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating appsettings.json from {ConfigPath}", configPath);
            return false;
        }
    }

    private void ConvertAppSettings(XElement root, Dictionary<string, object> appSettingsContent)
    {
        var appSettings = root.Element("appSettings");
        if (appSettings == null) return;

        var settingsDict = new Dictionary<string, string>();
        
        foreach (var add in appSettings.Elements("add"))
        {
            var key = add.Attribute("key")?.Value;
            var value = add.Attribute("value")?.Value;
            
            if (!string.IsNullOrEmpty(key) && value != null)
            {
                settingsDict[key] = value;
            }
        }

        if (settingsDict.Count > 0)
        {
            appSettingsContent["AppSettings"] = settingsDict;
        }
    }

    private void ConvertConnectionStrings(XElement root, Dictionary<string, object> appSettingsContent)
    {
        var connectionStrings = root.Element("connectionStrings");
        if (connectionStrings == null) return;

        var connStringsDict = new Dictionary<string, string>();
        
        foreach (var add in connectionStrings.Elements("add"))
        {
            var name = add.Attribute("name")?.Value;
            var connectionString = add.Attribute("connectionString")?.Value;
            
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(connectionString))
            {
                connStringsDict[name] = connectionString;
            }
        }

        if (connStringsDict.Count > 0)
        {
            appSettingsContent["ConnectionStrings"] = connStringsDict;
        }
    }

    private void AddDefaultLoggingConfiguration(Dictionary<string, object> appSettingsContent)
    {
        var loggingConfig = new Dictionary<string, object>
        {
            ["LogLevel"] = new Dictionary<string, object>
            {
                ["Default"] = "Information",
                ["Microsoft.AspNetCore"] = "Warning"
            }
        };
        
        appSettingsContent["Logging"] = loggingConfig;
    }

    private async Task GenerateDevelopmentSettingsAsync(string outputDir, Dictionary<string, object> baseSettings, CancellationToken cancellationToken)
    {
        // Create development-specific appsettings with enhanced logging
        var devSettings = new Dictionary<string, object>
        {
            ["Logging"] = new Dictionary<string, object>
            {
                ["LogLevel"] = new Dictionary<string, object>
                {
                    ["Default"] = "Debug",
                    ["System"] = "Information",
                    ["Microsoft"] = "Information"
                }
            }
        };

        // If there are connection strings, add development versions with Local DB
        if (baseSettings.TryGetValue("ConnectionStrings", out var connectionStringsObj) && 
            connectionStringsObj is Dictionary<string, string> connectionStrings)
        {
            var devConnectionStrings = new Dictionary<string, string>();
            
            foreach (var kvp in connectionStrings)
            {
                // Convert production connection strings to LocalDB for development
                if (kvp.Value.Contains("Server=") && kvp.Value.Contains("Database="))
                {
                    var dbName = ExtractDatabaseName(kvp.Value);
                    devConnectionStrings[kvp.Key] = $@"Server=(localdb)\mssqllocaldb;Database={dbName}_Dev;Trusted_Connection=true;MultipleActiveResultSets=true";
                }
                else
                {
                    devConnectionStrings[kvp.Key] = kvp.Value; // Keep as-is if can't parse
                }
            }

            if (devConnectionStrings.Count > 0)
            {
                devSettings["ConnectionStrings"] = devConnectionStrings;
            }
        }

        var devSettingsPath = Path.Combine(outputDir, "appsettings.Development.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var jsonContent = JsonSerializer.Serialize(devSettings, jsonOptions);
        await File.WriteAllTextAsync(devSettingsPath, jsonContent, cancellationToken);

        _logger.LogInformation("Generated development configuration at {DevSettingsPath}", devSettingsPath);
    }

    private string ExtractDatabaseName(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2 && 
                (keyValue[0].Trim().Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                 keyValue[0].Trim().Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)))
            {
                return keyValue[1].Trim();
            }
        }

        return "DefaultDatabase";
    }

    public async Task<bool> GenerateStartupMigrationCodeAsync(string projectDir, string targetFramework, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(projectDir, "web.config");
        var isWebConfig = File.Exists(configPath);
        
        if (!isWebConfig)
        {
            configPath = Path.Combine(projectDir, "Web.config");
            isWebConfig = File.Exists(configPath);
        }

        if (!isWebConfig)
        {
            configPath = Path.Combine(projectDir, "app.config");
            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(projectDir, "App.config");
                if (!File.Exists(configPath))
                {
                    return false; // No config file to migrate
                }
            }
        }

        try
        {
            var doc = XDocument.Load(configPath);
            var root = doc.Root;
            if (root?.Name != "configuration")
                return false;

            var codeBuilder = new System.Text.StringBuilder();
            
            if (isWebConfig)
            {
                await GenerateWebStartupCodeAsync(root, codeBuilder, targetFramework);
            }
            else
            {
                await GenerateConsoleStartupCodeAsync(root, codeBuilder, targetFramework);
            }

            if (codeBuilder.Length > 0)
            {
                var codeFilePath = Path.Combine(projectDir, "ConfigurationMigration.cs");
                await File.WriteAllTextAsync(codeFilePath, codeBuilder.ToString(), cancellationToken);
                
                _logger.LogInformation("Generated configuration migration code at {CodeFilePath}", codeFilePath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating startup migration code for {ProjectDir}", projectDir);
            return false;
        }
    }

    private async Task GenerateWebStartupCodeAsync(XElement root, System.Text.StringBuilder codeBuilder, string targetFramework)
    {
        codeBuilder.AppendLine("// Generated configuration migration code");
        codeBuilder.AppendLine("// Add this to your Program.cs or Startup.cs");
        codeBuilder.AppendLine();

        var systemWeb = root.Element("system.web");
        if (systemWeb != null)
        {
            // Check for authentication
            var authentication = systemWeb.Element("authentication");
            if (authentication != null)
            {
                var mode = authentication.Attribute("mode")?.Value?.ToLower();
                
                codeBuilder.AppendLine("// Authentication Migration");
                if (mode == "forms")
                {
                    codeBuilder.AppendLine("builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)");
                    codeBuilder.AppendLine("    .AddCookie(options =>");
                    codeBuilder.AppendLine("    {");
                    codeBuilder.AppendLine("        options.LoginPath = \"/Account/Login\";");
                    codeBuilder.AppendLine("        options.LogoutPath = \"/Account/Logout\";");
                    codeBuilder.AppendLine("        options.AccessDeniedPath = \"/Account/AccessDenied\";");
                    codeBuilder.AppendLine("    });");
                }
                else if (mode == "windows")
                {
                    codeBuilder.AppendLine("builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme);");
                }
                codeBuilder.AppendLine();
            }

            // Check for session state
            var sessionState = systemWeb.Element("sessionState");
            if (sessionState != null)
            {
                codeBuilder.AppendLine("// Session State Migration");
                codeBuilder.AppendLine("builder.Services.AddDistributedMemoryCache();");
                codeBuilder.AppendLine("builder.Services.AddSession(options =>");
                codeBuilder.AppendLine("{");
                codeBuilder.AppendLine("    options.IdleTimeout = TimeSpan.FromMinutes(20);");
                codeBuilder.AppendLine("    options.Cookie.HttpOnly = true;");
                codeBuilder.AppendLine("    options.Cookie.IsEssential = true;");
                codeBuilder.AppendLine("});");
                codeBuilder.AppendLine();
                codeBuilder.AppendLine("// Add this to the request pipeline:");
                codeBuilder.AppendLine("// app.UseSession();");
                codeBuilder.AppendLine();
            }
        }

        await Task.CompletedTask;
    }

    private async Task GenerateConsoleStartupCodeAsync(XElement root, System.Text.StringBuilder codeBuilder, string targetFramework)
    {
        codeBuilder.AppendLine("// Generated configuration migration code");
        codeBuilder.AppendLine("// Add this to your Program.cs Main method");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("var configuration = new ConfigurationBuilder()");
        codeBuilder.AppendLine("    .AddJsonFile(\"appsettings.json\", optional: true, reloadOnChange: true)");
        codeBuilder.AppendLine("    .AddEnvironmentVariables()");
        codeBuilder.AppendLine("    .Build();");
        codeBuilder.AppendLine();

        // Check for appSettings usage
        var appSettings = root.Element("appSettings");
        if (appSettings != null && appSettings.Elements("add").Any())
        {
            codeBuilder.AppendLine("// Access app settings:");
            foreach (var add in appSettings.Elements("add"))
            {
                var key = add.Attribute("key")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    codeBuilder.AppendLine($"// var {key.Replace(".", "")} = configuration[\"AppSettings:{key}\"];");
                }
            }
            codeBuilder.AppendLine();
        }

        // Check for connection strings
        var connectionStrings = root.Element("connectionStrings");
        if (connectionStrings != null && connectionStrings.Elements("add").Any())
        {
            codeBuilder.AppendLine("// Access connection strings:");
            foreach (var add in connectionStrings.Elements("add"))
            {
                var name = add.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    codeBuilder.AppendLine($"// var {name}ConnectionString = configuration.GetConnectionString(\"{name}\");");
                }
            }
            codeBuilder.AppendLine();
        }

        await Task.CompletedTask;
    }
}