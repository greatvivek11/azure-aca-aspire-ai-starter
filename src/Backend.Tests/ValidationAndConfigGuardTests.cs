using System.Reflection;
using AcaAspireAiTemplate.Backend.Features.DocumentIngestion;
using AcaAspireAiTemplate.Backend.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

[Collection("EnvironmentVariableTests")]
public class ValidationAndConfigGuardTests
{
    [Fact]
    public void ResolveEntraAuthOptions_Should_Return_Disabled_When_Auth_Explicitly_Off()
    {
        var snapshot = CaptureEntraEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("ENTRA_AUTH_ENABLED", "false");
            Environment.SetEnvironmentVariable("ENTRA_TENANT_ID", null);
            Environment.SetEnvironmentVariable("ENTRA_API_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("ENTRA_AUTHORITY", null);
            Environment.SetEnvironmentVariable("ENTRA_AUDIENCE", null);

            var configuration = new ConfigurationBuilder().Build();
            var options = EntraAuthSetup.ResolveEntraAuthOptions(configuration);

            options.Enabled.ShouldBeFalse();
        }
        finally
        {
            RestoreEntraEnvironment(snapshot);
        }
    }

    [Fact]
    public void ResolveEntraAuthOptions_Should_Throw_When_Auth_Disabled_In_Production()
    {
        var snapshot = CaptureEntraEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("ENTRA_AUTH_ENABLED", "false");

            var configuration = new ConfigurationBuilder().Build();
            Should.Throw<InvalidOperationException>(() => EntraAuthSetup.ResolveEntraAuthOptions(configuration));
        }
        finally
        {
            RestoreEntraEnvironment(snapshot);
        }
    }

    [Fact]
    public void ResolveEntraAuthOptions_Should_Fallback_To_Disabled_When_Enabled_But_Required_Values_Missing_In_Development()
    {
        var snapshot = CaptureEntraEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("ENTRA_AUTH_ENABLED", "true");
            Environment.SetEnvironmentVariable("ENTRA_TENANT_ID", "");
            Environment.SetEnvironmentVariable("ENTRA_API_CLIENT_ID", "");
            Environment.SetEnvironmentVariable("ENTRA_AUTHORITY", null);
            Environment.SetEnvironmentVariable("ENTRA_AUDIENCE", null);

            var configuration = new ConfigurationBuilder().Build();
            var options = EntraAuthSetup.ResolveEntraAuthOptions(configuration);

            options.Enabled.ShouldBeFalse();
        }
        finally
        {
            RestoreEntraEnvironment(snapshot);
        }
    }

    [Fact]
    public void ResolveEntraAuthOptions_Should_Throw_When_Enabled_And_Required_Values_Missing_In_Production()
    {
        var snapshot = CaptureEntraEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("ENTRA_AUTH_ENABLED", "true");
            Environment.SetEnvironmentVariable("ENTRA_TENANT_ID", "");
            Environment.SetEnvironmentVariable("ENTRA_API_CLIENT_ID", "");
            Environment.SetEnvironmentVariable("ENTRA_AUTHORITY", null);
            Environment.SetEnvironmentVariable("ENTRA_AUDIENCE", null);

            var configuration = new ConfigurationBuilder().Build();

            Should.Throw<InvalidOperationException>(() => EntraAuthSetup.ResolveEntraAuthOptions(configuration));
        }
        finally
        {
            RestoreEntraEnvironment(snapshot);
        }
    }

    [Theory]
    [InlineData("report.pdf", null, "report.pdf")]
    [InlineData("notes.txt", null, "notes.txt")]
    [InlineData("contract.docx", null, "contract.docx")]
    [InlineData("archive.exe", "Only .txt, .pdf, and .docx files are supported for ingestion.", null)]
    [InlineData("../../etc/passwd", "Only .txt, .pdf, and .docx files are supported for ingestion.", null)]
    [InlineData("", "fileName is required.", null)]
    public void ValidateFileName_Should_Apply_File_Guards(string fileName, string? expectedError, string? expectedSafeName)
    {
        var endpointType = typeof(Endpoint);
        var method = endpointType.GetMethod("ValidateFileName", BindingFlags.Static | BindingFlags.NonPublic);
        method.ShouldNotBeNull("DocumentIngestion.ValidateFileName method was not found.");

        object?[] args = [fileName, null];
        var result = method!.Invoke(null, args);

        var error = result as string;
        var safeName = args[1] as string;

        if (expectedError is null)
        {
            error.ShouldBeNull();
            safeName.ShouldBe(expectedSafeName);
        }
        else
        {
            error.ShouldBe(expectedError);
        }
    }

    private static Dictionary<string, string?> CaptureEntraEnvironment()
    {
        return new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            ["ENTRA_AUTH_ENABLED"] = Environment.GetEnvironmentVariable("ENTRA_AUTH_ENABLED"),
            ["ENTRA_TENANT_ID"] = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID"),
            ["ENTRA_API_CLIENT_ID"] = Environment.GetEnvironmentVariable("ENTRA_API_CLIENT_ID"),
            ["ENTRA_AUTHORITY"] = Environment.GetEnvironmentVariable("ENTRA_AUTHORITY"),
            ["ENTRA_AUDIENCE"] = Environment.GetEnvironmentVariable("ENTRA_AUDIENCE")
        };
    }

    private static void RestoreEntraEnvironment(Dictionary<string, string?> snapshot)
    {
        foreach (var (key, value) in snapshot)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
