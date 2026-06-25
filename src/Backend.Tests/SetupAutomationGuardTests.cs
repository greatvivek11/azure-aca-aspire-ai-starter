using System.Text.Json;
using Shouldly;
using Xunit;

namespace AcaAspireAiTemplate.Backend.Tests.Architecture;

public class SetupAutomationGuardTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void TasksJson_Should_Include_Native_Llama_Readiness_Gate_In_Setup_Chain()
    {
        var tasksPath = Path.Combine(RepositoryRoot, ".vscode", "tasks.json");
        File.Exists(tasksPath).ShouldBeTrue("Expected .vscode/tasks.json to exist.");

        using var doc = JsonDocument.Parse(File.ReadAllText(tasksPath));
        var tasks = doc.RootElement.GetProperty("tasks").EnumerateArray().ToArray();

        var setupAllTools = tasks.Single(t => t.GetProperty("label").GetString() == "setup: install all tools");
        setupAllTools.GetProperty("dependsOrder").GetString().ShouldBe("sequence");

        var depends = setupAllTools
            .GetProperty("dependsOn")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToArray();

        depends.ShouldContain("setup: ensure native llama.cpp");
        depends.ShouldContain("validate: native llama.cpp ready");

        var nativeIndex = Array.IndexOf(depends, "setup: ensure native llama.cpp");
        var readyIndex = Array.IndexOf(depends, "validate: native llama.cpp ready");
        readyIndex.ShouldBeGreaterThan(nativeIndex, "Readiness check must run after native llama setup.");

        foreach (var dependencyLabel in depends)
        {
            var dependencyTask = tasks.Single(t => t.GetProperty("label").GetString() == dependencyLabel);
            if (dependencyTask.TryGetProperty("runOptions", out var runOptions)
                && runOptions.TryGetProperty("runOn", out var runOn))
            {
                runOn.GetString().ShouldNotBe("folderOpen", $"{dependencyLabel} must run through setup: install all tools to preserve ordering.");
            }
        }

        tasks.Any(t => t.GetProperty("label").GetString() == "validate: native llama.cpp ready")
            .ShouldBeTrue("Missing readiness task in .vscode/tasks.json.");
    }

    [Fact]
    public void TasksJson_Should_Include_FolderOpen_Sql_Profile_Setup_Task()
    {
        var tasksPath = Path.Combine(RepositoryRoot, ".vscode", "tasks.json");
        File.Exists(tasksPath).ShouldBeTrue("Expected .vscode/tasks.json to exist.");

        using var doc = JsonDocument.Parse(File.ReadAllText(tasksPath));
        var tasks = doc.RootElement.GetProperty("tasks").EnumerateArray().ToArray();

        var sqlSetupTask = tasks.Single(t => t.GetProperty("label").GetString() == "setup: ensure SQL Server connection profile");

        sqlSetupTask.GetProperty("runOptions").GetProperty("runOn").GetString().ShouldBe("folderOpen");
        sqlSetupTask.GetProperty("dependsOrder").GetString().ShouldBe("sequence");

        var depends = sqlSetupTask
            .GetProperty("dependsOn")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToArray();

        depends.ShouldContain("setup: install all extensions");
        depends.ShouldContain("tools: ensure Docker engine");
    }

    [Theory]
    [InlineData(".vscode/ensure-local-llm.ps1")]
    [InlineData(".vscode/ensure-local-llm.sh")]
    public void Native_Setup_Scripts_Should_Install_And_Start_Native_Llama(string relativePath)
    {
        var scriptPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(scriptPath).ShouldBeTrue($"Expected setup script not found: {relativePath}");

        var source = File.ReadAllText(scriptPath);
        source.ShouldContain("LLAMA_CPP_BASE_URL");
        source.ShouldContain("LLAMA_CPP_EMBED_BASE_URL");
        source.ShouldContain("LLAMA_CPP_CHAT_MODEL_FILE");
        source.ShouldContain("LLAMA_CPP_EMBED_MODEL_FILE");
        source.ShouldContain("Qwen/Qwen2.5-0.5B-Instruct");
        source.ShouldContain("Qwen2.5-0.5B-Instruct-Q4_K_M.gguf");
        source.ShouldContain("unsloth/gemma-3-1b-it-GGUF");
        source.ShouldContain("Qwen2.5-1.5B-Instruct-Q4_K_M.gguf");
        source.ShouldContain("Llama-3.2-1B-Instruct-Q4_K_M.gguf");
        source.ShouldContain("LLAMA_CPP_CHAT_MODEL");
        source.ShouldContain("llama-server");
        source.ShouldContain("/health");
    }

    [Theory]
    [InlineData(".vscode/ensure-aspire-env.ps1")]
    [InlineData(".vscode/ensure-aspire-env.sh")]
    [InlineData(".vscode/ensure-docker.ps1")]
    [InlineData(".vscode/ensure-docker.sh")]
    [InlineData(".vscode/ensure-local-llm.ps1")]
    [InlineData(".vscode/ensure-local-llm.sh")]
    [InlineData(".vscode/ensure-local-llm-ready.ps1")]
    [InlineData(".vscode/ensure-local-llm-ready.sh")]
    [InlineData(".vscode/install-dapr.ps1")]
    [InlineData(".vscode/install-extensions.ps1")]
    [InlineData(".vscode/ensure-sql-connection-profile.ps1")]
    [InlineData(".vscode/ensure-sql-connection-profile.sh")]
    public void Vscode_Setup_Scripts_Should_Be_Allowed_By_Gitignore(string relativePath)
    {
        var scriptPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(scriptPath).ShouldBeTrue($"Expected VS Code setup helper not found: {relativePath}");

        var gitignorePath = Path.Combine(RepositoryRoot, ".gitignore");
        File.Exists(gitignorePath).ShouldBeTrue("Expected .gitignore to exist.");

        var expectedException = $"!{relativePath}";
        File.ReadAllLines(gitignorePath).ShouldContain(
            expectedException,
            $"{relativePath} is referenced by .vscode/tasks.json and must not be ignored.");
    }

    [Theory]
    [InlineData(".vscode/ensure-local-llm-ready.ps1")]
    [InlineData(".vscode/ensure-local-llm-ready.sh")]
    public void Readiness_Scripts_Should_Check_Both_Chat_And_Embedding_Endpoints(string relativePath)
    {
        var scriptPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(scriptPath).ShouldBeTrue($"Expected readiness script not found: {relativePath}");

        var source = File.ReadAllText(scriptPath);
        source.ShouldContain("LLAMA_CPP_BASE_URL");
        source.ShouldContain("LLAMA_CPP_EMBED_BASE_URL");
        source.ShouldContain("8082");
        source.ShouldContain("8083");
        source.ShouldContain("/health");
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
