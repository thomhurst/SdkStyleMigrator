using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SdkMigrator.Services;

/// <summary>
/// Advanced security configuration analyzer for comprehensive security assessment
/// including authentication, authorization, cryptography, and compliance requirements
/// </summary>
public class SecurityConfigurationAnalyzer
{
    private readonly ILogger<SecurityConfigurationAnalyzer> _logger;

    public SecurityConfigurationAnalyzer(ILogger<SecurityConfigurationAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Performs comprehensive security configuration analysis including
    /// authentication patterns, authorization mechanisms, cryptography usage,
    /// and compliance requirements
    /// </summary>
    public async Task<SecurityAnalysisResult> AnalyzeSecurityConfiguration(
        Project project,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new SecurityAnalysisResult
        {
            ProjectPath = project.FullPath,
            ProjectDirectory = projectDirectory,
            TargetFramework = project.GetPropertyValue("TargetFramework") ?? 
                            project.GetPropertyValue("TargetFrameworks") ?? "Unknown"
        };

        try
        {
            // Analyze authentication configurations
            await AnalyzeAuthenticationPatterns(project, projectDirectory, result, cancellationToken);

            // Analyze authorization mechanisms
            await AnalyzeAuthorizationPatterns(project, projectDirectory, result, cancellationToken);

            // Analyze cryptography usage
            await AnalyzeCryptographyUsage(project, projectDirectory, result, cancellationToken);

            // Analyze secrets management
            await AnalyzeSecretsManagement(project, projectDirectory, result, cancellationToken);

            // Analyze network security
            await AnalyzeNetworkSecurity(project, projectDirectory, result, cancellationToken);

            // Analyze data protection
            await AnalyzeDataProtection(project, projectDirectory, result, cancellationToken);

            // Analyze security headers and middleware
            await AnalyzeSecurityMiddleware(project, projectDirectory, result, cancellationToken);

            // Analyze compliance requirements
            await AnalyzeComplianceRequirements(project, projectDirectory, result, cancellationToken);

            // Analyze vulnerable dependencies
            await AnalyzeVulnerableDependencies(project, result, cancellationToken);

            // Analyze security misconfigurations
            await AnalyzeSecurityMisconfigurations(project, projectDirectory, result, cancellationToken);

            // Determine overall security posture
            DetermineSecurityPosture(result);

            _logger.LogInformation("Security analysis complete: Posture={Posture}, Score={Score}, Issues={IssueCount}, Critical={CriticalCount}",
                result.SecurityPosture, result.SecurityScore, result.SecurityIssues.Count, 
                result.SecurityIssues.Count(i => i.Severity == "Critical"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze security configuration for {ProjectPath}", project.FullPath);
            result.AnalysisErrors.Add($"Security analysis error: {ex.Message}");
        }

        return result;
    }

    private async Task AnalyzeAuthenticationPatterns(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        var authPatterns = new List<string>();
        
        // Check for authentication packages
        var packages = project.AllEvaluatedItems.Where(i => i.ItemType == "PackageReference");
        foreach (var package in packages)
        {
            var packageId = package.EvaluatedInclude.ToLowerInvariant();
            
            // Identity packages
            if (packageId.Contains("identity"))
            {
                authPatterns.Add("ASP.NET Identity");
                result.AuthenticationMechanisms.Add("ASP.NET Identity");
            }
            
            // OAuth/OIDC packages
            if (packageId.Contains("openidconnect") || packageId.Contains("oauth"))
            {
                authPatterns.Add("OAuth/OpenID Connect");
                result.AuthenticationMechanisms.Add("OAuth/OIDC");
            }
            
            // JWT packages
            if (packageId.Contains("jwt") || packageId.Contains("jwtbearer"))
            {
                authPatterns.Add("JWT Bearer Authentication");
                result.AuthenticationMechanisms.Add("JWT Bearer");
            }
            
            // Azure AD/Entra
            if (packageId.Contains("azuread") || packageId.Contains("entra"))
            {
                authPatterns.Add("Azure AD/Entra ID");
                result.AuthenticationMechanisms.Add("Azure AD");
            }
            
            // SAML
            if (packageId.Contains("saml"))
            {
                authPatterns.Add("SAML");
                result.AuthenticationMechanisms.Add("SAML");
            }
        }

        // Analyze configuration files
        var configFiles = new[] { "appsettings.json", "appsettings.*.json", "web.config", "app.config" };
        foreach (var pattern in configFiles)
        {
            var files = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                await AnalyzeAuthenticationConfig(file, result, cancellationToken);
            }
        }

        // Check for custom authentication
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(50);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for authentication attributes
                if (content.Contains("[Authorize]") || content.Contains("[AllowAnonymous]"))
                {
                    result.UsesAuthorizationAttributes = true;
                }
                
                // Check for authentication handlers
                if (content.Contains("AuthenticationHandler") || content.Contains("IAuthenticationHandler"))
                {
                    result.SecurityFeatures.Add("Custom Authentication Handler");
                }
                
                // Check for authentication schemes
                if (content.Contains("AddAuthentication") && content.Contains("AddScheme"))
                {
                    result.SecurityFeatures.Add("Multiple Authentication Schemes");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze authentication in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeAuthenticationConfig(string configFile, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(configFile, cancellationToken);
            
            // Check for authentication settings
            if (content.Contains("Authentication") || content.Contains("Identity"))
            {
                // Check for weak configurations
                if (content.Contains("\"RequireHttpsMetadata\": false") || 
                    content.Contains("\"RequireHttpsMetadata\":false"))
                {
                    result.SecurityIssues.Add(new SecurityIssue
                    {
                        Type = "Authentication",
                        Severity = "High",
                        Description = "HTTPS metadata requirement disabled in authentication configuration",
                        FilePath = configFile,
                        Recommendation = "Set RequireHttpsMetadata to true in production"
                    });
                }
                
                // Check for development certificates
                if (content.Contains("localhost") && content.Contains("certificate"))
                {
                    result.SecurityWarnings.Add("Development certificates detected - ensure proper certificates in production");
                }
            }
            
            // Check for connection strings
            if (content.Contains("ConnectionString") || content.Contains("DefaultConnection"))
            {
                // Check for plain text passwords
                if (Regex.IsMatch(content, @"password\s*=\s*[^;{]+", RegexOptions.IgnoreCase))
                {
                    result.SecurityIssues.Add(new SecurityIssue
                    {
                        Type = "Secrets",
                        Severity = "Critical",
                        Description = "Plain text password detected in configuration",
                        FilePath = configFile,
                        Recommendation = "Use Azure Key Vault or environment variables for sensitive data"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to analyze config file {File}: {Error}", configFile, ex.Message);
        }
    }

    private async Task AnalyzeAuthorizationPatterns(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        var authzPatterns = new List<string>();
        
        // Check for authorization packages
        var packages = project.AllEvaluatedItems.Where(i => i.ItemType == "PackageReference");
        foreach (var package in packages)
        {
            var packageId = package.EvaluatedInclude.ToLowerInvariant();
            
            if (packageId.Contains("authorization"))
            {
                authzPatterns.Add("Authorization Framework");
                result.AuthorizationMechanisms.Add("ASP.NET Authorization");
            }
            
            if (packageId.Contains("policyserver"))
            {
                authzPatterns.Add("PolicyServer");
                result.AuthorizationMechanisms.Add("PolicyServer");
            }
        }

        // Analyze policy-based authorization
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(50);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for authorization policies
                if (content.Contains("AddAuthorization") && content.Contains("AddPolicy"))
                {
                    result.SecurityFeatures.Add("Policy-based Authorization");
                    authzPatterns.Add("Policy-based Authorization");
                }
                
                // Check for resource-based authorization
                if (content.Contains("IAuthorizationHandler") || content.Contains("AuthorizationHandler<"))
                {
                    result.SecurityFeatures.Add("Resource-based Authorization");
                    authzPatterns.Add("Resource-based Authorization");
                }
                
                // Check for claims-based authorization
                if (content.Contains("ClaimsPrincipal") || content.Contains("RequireClaim"))
                {
                    result.SecurityFeatures.Add("Claims-based Authorization");
                    authzPatterns.Add("Claims-based Authorization");
                }
                
                // Check for role-based authorization
                if (content.Contains("RequireRole") || content.Contains("[Authorize(Roles"))
                {
                    result.SecurityFeatures.Add("Role-based Authorization");
                    authzPatterns.Add("Role-based Authorization");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze authorization in {File}: {Error}", file, ex.Message);
            }
        }
        
        if (!authzPatterns.Any() && result.UsesAuthorizationAttributes)
        {
            result.SecurityWarnings.Add("Authorization attributes used but no authorization policies detected");
        }
    }

    private async Task AnalyzeCryptographyUsage(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        var cryptoPatterns = new HashSet<string>();
        var weakAlgorithms = new List<string>();
        
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(50);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for cryptography namespaces
                if (content.Contains("using System.Security.Cryptography"))
                {
                    result.UsesCryptography = true;
                    
                    // Check for weak algorithms
                    var weakPatterns = new[]
                    {
                        ("MD5", "MD5 is cryptographically broken"),
                        ("SHA1", "SHA1 is deprecated for security use"),
                        ("DES", "DES provides inadequate encryption strength"),
                        ("RC2", "RC2 is obsolete"),
                        ("TripleDES", "3DES is deprecated")
                    };
                    
                    foreach (var (algorithm, reason) in weakPatterns)
                    {
                        if (Regex.IsMatch(content, $@"\b{algorithm}\.Create\(\)"))
                        {
                            weakAlgorithms.Add(algorithm);
                            result.SecurityIssues.Add(new SecurityIssue
                            {
                                Type = "Cryptography",
                                Severity = "High",
                                Description = $"Weak cryptographic algorithm {algorithm} detected",
                                FilePath = file,
                                Recommendation = $"{reason}. Use AES, SHA256, or SHA512 instead"
                            });
                        }
                    }
                    
                    // Check for good algorithms
                    var strongPatterns = new[] { "AES", "RSA", "SHA256", "SHA384", "SHA512", "HMAC" };
                    foreach (var algorithm in strongPatterns)
                    {
                        if (content.Contains($"{algorithm}.Create()") || content.Contains($"new {algorithm}"))
                        {
                            cryptoPatterns.Add(algorithm);
                        }
                    }
                    
                    // Check for proper key management
                    if (content.Contains("GenerateKey()") || content.Contains("GenerateIV()"))
                    {
                        result.SecurityFeatures.Add("Cryptographic Key Generation");
                    }
                    
                    // Check for hard-coded keys
                    if (Regex.IsMatch(content, @"Key\s*=\s*new\s*byte\[\]\s*{[\s\d,]+}"))
                    {
                        result.SecurityIssues.Add(new SecurityIssue
                        {
                            Type = "Cryptography",
                            Severity = "Critical",
                            Description = "Hard-coded cryptographic key detected",
                            FilePath = file,
                            Recommendation = "Use secure key storage like Azure Key Vault"
                        });
                    }
                }
                
                // Check for data protection
                if (content.Contains("IDataProtector") || content.Contains("IDataProtectionProvider"))
                {
                    result.SecurityFeatures.Add("ASP.NET Core Data Protection");
                    cryptoPatterns.Add("DataProtection");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze cryptography in {File}: {Error}", file, ex.Message);
            }
        }
        
        if (cryptoPatterns.Any())
        {
            result.CryptographyAlgorithms = cryptoPatterns.ToList();
        }
    }

    private async Task AnalyzeSecretsManagement(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for secrets management packages
        var packages = project.AllEvaluatedItems.Where(i => i.ItemType == "PackageReference");
        foreach (var package in packages)
        {
            var packageId = package.EvaluatedInclude;
            
            if (packageId.Contains("Azure.Security.KeyVault"))
            {
                result.SecretsManagement.Add("Azure Key Vault");
            }
            else if (packageId.Contains("AWS.SecretManager"))
            {
                result.SecretsManagement.Add("AWS Secrets Manager");
            }
            else if (packageId.Contains("HashiCorp.Vault"))
            {
                result.SecretsManagement.Add("HashiCorp Vault");
            }
            else if (packageId.Contains("Microsoft.Extensions.Configuration.UserSecrets"))
            {
                result.SecretsManagement.Add("User Secrets");
            }
        }
        
        // Check for secret files
        var secretPatterns = new[]
        {
            "*.pfx", "*.p12", "*.key", "*.pem", "*.cer", "*.crt",
            "secrets.json", "secrets.xml", ".env", "*.secrets"
        };
        
        foreach (var pattern in secretPatterns)
        {
            var files = Directory.GetFiles(projectDirectory, pattern, SearchOption.AllDirectories);
            if (files.Any())
            {
                result.SecurityIssues.Add(new SecurityIssue
                {
                    Type = "Secrets",
                    Severity = "High",
                    Description = $"Potential secret files detected: {pattern}",
                    FilePath = string.Join(", ", files.Take(3)),
                    Recommendation = "Move secrets to secure storage and add to .gitignore"
                });
            }
        }
        
        // Check source code for secrets
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(30);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Common secret patterns in code
                var codeSecretPatterns = new[]
                {
                    @"api[_-]?key\s*=\s*[""'][^""']+[""']",
                    @"secret\s*=\s*[""'][^""']+[""']",
                    @"password\s*=\s*[""'][^""']+[""']",
                    @"token\s*=\s*[""'][^""']+[""']",
                    @"connectionstring\s*=\s*[""'][^""']+[""']"
                };
                
                foreach (var pattern in codeSecretPatterns)
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    {
                        result.SecurityIssues.Add(new SecurityIssue
                        {
                            Type = "Secrets",
                            Severity = "Critical",
                            Description = "Hard-coded secret detected in source code",
                            FilePath = file,
                            Recommendation = "Use environment variables or secure secret storage"
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze secrets in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeNetworkSecurity(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for HTTPS enforcement
        var projectType = project.GetPropertyValue("Sdk") ?? "";
        if (projectType.Contains("Web"))
        {
            result.ProjectType = "Web";
            
            // Check for HTTPS redirection
            var startupFiles = Directory.GetFiles(projectDirectory, "*Startup*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(projectDirectory, "Program.cs", SearchOption.AllDirectories));
            
            var hasHttpsRedirection = false;
            var hasHsts = false;
            
            foreach (var file in startupFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    
                    if (content.Contains("UseHttpsRedirection"))
                    {
                        hasHttpsRedirection = true;
                        result.SecurityFeatures.Add("HTTPS Redirection");
                    }
                    
                    if (content.Contains("UseHsts"))
                    {
                        hasHsts = true;
                        result.SecurityFeatures.Add("HSTS");
                    }
                    
                    // Check for CORS
                    if (content.Contains("UseCors") || content.Contains("AddCors"))
                    {
                        result.SecurityFeatures.Add("CORS Configuration");
                        
                        // Check for overly permissive CORS
                        if (content.Contains("AllowAnyOrigin") && content.Contains("AllowAnyMethod") && content.Contains("AllowAnyHeader"))
                        {
                            result.SecurityIssues.Add(new SecurityIssue
                            {
                                Type = "Network",
                                Severity = "High",
                                Description = "Overly permissive CORS configuration detected",
                                FilePath = file,
                                Recommendation = "Restrict CORS to specific origins, methods, and headers"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze network security in {File}: {Error}", file, ex.Message);
                }
            }
            
            if (!hasHttpsRedirection)
            {
                result.SecurityWarnings.Add("HTTPS redirection not configured");
            }
            
            if (!hasHsts)
            {
                result.SecurityWarnings.Add("HSTS (HTTP Strict Transport Security) not configured");
            }
        }
        
        // Check for TLS configuration
        var configFiles = Directory.GetFiles(projectDirectory, "*.config", SearchOption.AllDirectories);
        foreach (var file in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                if (content.Contains("SecurityProtocol") || content.Contains("SslProtocols"))
                {
                    // Check for weak TLS versions
                    if (content.Contains("Tls") && !content.Contains("Tls12") && !content.Contains("Tls13"))
                    {
                        result.SecurityIssues.Add(new SecurityIssue
                        {
                            Type = "Network",
                            Severity = "High",
                            Description = "Weak TLS version configuration detected",
                            FilePath = file,
                            Recommendation = "Use TLS 1.2 or TLS 1.3 only"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze TLS config in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeDataProtection(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for data protection in databases
        var efPackages = project.AllEvaluatedItems
            .Where(i => i.ItemType == "PackageReference" && i.EvaluatedInclude.Contains("EntityFramework"))
            .Any();
        
        if (efPackages)
        {
            var dbContextFiles = Directory.GetFiles(projectDirectory, "*Context.cs", SearchOption.AllDirectories);
            
            foreach (var file in dbContextFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file, cancellationToken);
                    
                    // Check for encryption attributes
                    if (content.Contains("[Encrypted]") || content.Contains("HasConversion") && content.Contains("encrypt"))
                    {
                        result.SecurityFeatures.Add("Database Column Encryption");
                    }
                    
                    // Check for Always Encrypted
                    if (content.Contains("Column Encryption Setting=Enabled"))
                    {
                        result.SecurityFeatures.Add("SQL Always Encrypted");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to analyze data protection in {File}: {Error}", file, ex.Message);
                }
            }
        }
        
        // Check for PII handling
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(30);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Check for PII attributes
                if (content.Contains("[PersonalData]") || content.Contains("[SensitiveData]"))
                {
                    result.SecurityFeatures.Add("PII Data Marking");
                }
                
                // Check for data masking
                if (content.Contains("DataMasking") || content.Contains("Redact"))
                {
                    result.SecurityFeatures.Add("Data Masking");
                }
                
                // Check for audit logging
                if (content.Contains("IAuditLog") || content.Contains("AuditLog"))
                {
                    result.SecurityFeatures.Add("Audit Logging");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze PII handling in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeSecurityMiddleware(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        if (result.ProjectType != "Web")
            return;
        
        var startupFiles = Directory.GetFiles(projectDirectory, "*Startup*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDirectory, "Program.cs", SearchOption.AllDirectories));
        
        foreach (var file in startupFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                // Security headers
                if (content.Contains("UseSecurityHeaders") || content.Contains("AddSecurityHeaders"))
                {
                    result.SecurityFeatures.Add("Security Headers Middleware");
                }
                
                // Content Security Policy
                if (content.Contains("Content-Security-Policy") || content.Contains("CSP"))
                {
                    result.SecurityFeatures.Add("Content Security Policy");
                }
                
                // Rate limiting
                if (content.Contains("UseRateLimiter") || content.Contains("AddRateLimiter"))
                {
                    result.SecurityFeatures.Add("Rate Limiting");
                }
                
                // Anti-forgery
                if (content.Contains("ValidateAntiForgeryToken") || content.Contains("AddAntiforgery"))
                {
                    result.SecurityFeatures.Add("Anti-Forgery Protection");
                }
                
                // Request validation
                if (content.Contains("ModelState.IsValid"))
                {
                    result.SecurityFeatures.Add("Input Validation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze security middleware in {File}: {Error}", file, ex.Message);
            }
        }
    }

    private async Task AnalyzeComplianceRequirements(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        var complianceIndicators = new Dictionary<string, List<string>>
        {
            ["GDPR"] = new List<string> { "PersonalData", "DataProtection", "ConsentRequired", "RightToErasure", "DataPortability" },
            ["HIPAA"] = new List<string> { "PHI", "ProtectedHealthInformation", "MedicalRecord", "HealthData" },
            ["PCI-DSS"] = new List<string> { "CardNumber", "CreditCard", "PaymentCard", "CardholderData" },
            ["SOC2"] = new List<string> { "AuditLog", "AccessControl", "SecurityEvent", "ComplianceAudit" }
        };
        
        var detectedCompliance = new HashSet<string>();
        
        var sourceFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories).Take(50);
        foreach (var file in sourceFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                foreach (var (standard, indicators) in complianceIndicators)
                {
                    if (indicators.Any(indicator => content.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
                    {
                        detectedCompliance.Add(standard);
                    }
                }
                
                // Check for compliance attributes
                if (content.Contains("[Compliance") || content.Contains("ComplianceAttribute"))
                {
                    result.SecurityFeatures.Add("Compliance Attributes");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze compliance in {File}: {Error}", file, ex.Message);
            }
        }
        
        result.ComplianceStandards = detectedCompliance.ToList();
        
        // Add compliance recommendations
        foreach (var standard in detectedCompliance)
        {
            switch (standard)
            {
                case "GDPR":
                    if (!result.SecurityFeatures.Contains("Data Masking"))
                    {
                        result.SecurityWarnings.Add("GDPR compliance detected but no data masking found");
                    }
                    break;
                case "PCI-DSS":
                    if (!result.UsesCryptography || !result.SecurityFeatures.Contains("Database Column Encryption"))
                    {
                        result.SecurityIssues.Add(new SecurityIssue
                        {
                            Type = "Compliance",
                            Severity = "High",
                            Description = "PCI-DSS data detected without encryption",
                            Recommendation = "Implement encryption for cardholder data at rest and in transit"
                        });
                    }
                    break;
                case "HIPAA":
                    if (!result.SecurityFeatures.Contains("Audit Logging"))
                    {
                        result.SecurityWarnings.Add("HIPAA compliance detected but no audit logging found");
                    }
                    break;
            }
        }
    }

    private async Task AnalyzeVulnerableDependencies(Project project, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check for known vulnerable packages (simplified version)
        var vulnerablePackages = new Dictionary<string, string>
        {
            ["Newtonsoft.Json"] = "< 13.0.1",
            ["System.Text.Encodings.Web"] = "< 4.7.2",
            ["System.Text.RegularExpressions"] = "< 4.3.1",
            ["Microsoft.AspNetCore.App"] = "< 3.1.32",
            ["jQuery"] = "< 3.5.0",
            ["bootstrap"] = "< 4.6.2"
        };
        
        var packages = project.AllEvaluatedItems.Where(i => i.ItemType == "PackageReference");
        foreach (var package in packages)
        {
            var packageId = package.EvaluatedInclude;
            var version = package.GetMetadataValue("Version");
            
            if (vulnerablePackages.ContainsKey(packageId))
            {
                // Simplified version check (in production, use NuGet.Versioning)
                result.VulnerablePackages.Add(new VulnerablePackageInfo
                {
                    PackageId = packageId,
                    CurrentVersion = version,
                    VulnerableVersions = vulnerablePackages[packageId],
                    Severity = "High"
                });
            }
        }
    }

    private async Task AnalyzeSecurityMisconfigurations(Project project, string projectDirectory, SecurityAnalysisResult result, CancellationToken cancellationToken)
    {
        // Check debug configuration
        var isDebugEnabled = project.GetPropertyValue("DebugType") == "full" || 
                           project.GetPropertyValue("DebugSymbols") == "true";
        
        if (isDebugEnabled)
        {
            var configuration = project.GetPropertyValue("Configuration");
            if (configuration?.Equals("Release", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.SecurityWarnings.Add("Debug symbols enabled in Release configuration");
            }
        }
        
        // Check for development settings in production
        var configFiles = Directory.GetFiles(projectDirectory, "appsettings.Production.json", SearchOption.AllDirectories);
        foreach (var file in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                if (content.Contains("\"DetailedErrors\": true") || 
                    content.Contains("\"DetailedErrors\":true"))
                {
                    result.SecurityIssues.Add(new SecurityIssue
                    {
                        Type = "Configuration",
                        Severity = "Medium",
                        Description = "Detailed errors enabled in production configuration",
                        FilePath = file,
                        Recommendation = "Disable detailed errors in production"
                    });
                }
                
                if (content.Contains("localhost") || content.Contains("127.0.0.1"))
                {
                    result.SecurityWarnings.Add("Development URLs found in production configuration");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze production config {File}: {Error}", file, ex.Message);
            }
        }
        
        // Check for custom errors disabled
        var webConfigFiles = Directory.GetFiles(projectDirectory, "web.config", SearchOption.AllDirectories);
        foreach (var file in webConfigFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                
                if (content.Contains("customErrors") && content.Contains("mode=\"Off\""))
                {
                    result.SecurityIssues.Add(new SecurityIssue
                    {
                        Type = "Configuration",
                        Severity = "Medium",
                        Description = "Custom errors disabled in web.config",
                        FilePath = file,
                        Recommendation = "Set customErrors mode to 'On' or 'RemoteOnly'"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to analyze web.config {File}: {Error}", file, ex.Message);
            }
        }
    }

    private void DetermineSecurityPosture(SecurityAnalysisResult result)
    {
        var score = 100;
        
        // Critical issues
        score -= result.SecurityIssues.Count(i => i.Severity == "Critical") * 20;
        
        // High severity issues
        score -= result.SecurityIssues.Count(i => i.Severity == "High") * 10;
        
        // Medium severity issues
        score -= result.SecurityIssues.Count(i => i.Severity == "Medium") * 5;
        
        // Warnings
        score -= result.SecurityWarnings.Count * 2;
        
        // Positive factors
        if (result.AuthenticationMechanisms.Any()) score += 5;
        if (result.AuthorizationMechanisms.Any()) score += 5;
        if (result.SecretsManagement.Any()) score += 10;
        if (result.UsesCryptography && !result.SecurityIssues.Any(i => i.Type == "Cryptography")) score += 5;
        if (result.SecurityFeatures.Contains("HTTPS Redirection")) score += 5;
        if (result.SecurityFeatures.Contains("Security Headers Middleware")) score += 5;
        
        // Cap at 100
        score = Math.Min(100, Math.Max(0, score));
        
        result.SecurityScore = score;
        
        // Determine posture
        if (score >= 90)
            result.SecurityPosture = "Excellent";
        else if (score >= 75)
            result.SecurityPosture = "Good";
        else if (score >= 60)
            result.SecurityPosture = "Fair";
        else if (score >= 40)
            result.SecurityPosture = "Poor";
        else
            result.SecurityPosture = "Critical";
    }
}

// Result models
public class SecurityAnalysisResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectDirectory { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    
    // Authentication & Authorization
    public List<string> AuthenticationMechanisms { get; set; } = new();
    public List<string> AuthorizationMechanisms { get; set; } = new();
    public bool UsesAuthorizationAttributes { get; set; }
    
    // Cryptography
    public bool UsesCryptography { get; set; }
    public List<string> CryptographyAlgorithms { get; set; } = new();
    
    // Secrets Management
    public List<string> SecretsManagement { get; set; } = new();
    
    // Security Features
    public List<string> SecurityFeatures { get; set; } = new();
    
    // Compliance
    public List<string> ComplianceStandards { get; set; } = new();
    
    // Issues and Warnings
    public List<SecurityIssue> SecurityIssues { get; set; } = new();
    public List<string> SecurityWarnings { get; set; } = new();
    public List<VulnerablePackageInfo> VulnerablePackages { get; set; } = new();
    
    // Analysis Results
    public string SecurityPosture { get; set; } = "Unknown";
    public int SecurityScore { get; set; }
    public List<string> AnalysisErrors { get; set; } = new();
}

public class SecurityIssue
{
    public string Type { get; set; } = string.Empty; // Authentication, Authorization, Cryptography, Secrets, Network, Configuration
    public string Severity { get; set; } = string.Empty; // Critical, High, Medium, Low
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class VulnerablePackageInfo
{
    public string PackageId { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string VulnerableVersions { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}