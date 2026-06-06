# Architecture Tests

This document explains the architecture tests used to maintain code quality and enforce dependency boundaries in the AI Hub backend.

## Overview

**Architecture tests** validate that the codebase follows the intended design patterns and maintains proper separation of concerns. They use reflection to check project dependencies, namespace organization, and feature isolation.

**Why are they important?**
- Prevent accidental dependencies between layers
- Catch architectural violations early in CI/CD
- Document intended architecture through executable tests
- Support DDD and Vertical Slice Architecture patterns

---

## Test Framework

- **Test Framework**: [xUnit](https://xunit.net/) (C# testing framework)
- **Assertion Library**: [Shouldly](https://shouldly.io/) (fluent assertions)
- **Test Approach**: Reflection-based checks of assemblies and types

**Project location**: `src/Backend.Tests/`

---

## Test Categories

### 1. Dependency Boundary Tests

Ensure projects don't accidentally reference inappropriate dependencies.

#### Test: BackendProject_ShouldNotHaveDependencyOnFrontend

```csharp
[Fact]
public void BackendProject_ShouldNotHaveDependencyOnFrontend()
{
    // Arrange
    var backendReferences = GetProjectReferences(BackendAssembly);
    
    // Act
    var frontendReference = backendReferences.FirstOrDefault(r => 
        r.Name.Equals("Frontend", StringComparison.OrdinalIgnoreCase));
    
    // Assert
    frontendReference.ShouldBeNull("Backend should never depend on Frontend");
}
```

**Purpose**: Prevent backend code from importing frontend code (bidirectional dependency)

**Why it matters**: Frontend depends on Backend, not vice versa. If Backend imports Frontend, you have a circular dependency.

#### Test: BackendProject_ShouldHaveRequiredInfrastructureDependencies

```csharp
[Fact]
public void BackendProject_ShouldHaveRequiredInfrastructureDependencies()
{
    // Arrange
    var backendReferences = GetProjectReferences(BackendAssembly);
    var referencedNames = backendReferences.Select(r => r.Name).ToList();
    
    // Act & Assert
    referencedNames.Should().Contain("Dapr.AspNetCore");
    referencedNames.Should().Contain("Microsoft.Data.SqlClient");
    referencedNames.Should().Contain("SemanticKernel");
}
```

**Purpose**: Ensure required infrastructure libraries are present

**Why it matters**: Documents required runtime dependencies and catches accidental package removal

---

### 2. Namespace Organization Tests

Ensure the codebase follows Vertical Slice Architecture (VSA).

#### Test: BackendNamespaces_ShouldFollowVerticalSliceStructure

```csharp
[Fact]
public void BackendNamespaces_ShouldFollowVerticalSliceStructure()
{
    // Arrange
    var backendTypes = BackendAssembly.GetTypes();
    var namespaces = backendTypes.Select(t => t.Namespace).Distinct().ToList();
    
    // Act
    var hasFeatureNamespace = namespaces.Any(ns => ns?.Contains("Features") == true);
    var hasInfrastructureNamespace = namespaces.Any(ns => ns?.Contains("Infrastructure") == true);
    var hasDomainNamespace = namespaces.Any(ns => ns?.Contains("Domain") == true);
    
    // Assert
    hasFeatureNamespace.ShouldBeTrue();
    hasInfrastructureNamespace.ShouldBeTrue();
    hasDomainNamespace.ShouldBeTrue();
}
```

**Expected namespace structure:**
```
AIHub.Backend
├── Features
│   ├── Health
│   └── AiPing
├── Infrastructure
│   ├── Ai
│   └── Sql
└── Domain
    └── Customer.cs
```

**Why it matters**: 
- Enforces consistent project structure
- Supports code organization and discoverability
- Enables proper feature isolation

---

### 3. Feature Independence Tests

Ensure features don't depend on each other (preventing tangled dependencies).

#### Test: BackendFeatures_ShouldBeIndependent

```csharp
[Fact]
public void BackendFeatures_ShouldBeIndependent()
{
    // Arrange
    var backendTypes = BackendAssembly.GetTypes()
        .Where(t => t.Namespace?.Contains("Features") == true)
        .ToList();
    
    var featureGroups = backendTypes
        .GroupBy(t => t.Namespace?.Split('.')[3])  // Get feature name
        .ToDictionary(g => g.Key, g => g.ToList());
    
    // Act & Assert
    foreach (var (featureName, featureTypes) in featureGroups)
    {
        foreach (var featureType in featureTypes)
        {
            var otherFeatureReferences = GetReferencedTypes(featureType)
                .Where(rt => rt.Namespace?.Contains("Features") == true && 
                             !rt.Namespace.Contains(featureName ?? ""))
                .ToList();
            
            otherFeatureReferences.ShouldBeEmpty(
                $"Feature '{featureName}' should not depend on other features");
        }
    }
}
```

**Enforces**:
```
✅ Health feature can use Infrastructure
❌ Health feature cannot use AiPing feature
✅ AiPing feature can use Infrastructure
❌ AiPing feature cannot use Health feature
```

**Why it matters**:
- Prevents tangled, hard-to-understand dependencies
- Supports adding/removing/modifying features independently
- Enables parallel development on different features

---

## Running Tests

### From Command Line

```bash
# Run all tests
dotnet test copilot-sk.sln

# Run architecture tests only
dotnet test src/Backend.Tests/Backend.Tests.csproj

# Run with verbose output
dotnet test src/Backend.Tests/Backend.Tests.csproj --logger "console;verbosity=detailed"
```

### From Visual Studio / VS Code

1. Open Test Explorer: **View → Test Explorer** (or `Ctrl+E, T`)
2. Run tests:
   - **Run All**: Click the play icon
   - **Run Category**: Right-click `DependencyArchitectureTests`
   - **Run Single Test**: Right-click individual test

### In GitHub Actions

Tests run automatically in the `validate` job:

```yaml
- name: Run Architecture Tests
  run: dotnet test src/Backend.Tests/Backend.Tests.csproj --configuration Release --no-build
```

---

## Test Failure Examples

### Example 1: Accidentally Importing Frontend

**Test**: `BackendProject_ShouldNotHaveDependencyOnFrontend`  
**Failure**: If someone adds `using AIHub.Frontend;` to backend code

```
❌ Backend should never depend on Frontend
```

**Fix**: Remove the import

---

### Example 2: Missing Infrastructure Dependency

**Test**: `BackendProject_ShouldHaveRequiredInfrastructureDependencies`  
**Failure**: If `Microsoft.Data.SqlClient` NuGet package is accidentally removed

```
❌ Collection should contain "Microsoft.Data.SqlClient"
```

**Fix**: Restore the NuGet package

---

### Example 3: Feature Coupling

**Test**: `BackendFeatures_ShouldBeIndependent`  
**Failure**: If `Health` feature imports `AiPing` feature class

```
❌ Feature 'Health' should not depend on other features. 
   Found references: AiPing.Endpoint
```

**Fix**: Extract shared logic to Infrastructure layer instead

---

## Extending Architecture Tests

### Add a New Test

When adding architectural rules, create a new `[Fact]` method:

```csharp
[Fact]
public void SomeNewArchitecturalRule()
{
    // Arrange: Prepare data
    var types = GetRelevantTypes();
    
    // Act: Perform check
    var violations = PerformCheck(types);
    
    // Assert: Verify expectation
    violations.ShouldBeEmpty("Description of the rule");
}
```

### Example: Ensure All Endpoints Have Descriptive Names

```csharp
[Fact]
public void AllEndpointClasses_ShouldFollowNamingConvention()
{
    var endpointTypes = BackendAssembly.GetTypes()
        .Where(t => t.Name.EndsWith("Endpoint"))
        .ToList();
    
    var invalidNames = endpointTypes
        .Where(t => !t.Name.StartsWith("Get") && 
                    !t.Name.StartsWith("Post") && 
                    !t.Name.StartsWith("Put") && 
                    !t.Name.StartsWith("Delete"))
        .ToList();
    
    invalidNames.ShouldBeEmpty("Endpoints should follow verb-based naming");
}
```

---

## Best Practices

### ✅ Do

- Write tests for critical architectural rules
- Keep tests focused on one concern
- Use clear, descriptive test names
- Include comments explaining the "why"

### ❌ Don't

- Over-test implementation details
- Create tests that are brittle or hard to maintain
- Test framework behavior (test your code, not .NET)
- Create tests that fail due to naming conventions only

---

## Integration with DDD

For projects using Domain-Driven Design (DDD), consider adding tests for:

1. **Aggregate Root Consistency**
   ```csharp
   [Fact]
   public void AggregateRoots_ShouldNotReferenceDomainEvents()
   {
       // Ensure proper event handling pattern
   }
   ```

2. **Value Object Immutability**
   ```csharp
   [Fact]
   public void ValueObjects_ShouldHaveNoSetterProperties()
   {
       // All properties should be init-only
   }
   ```

3. **Bounded Context Isolation**
   ```csharp
   [Fact]
   public void OrderContext_ShouldNotDependOnCustomerContext()
   {
       // Enforce bounded context boundaries
   }
   ```

---

## References

- [xUnit Documentation](https://xunit.net/)
- [Shouldly GitHub](https://github.com/shouldly/shouldly)
- [Vertical Slice Architecture](https://jimmybogard.com/vertical-slice-architecture/)
- [Domain-Driven Design (Evans)](https://www.domainlanguage.com/ddd/)
- [Clean Architecture (Martin)](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)

---

## Troubleshooting

### ❌ Tests fail after architectural changes

**Solution**: Update the test to match the new architecture, then verify the design is intentional

### ❌ "System.Reflection" errors in tests

**Solution**: Ensure test is only checking public/internal members using appropriate `BindingFlags`

### ❌ Performance issues when running large test suites

**Solution**: Cache reflection results or consider splitting tests into focused categories
