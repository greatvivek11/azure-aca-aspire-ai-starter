using Xunit;
using Shouldly;
using System.Text.RegularExpressions;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

/// <summary>
/// Simple architecture tests to validate backend structure
/// </summary>
public class DependencyArchitectureTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void Backend_Should_Contain_Expected_Feature_Slices()
    {
        var expectedFeaturePaths = new[]
        {
            Path.Combine(RepositoryRoot, "src/backend/Features/Health/Endpoint.cs"),
            Path.Combine(RepositoryRoot, "src/backend/Features/AiPing/Endpoint.cs"),
            Path.Combine(RepositoryRoot, "src/backend/Features/Customers/Endpoint.cs"),
            Path.Combine(RepositoryRoot, "src/backend/Features/DocumentIngestion/Endpoint.cs"),
            Path.Combine(RepositoryRoot, "src/backend/Features/Chat/Endpoint.cs")
        };

        foreach (var expectedPath in expectedFeaturePaths)
        {
            File.Exists(expectedPath).ShouldBeTrue($"Expected feature slice file is missing: {expectedPath}");
        }
    }

    [Fact]
    public void Features_Should_Not_Depends_On_Other_Features()
    {
        var featureFiles = Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src/backend/Features"), "*.cs", SearchOption.AllDirectories)
            .OrderBy(filePath => filePath)
            .ToArray();

        featureFiles.Length.ShouldBeGreaterThan(0, "No backend feature files were discovered.");

        foreach (var filePath in featureFiles)
        {
            var featureName = new DirectoryInfo(Path.GetDirectoryName(filePath)!).Name;
            var source = File.ReadAllText(filePath);
            var crossFeatureImports = Regex.Matches(
                    source,
                    @"using\s+AcaAspireAiTemplate\.Backend\.Features\.(?<target>\w+)",
                    RegexOptions.Multiline)
                .Select(match => match.Groups["target"].Value)
                .Where(target => !string.Equals(target, featureName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            crossFeatureImports.ShouldBeEmpty(
                $"Feature '{featureName}' should not import other features. File: {filePath}");
        }
    }

    [Fact]
    public void Program_Should_Enforce_Auth_Middleware()
    {
        var programPath = Path.Combine(RepositoryRoot, "src/backend/Program.cs");
        var authSetupPath = Path.Combine(RepositoryRoot, "src/backend/Infrastructure/Auth/EntraAuthSetup.cs");
        File.Exists(programPath).ShouldBeTrue("Backend Program.cs was not found.");
        File.Exists(authSetupPath).ShouldBeTrue("Backend Entra auth setup file was not found.");

        var programSource = File.ReadAllText(programPath);
        var authSource = File.ReadAllText(authSetupPath);
        programSource.ShouldContain("UseAuthentication()");
        programSource.ShouldContain("UseAuthorization()");
        programSource.ShouldContain("UseRateLimiter()");
        programSource.ShouldContain("AddEntraAuth");
        authSource.ShouldContain("AddJwtBearer");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var slnPath = Path.Combine(directory.FullName, "azure-aca-aspire-ai-starter.sln");
            if (File.Exists(slnPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }
}
