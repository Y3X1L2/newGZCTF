using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabFoundationBoundaryTests
{
    [Fact]
    public void TeamLabModule_DoesNotReferencePenetrationDomainOrDtos()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "PenetrationConfig", "PenetrationNode", "PenetrationNetwork",
            "PenetrationInterface", "PenetrationEdge", "PenetrationConfigModel"
        };
        var offenders = typeof(TeamLabTopologyApplicationService).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GZCTF.Modules.TeamLab", StringComparison.Ordinal) == true)
            .SelectMany(type => ReferencedTypes(type).Select(reference => (Type: type, Reference: reference)))
            .Where(item => forbidden.Contains(Unwrap(item.Reference).Name))
            .Select(item => $"{item.Type.FullName} -> {Unwrap(item.Reference).FullName}")
            .Distinct()
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TeamLabApplication_DoesNotDependOnAgentClient()
    {
        var offenders = typeof(TeamLabTopologyApplicationService).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GZCTF.Modules.TeamLab.Application", StringComparison.Ordinal) == true)
            .Where(type => type.GetConstructors().SelectMany(ctor => ctor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(AgentClient)))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(type.GetProperties().Select(property => property.PropertyType))
            .Concat(type.GetMethods().SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)));

    private static Type Unwrap(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray) return Unwrap(type.GetElementType()!);
        if (type.IsGenericType)
            return type.GetGenericArguments().Select(Unwrap).FirstOrDefault(argument => argument.Namespace is not null) ?? type;
        return type;
    }
}
