using System;
using System.Linq;
using GZCTF.Models;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Xunit;

namespace GZCTF.Test.UnitTests.Architecture;

public class ArchitectureDependencyTests
{
    [Fact]
    public void ModuleApiControllers_DoNotDependOnPersistenceOrAgent()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GZCTF.Modules", StringComparison.Ordinal) == true)
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(controllers);

        var forbidden = new[] { typeof(AppDbContext), typeof(AgentClient) };
        var violations = controllers
            .SelectMany(type => GetReferencedSurfaceTypes(type)
                .SelectMany(Flatten)
                    .Where(forbidden.Contains)
                    .Select(dependency => $"{type.FullName} -> {dependency.FullName}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ModuleDomainNamespaces_DoNotDependOnFrameworks()
    {
        var domainTypes = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GZCTF.Modules", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(domainTypes);

        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespaceMatching(@"GZCTF\.Modules\..*\.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "StackExchange.Redis",
                "Docker.DotNet")
            .GetResult();

        Assert.True(result.IsSuccessful,
            string.Join(", ", result.FailingTypes?.Select(type => type.FullName) ?? []));
    }

    [Fact]
    public void ModuleApplicationNamespaces_DoNotDependOnInfrastructure()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespaceMatching(@"GZCTF\.Modules\..*\.Application")
            .ShouldNot().HaveDependencyOnAny(
                "GZCTF.Modules.Identity.Infrastructure",
                "GZCTF.Modules.Audit.Infrastructure",
                "GZCTF.Modules.Content.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            string.Join(", ", result.FailingTypes?.Select(type => type.FullName) ?? []));
    }

    private static Type[] Flatten(Type type)
    {
        if (type.IsArray)
            return [type, .. Flatten(type.GetElementType()!)];
        if (!type.IsGenericType)
            return [type];

        return [type, .. type.GetGenericArguments().SelectMany(Flatten)];
    }

    private static Type[] GetReferencedSurfaceTypes(Type type) =>
    [
        .. type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType),
        .. type.GetFields(System.Reflection.BindingFlags.Instance |
                          System.Reflection.BindingFlags.Static |
                          System.Reflection.BindingFlags.Public |
                          System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType),
        .. type.GetProperties().Select(property => property.PropertyType),
        .. type.GetMethods(System.Reflection.BindingFlags.Instance |
                           System.Reflection.BindingFlags.Static |
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.NonPublic |
                           System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType)
                .Concat(method.GetMethodBody()?.LocalVariables.Select(local => local.LocalType) ?? []))
    ];
}
