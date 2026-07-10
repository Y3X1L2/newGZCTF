using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GZCTF.Test.UnitTests.Phase;

public class LegacySurfaceRemovalTests
{
    private static readonly string[] RemovedTypeNames =
    [
        "IRCheckpoint",
        "IRInstance",
        "Stage",
        "StageStatus",
        "ScenarioInstance",
        "ScenarioInstanceStatus",
        "ScenarioTimelineEntry",
        "ScoringRule",
        "TimeSlot",
        "TrainingDirection",
        "TrainingType",
        "TrainingModule",
        "TrainingCompletionRule",
        "TrainingVisibilityType",
        "TrainingModuleProgressStatus",
        "TrainingModuleVisibility",
        "TrainingModuleChallenge",
        "TrainingCtfSubmission",
        "TheoryTrainingMode",
        "TheoryTrainingPlan",
        "TheoryTrainingPlanQuestion",
        "TheoryTrainingSessionStatus",
        "TheoryTrainingSession",
        "TheoryTrainingSessionQuestion",
        "TrainingArticleProgress",
        "TrainingModuleProgress"
    ];

    [Fact]
    public void RuntimeAssembly_DoesNotContainRemovedLegacyTypes()
    {
        var runtimeTypeNames = typeof(Program).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var remainingTypes = RemovedTypeNames
            .Where(runtimeTypeNames.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(remainingTypes);
    }

    [Fact]
    public void Controllers_ExposeOnlyCourseTrainingRouteRoots()
    {
        var routeRoots = typeof(Program).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>())
            .Select(attribute => attribute.Template)
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var trainingRouteRoots = routeRoots
            .Where(route => route!.StartsWith("api/training", StringComparison.OrdinalIgnoreCase) ||
                            route.StartsWith("api/admin/training", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(trainingRouteRoots.SetEquals([
            "api/training/courses",
            "api/admin/training/courses"
        ]), $"Unexpected training route roots: {string.Join(", ", trainingRouteRoots.Order())}");
    }
}
