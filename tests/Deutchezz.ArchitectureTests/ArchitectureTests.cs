using System.Reflection;
using NetArchTest.Rules;

namespace Deutchezz.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly AppAssembly = typeof(Deutchezz.Domain.Common.Result).Assembly;

    private static void AssertSuccessful(TestResult result) =>
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));

    [Fact]
    public void Domain_Should_Not_Depend_On_Application_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(AppAssembly)
            .That().ResideInNamespace("Deutchezz.Domain")
            .Should().NotHaveDependencyOnAny("Deutchezz.Application", "Deutchezz.Infrastructure", "Deutchezz.Api")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(AppAssembly)
            .That().ResideInNamespace("Deutchezz.Application")
            .Should().NotHaveDependencyOnAny("Deutchezz.Infrastructure", "Deutchezz.Api")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(AppAssembly)
            .That().ResideInNamespace("Deutchezz.Infrastructure")
            .Should().NotHaveDependencyOn("Deutchezz.Api")
            .GetResult();

        AssertSuccessful(result);
    }
}
