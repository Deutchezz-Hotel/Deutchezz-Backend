using Deutchezz.Application;
using Deutchezz.Domain.Common;
using Deutchezz.Infrastructure;
using NetArchTest.Rules;

namespace Deutchezz.ArchitectureTests;

public class ArchitectureTests
{
    private const string DomainAssembly = "Domain";
    private const string ApplicationAssembly = "Application";
    private const string InfrastructureAssembly = "Infrastructure";
    private const string ApiAssembly = "Api";

    [Fact]
    public void Domain_Should_Not_Have_Dependencies()
    {
        var types = Types.InAssembly(typeof(Result).Assembly);
        var result = types.Should().NotHaveDependencyOnAny(InfrastructureAssembly, ApiAssembly, ApplicationAssembly).GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var types = Types.InAssembly(typeof(Deutchezz.Application.DependencyInjection).Assembly);
        var result = types.Should().NotHaveDependencyOnAny(InfrastructureAssembly, ApiAssembly).GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var types = Types.InAssembly(typeof(Deutchezz.Infrastructure.DependencyInjection).Assembly);
        var result = types.Should().NotHaveDependencyOnAny(ApiAssembly).GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }
}

public static class DependencyInjectionInfrastructureMarker { } // Marker for test to reference Infrastructure assembly
