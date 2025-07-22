using SdkMigrator.Models;
using Microsoft.Extensions.Logging;

namespace SdkMigrator.Services;

public class CpmPackageClassifier
{
    private readonly ILogger<CpmPackageClassifier> _logger;

    public CpmPackageClassifier(ILogger<CpmPackageClassifier> logger)
    {
        _logger = logger;
    }

    public CpmPackageClassification ClassifyPackage(string packageId, string version, List<string> targetFrameworks)
    {
        var classification = new CpmPackageClassification
        {
            PackageId = packageId,
            Version = version,
            TargetFrameworks = targetFrameworks
        };

        classification.PackageType = DeterminePackageType(packageId);
        classification.IsGlobalReference = ShouldBeGlobalReference(packageId, classification.PackageType);
        classification.Priority = DeterminePriority(classification.PackageType);
        classification.RequiresSpecialHandling = RequiresSpecialHandling(packageId, classification.PackageType);
        
        if (classification.RequiresSpecialHandling)
        {
            classification.SpecialHandlingNotes = GetSpecialHandlingNotes(packageId, classification.PackageType);
        }

        return classification;
    }

    private CpmPackageType DeterminePackageType(string packageId)
    {
        // Analyzer packages
        if (IsAnalyzerPackage(packageId))
            return CpmPackageType.Analyzer;

        // Build tools and MSBuild packages
        if (IsBuildToolPackage(packageId))
            return CpmPackageType.BuildTool;

        // Testing frameworks
        if (IsTestingPackage(packageId))
            return CpmPackageType.Testing;

        // Microsoft runtime/framework packages
        if (IsMicrosoftFrameworkPackage(packageId))
            return CpmPackageType.MicrosoftRuntime;

        // Third-party runtime packages
        if (IsThirdPartyRuntimePackage(packageId))
            return CpmPackageType.ThirdPartyRuntime;

        // Development/design time only packages
        if (IsDevelopmentOnlyPackage(packageId))
            return CpmPackageType.DevelopmentOnly;

        // Default to runtime
        return CpmPackageType.Runtime;
    }

