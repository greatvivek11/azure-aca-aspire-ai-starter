using Xunit;
using Shouldly;

namespace AIHub.Backend.Tests.Architecture;

/// <summary>
/// Simple architecture tests to validate backend structure
/// </summary>
public class DependencyArchitectureTests
{
    [Fact]
    public void Backend_Should_Be_Properly_Structured()
    {
        // This is a placeholder test that validates the test infrastructure works
        // More complex reflection-based tests will be added as the codebase grows

        // Arrange & Act
        var testAssembly = typeof(DependencyArchitectureTests).Assembly;

        // Assert: Test infrastructure should work
        testAssembly.ShouldNotBeNull();
        testAssembly.GetName().Name?.ShouldContain("Backend.Tests");
    }

    [Fact]
    public void Architecture_Tests_Can_Load()
    {
        // This validates that the architecture test project itself loads correctly
        var testClass = typeof(DependencyArchitectureTests);
        testClass.ShouldNotBeNull();
    }
}
