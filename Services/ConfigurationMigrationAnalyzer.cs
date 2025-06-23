using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SdkMigrator.Models;

namespace SdkMigrator.Services;

public class ConfigurationMigrationAnalyzer
{
    private readonly ILogger<ConfigurationMigrationAnalyzer> _logger;

    public ConfigurationMigrationAnalyzer(ILogger<ConfigurationMigrationAnalyzer> logger)
    {
        _logger = logger;
    }

    public ConfigurationMigrationGuidance AnalyzeConfiguration(string projectPath, string targetFramework)
    {
        var guidance = new ConfigurationMigrationGuidance
        {
            ProjectPath = projectPath
        };

        var projectDir = Path.GetDirectoryName(projectPath);
        if (projectDir == null)
            return guidance;

        // Check for web.config
        var webConfigPath = Path.Combine(projectDir, "web.config");
        if (!File.Exists(webConfigPath))
        {
            webConfigPath = Path.Combine(projectDir, "Web.config");
        }

        if (File.Exists(webConfigPath))
        {
            AnalyzeWebConfig(webConfigPath, targetFramework, guidance);
        }

        // Check for app.config
        var appConfigPath = Path.Combine(projectDir, "app.config");
        if (!File.Exists(appConfigPath))
        {
            appConfigPath = Path.Combine(projectDir, "App.config");
        }

        if (File.Exists(appConfigPath))
        {
            AnalyzeAppConfig(appConfigPath, targetFramework, guidance);
        }

        return guidance;
    }

    private void AnalyzeWebConfig(string configPath, string targetFramework, ConfigurationMigrationGuidance guidance)
    {
        guidance.ConfigType = "web.config";
        guidance.ConfigPath = configPath;

        try
        {
            var doc = XDocument.Load(configPath);
            var root = doc.Root;
            if (root == null || root.Name != "configuration")
                return;

            // Analyze system.web section
            var systemWeb = root.Element("system.web");
            if (systemWeb != null)
            {
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "system.web",
                    Issue = "system.web configuration is not used in .NET Core/5+",
                    MigrationSteps = new List<string>
                    {
                        "Move authentication settings to appsettings.json or use ASP.NET Core Identity",
                        "Convert httpModules to ASP.NET Core middleware",
                        "Convert httpHandlers to ASP.NET Core endpoints or middleware",
                        "Move compilation settings to project file properties",
                        "Convert custom errors to ASP.NET Core exception handling middleware"
                    }
                });

                // Check for specific elements
                CheckAuthenticationSettings(systemWeb, guidance);
                CheckSessionState(systemWeb, guidance);
                CheckHttpModulesAndHandlers(systemWeb, guidance);
            }

            // Analyze appSettings
            var appSettings = root.Element("appSettings");
            if (appSettings != null && appSettings.Elements("add").Any())
            {
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "appSettings",
                    Issue = "appSettings should be migrated to appsettings.json",
                    MigrationSteps = new List<string>
                    {
                        "Create appsettings.json file if it doesn't exist",
                        "Convert <add key=\"name\" value=\"value\"/> to JSON format",
                        "Use IConfiguration to read settings in code",
                        "Consider using Options pattern for strongly-typed configuration"
                    },
                    CodeExample = @"// appsettings.json
{
  ""MySettings"": {
    ""Setting1"": ""value1"",
    ""Setting2"": ""value2""
  }
}

// Startup.cs or Program.cs
services.Configure<MySettings>(Configuration.GetSection(""MySettings""));"
                });
            }

            // Analyze connection strings
            var connectionStrings = root.Element("connectionStrings");
            if (connectionStrings != null && connectionStrings.Elements("add").Any())
            {
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "connectionStrings",
                    Issue = "Connection strings should be migrated to appsettings.json",
                    MigrationSteps = new List<string>
                    {
                        "Add ConnectionStrings section to appsettings.json",
                        "Use configuration.GetConnectionString(\"name\") to retrieve",
                        "Consider using User Secrets for development",
                        "Use environment variables or Azure Key Vault for production"
                    },
                    CodeExample = @"// appsettings.json
{
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Server=...;Database=...;""
  }
}