    private bool IsAnalyzerPackage(string packageId)
    {
        var analyzerPackages = new[]
        {
            "StyleCop.Analyzers",
            "SonarAnalyzer.CSharp",
            "Microsoft.CodeAnalysis.NetAnalyzers",
            "Microsoft.CodeAnalysis.FxCopAnalyzers",
            "Microsoft.CodeAnalysis.Analyzers",
            "Roslynator.Analyzers",
            "Microsoft.VisualStudio.Threading.Analyzers",
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            "Microsoft.CodeAnalysis.PublicApiAnalyzers",
            "AsyncUsageAnalyzers",
            "Meziantou.Analyzer",
            "SecurityCodeScan.VS2019"
        };

        return analyzerPackages.Any(ap => packageId.Equals(ap, StringComparison.OrdinalIgnoreCase)) ||
               packageId.EndsWith(".Analyzers", StringComparison.OrdinalIgnoreCase) ||
               packageId.EndsWith(".CodeAnalysis", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBuildToolPackage(string packageId)
    {
        var buildToolPackages = new[]
        {
            "Microsoft.Build",
            "Microsoft.Build.Tasks.Core",
            "Microsoft.Build.Utilities.Core",
            "Microsoft.Build.Framework",
            "MSBuild.Sdk.Extras",
            "Microsoft.SourceLink.GitHub",
            "Microsoft.SourceLink.AzureRepos.Git",
            "Microsoft.SourceLink.Bitbucket.Git",
            "Nerdbank.GitVersioning",
            "GitVersion.MsBuild",
            "Microsoft.CodeCoverage",
            "ReportGenerator"
        };

        return buildToolPackages.Any(btp => packageId.Equals(btp, StringComparison.OrdinalIgnoreCase)) ||
               packageId.StartsWith("Microsoft.Build.", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("Microsoft.SourceLink.", StringComparison.OrdinalIgnoreCase) ||
               packageId.Contains("MSBuild", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTestingPackage(string packageId)
    {
        var testingPackages = new[]
        {
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio",
            "xunit.runner.console",
            "NUnit",
            "NUnit3TestAdapter",
            "MSTest.TestAdapter",
            "MSTest.TestFramework",
            "Microsoft.TestPlatform.TestHost",
            "coverlet.collector",
            "coverlet.msbuild",
            "Moq",
            "NSubstitute",
            "FluentAssertions",
            "Shouldly",
            "Microsoft.EntityFrameworkCore.InMemory"
        };

        return testingPackages.Any(tp => packageId.Equals(tp, StringComparison.OrdinalIgnoreCase)) ||
               packageId.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               packageId.Contains("Mock", StringComparison.OrdinalIgnoreCase) ||
               packageId.Contains("Fake", StringComparison.OrdinalIgnoreCase) ||
               packageId.EndsWith(".Testing", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMicrosoftFrameworkPackage(string packageId)
    {
        return packageId.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsThirdPartyRuntimePackage(string packageId)
    {
        var popularRuntimePackages = new[]
        {
            "Newtonsoft.Json",
            "Serilog",
            "AutoMapper",
            "FluentValidation",
            "MediatR",
            "Polly",
            "Dapper",
            "StackExchange.Redis",
            "MongoDB.Driver",
            "MySql.Data",
            "Npgsql",
            "Oracle.ManagedDataAccess"
        };

        return popularRuntimePackages.Any(rtp => packageId.StartsWith(rtp, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDevelopmentOnlyPackage(string packageId)
    {
        var developmentOnlyPackages = new[]
        {
            "Microsoft.EntityFrameworkCore.Tools",
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.VisualStudio.Web.CodeGeneration.Design",
            "Swashbuckle.AspNetCore",
            "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation"
        };

        return developmentOnlyPackages.Any(dop => packageId.Equals(dop, StringComparison.OrdinalIgnoreCase)) ||
               packageId.EndsWith(".Design", StringComparison.OrdinalIgnoreCase) ||
               packageId.EndsWith(".Tools", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldBeGlobalReference(string packageId, CpmPackageType packageType)
    {
        return packageType == CpmPackageType.Analyzer || 
               packageType == CpmPackageType.BuildTool;
    }

    private int DeterminePriority(CpmPackageType packageType)
    {
        return packageType switch
        {
            CpmPackageType.MicrosoftRuntime => 1,  // Highest priority
            CpmPackageType.Runtime => 2,
            CpmPackageType.ThirdPartyRuntime => 3,
            CpmPackageType.Testing => 4,
            CpmPackageType.BuildTool => 5,
            CpmPackageType.Analyzer => 6,
            CpmPackageType.DevelopmentOnly => 7,  // Lowest priority
            _ => 5
        };
    }

    private bool RequiresSpecialHandling(string packageId, CpmPackageType packageType)
    {
        // Entity Framework packages need special handling for multi-targeting
        if (packageId.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            return true;

        // ASP.NET Core packages may need framework-specific versions
        if (packageId.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            return true;

        // Some packages have breaking changes across major versions
        var breakingChangePackages = new[]
        {
            "AutoMapper",
            "MediatR",
            "FluentValidation",
            "Serilog"
        };

        return breakingChangePackages.Any(bcp => packageId.StartsWith(bcp, StringComparison.OrdinalIgnoreCase));
    }

    private string GetSpecialHandlingNotes(string packageId, CpmPackageType packageType)
    {
        if (packageId.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            return "Entity Framework Core packages may require different versions for .NET Framework vs .NET Core/.NET targets";

        if (packageId.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            return "ASP.NET Core packages should use framework-specific versions";

        if (packageId.StartsWith("AutoMapper", StringComparison.OrdinalIgnoreCase))
            return "AutoMapper has breaking changes between major versions - verify compatibility";

        if (packageId.StartsWith("MediatR", StringComparison.OrdinalIgnoreCase))
            return "MediatR has significant API changes between major versions";

        return "This package may require special attention during version resolution";
    }
}

public class CpmPackageClassification
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public CpmPackageType PackageType { get; set; }
    public bool IsGlobalReference { get; set; }
    public int Priority { get; set; }
    public bool RequiresSpecialHandling { get; set; }
    public string? SpecialHandlingNotes { get; set; }
    public List<string> TargetFrameworks { get; set; } = new();
}

public enum CpmPackageType
{
    Runtime,
    MicrosoftRuntime,
    ThirdPartyRuntime,
    Analyzer,
    BuildTool,
    Testing,
    DevelopmentOnly
}