// Usage
var connectionString = Configuration.GetConnectionString(""DefaultConnection"");"
                });
            }

            // Check for WCF configuration
            var systemServiceModel = root.Element("system.serviceModel");
            if (systemServiceModel != null)
            {
                var wcfIssue = new ConfigurationIssue
                {
                    Section = "system.serviceModel",
                    Issue = "WCF configuration requires major changes for .NET 5+",
                    MigrationSteps = new List<string>()
                };

                // Check if it's client or service
                var clientSection = systemServiceModel.Element("client");
                var servicesSection = systemServiceModel.Element("services");

                if (clientSection != null)
                {
                    wcfIssue.MigrationSteps.Add("WCF CLIENT detected. Options:");
                    wcfIssue.MigrationSteps.Add("1. Use System.ServiceModel.* NuGet packages for basic WCF client support");
                    wcfIssue.MigrationSteps.Add("2. Regenerate service proxies using 'dotnet-svcutil' tool");
                    wcfIssue.MigrationSteps.Add("3. Consider migrating to REST/HTTP clients if the service supports it");
                    wcfIssue.MigrationSteps.Add("4. Configure bindings in code instead of config");

                    wcfIssue.CodeExample = @"// Install packages:
// dotnet add package System.ServiceModel.Duplex
// dotnet add package System.ServiceModel.Http
// dotnet add package System.ServiceModel.NetTcp
// dotnet add package System.ServiceModel.Security

// Regenerate proxy:
// dotnet tool install --global dotnet-svcutil
// dotnet-svcutil https://service.com/service.svc?wsdl

// Configure in code:
var binding = new BasicHttpBinding();
var endpoint = new EndpointAddress(""https://service.com/service.svc"");
var client = new ServiceClient(binding, endpoint);";
                }

                if (servicesSection != null)
                {
                    wcfIssue.MigrationSteps.Add("WCF SERVICE detected. Options:");
                    wcfIssue.MigrationSteps.Add("1. Migrate to ASP.NET Core Web API (recommended)");
                    wcfIssue.MigrationSteps.Add("2. Use CoreWCF for compatibility (community-supported)");
                    wcfIssue.MigrationSteps.Add("3. Consider gRPC for high-performance RPC scenarios");
                    wcfIssue.MigrationSteps.Add("4. Implement OpenAPI/Swagger for better API documentation");

                    wcfIssue.CodeExample = @"// Option 1: Migrate to Web API
[ApiController]
[Route(""api/[controller]"")]
public class MyServiceController : ControllerBase
{
    [HttpPost(""MyOperation"")]
    public async Task<ActionResult<MyResponse>> MyOperation(MyRequest request)
    {
        // Service logic here
    }
}

// Option 2: Use CoreWCF
// dotnet add package CoreWCF.Http
// dotnet add package CoreWCF.Primitives";
                }

                guidance.Issues.Add(wcfIssue);
            }

            // Check for custom configuration sections
            var configSections = root.Element("configSections");
            if (configSections != null && configSections.Elements("section").Any())
            {
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "configSections",
                    Issue = "Custom configuration sections need to be reimplemented",
                    MigrationSteps = new List<string>
                    {
                        "Convert custom configuration to appsettings.json structure",
                        "Create POCO classes for configuration binding",
                        "Use IOptions<T> pattern for dependency injection",
                        "Consider using configuration validation"
                    }
                });
            }

            // Check for Entity Framework configuration
            var entityFramework = root.Element("entityFramework");
            if (entityFramework != null)
            {
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "entityFramework",
                    Issue = "Entity Framework 6 configuration detected",
                    MigrationSteps = new List<string>
                    {
                        "EF6 requires significant changes for .NET 5+:",
                        "Option 1: Migrate to Entity Framework Core (recommended)",
                        "  - Different API, requires code changes",
                        "  - Better performance and cross-platform support",
                        "  - Use EF Core migration tools",
                        "Option 2: Use EF6 with .NET 5+ (limited support)",
                        "  - Install EntityFramework 6.4.4+ NuGet package",
                        "  - Limited to Windows and SQL Server",
                        "  - Configure DbContext in code instead of config"
                    },
                    CodeExample = @"// Option 1: Migrate to EF Core
// Install: Microsoft.EntityFrameworkCore.SqlServer

public class MyContext : DbContext
{
    public MyContext(DbContextOptions<MyContext> options) : base(options) { }
    
    public DbSet<Customer> Customers { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fluent API configuration
    }
}

// In Program.cs or Startup.cs:
services.AddDbContext<MyContext>(options =>
    options.UseSqlServer(Configuration.GetConnectionString(""DefaultConnection"")));

// Option 2: EF6 on .NET 5+
// Install: EntityFramework 6.4.4+
// Configure in code, not config file"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing web.config at {Path}", configPath);
        }
    }

    private void AnalyzeAppConfig(string configPath, string targetFramework, ConfigurationMigrationGuidance guidance)
    {
        guidance.ConfigType = "app.config";
        guidance.ConfigPath = configPath;

        try
        {
            var doc = XDocument.Load(configPath);
            var root = doc.Root;
            if (root == null || root.Name != "configuration")
                return;

            // For .NET Core/5+ console apps, most app.config content should migrate
            if (targetFramework.StartsWith("net5") || targetFramework.StartsWith("net6") ||
                targetFramework.StartsWith("net7") || targetFramework.StartsWith("net8") ||
                targetFramework.Contains("netcore"))
            {
                // Check runtime section
                var runtime = root.Element("runtime");
                if (runtime != null)
                {
                    var assemblyBinding = runtime.Element(XName.Get("assemblyBinding", "urn:schemas-microsoft-com:asm.v1"));
                    if (assemblyBinding != null)
                    {
                        guidance.Issues.Add(new ConfigurationIssue
                        {
                            Section = "runtime/assemblyBinding",
                            Issue = "Assembly binding redirects are not needed in .NET Core/5+",
                            MigrationSteps = new List<string>
                            {
                                "Remove assembly binding redirects",
                                ".NET Core/5+ handles assembly resolution automatically",
                                "Use PackageReference for all dependencies"
                            }
                        });
                    }
                }

                // Check appSettings
                var appSettings = root.Element("appSettings");
                if (appSettings != null && appSettings.Elements("add").Any())
                {
                    guidance.Issues.Add(new ConfigurationIssue
                    {
                        Section = "appSettings",
                        Issue = "Migrate appSettings to appsettings.json",
                        MigrationSteps = new List<string>
                        {
                            "Create appsettings.json in project root",
                            "Add Microsoft.Extensions.Configuration packages",
                            "Use ConfigurationBuilder to load configuration",
                            "Consider environment-specific configuration files"
                        },
                        CodeExample = @"// Program.cs
var configuration = new ConfigurationBuilder()
    .AddJsonFile(""appsettings.json"", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();"
                    });
                }
            }
            else
            {
                // For .NET Framework targets, app.config is still relevant
                guidance.Issues.Add(new ConfigurationIssue
                {
                    Section = "general",
                    Issue = "app.config is still used for .NET Framework targets",
                    MigrationSteps = new List<string>
                    {
                        "Review and clean up unnecessary configuration",
                        "Consider using both app.config and appsettings.json for multi-targeting",
                        "Remove auto-generated binding redirects if using PackageReference"
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing app.config at {Path}", configPath);
        }
    }

    private void CheckAuthenticationSettings(XElement systemWeb, ConfigurationMigrationGuidance guidance)
    {
        var authentication = systemWeb.Element("authentication");
        if (authentication != null)
        {
            var mode = authentication.Attribute("mode")?.Value;
            guidance.Issues.Add(new ConfigurationIssue
            {
                Section = "system.web/authentication",
                Issue = $"Authentication mode '{mode}' needs migration",
                MigrationSteps = mode?.ToLower() switch
                {
                    "forms" => new List<string>
                    {
                        "Use ASP.NET Core Identity for forms authentication",
                        "Configure cookie authentication in Startup.cs",
                        "Migrate membership database if applicable",
                        "Update login/logout logic to use Identity"
                    },
                    "windows" => new List<string>
                    {
                        "Use Windows Authentication in ASP.NET Core",
                        "Configure in launchSettings.json for development",
                        "Configure IIS or Kestrel for Windows auth",
                        "Use [Authorize] attributes with authentication schemes"
                    },
                    _ => new List<string> { "Evaluate authentication requirements for ASP.NET Core" }
                },
                CodeExample = mode?.ToLower() == "forms" ? @"// Program.cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = ""/Account/Login"";
        options.LogoutPath = ""/Account/Logout"";
    });" : null
            });
        }
    }

    private void CheckSessionState(XElement systemWeb, ConfigurationMigrationGuidance guidance)
    {
        var sessionState = systemWeb.Element("sessionState");
        if (sessionState != null)
        {
            guidance.Issues.Add(new ConfigurationIssue
            {
                Section = "system.web/sessionState",
                Issue = "Session state configuration needs migration",
                MigrationSteps = new List<string>
                {
                    "Add session services in ConfigureServices",
                    "Configure session middleware in Configure method",
                    "Consider distributed cache for session storage",
                    "Review session timeout and cookie settings"
                },
                CodeExample = @"// Program.cs
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// In Configure
app.UseSession();"
            });
        }
    }

    private void CheckHttpModulesAndHandlers(XElement systemWeb, ConfigurationMigrationGuidance guidance)
    {
        var httpModules = systemWeb.Element("httpModules");
        var httpHandlers = systemWeb.Element("httpHandlers");

        if (httpModules != null && httpModules.Elements().Any())
        {
            guidance.Issues.Add(new ConfigurationIssue
            {
                Section = "system.web/httpModules",
                Issue = "HTTP Modules need to be converted to middleware",
                MigrationSteps = new List<string>
                {
                    "Create middleware classes for each module",
                    "Register middleware in the request pipeline",
                    "Update module logic to use ASP.NET Core APIs",
                    "Consider middleware order in the pipeline"
                },
                CodeExample = @"// Custom middleware
public class MyMiddleware
{
    private readonly RequestDelegate _next;
    
    public MyMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Before logic
        await _next(context);
        // After logic
    }
}

// Registration
app.UseMiddleware<MyMiddleware>();"
            });
        }

        if (httpHandlers != null && httpHandlers.Elements().Any())
        {
            guidance.Issues.Add(new ConfigurationIssue
            {
                Section = "system.web/httpHandlers",
                Issue = "HTTP Handlers need to be converted to endpoints or middleware",
                MigrationSteps = new List<string>
                {
                    "Convert handlers to minimal APIs or controllers",
                    "Use endpoint routing for URL mapping",
                    "Update handler logic to use ASP.NET Core APIs",
                    "Consider using static file middleware for file handlers"
                },
                CodeExample = @"// Minimal API endpoint
app.MapGet(""/api/myhandler"", async (HttpContext context) =>
{
    // Handler logic
    await context.Response.WriteAsync(""Response"");
});"
            });
        }
    }
}

public class ConfigurationMigrationGuidance
{
    public string ProjectPath { get; set; } = string.Empty;
    public string? ConfigType { get; set; }
    public string? ConfigPath { get; set; }
    public List<ConfigurationIssue> Issues { get; set; } = new();
}

public class ConfigurationIssue
{
    public string Section { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public List<string> MigrationSteps { get; set; } = new();
    public string? CodeExample { get; set; }
}