using Docker.DotNet;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Container.Provider;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Repository;

[Collection(nameof(IntegrationTestCollection))]
public sealed class TrainingContainerDockerTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task ThreeLabs_KeepDistinctDockerPortsAndPersistedInjectedFlags()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var client = new DockerClientConfiguration().CreateClient();
        var docker = new DockerManager(new TestDockerProvider(client), NullLogger<DockerManager>.Instance);
        var manager = new TrackingTestManager(context, docker);
        var repository = ActivatorUtilities.CreateInstance<ExerciseInstanceRepository>(scope.ServiceProvider, manager);
        var execution = scope.ServiceProvider.GetRequiredService<DeploymentExecutionContextAccessor>();
        var user = new UserInfo { UserName = $"docker{Guid.NewGuid():N}"[..16] };
        var course = new TrainingCourse { Title = "isolated training runtime regression", CreatedBy = user };
        context.Add(course);
        await context.SaveChangesAsync();
        var labs = Enumerable.Range(0, 3).Select(index => new ExerciseInstance
        {
            User = user, IsLoaded = true,
            Exercise = new ExerciseChallenge
            {
                Title = $"harmless runtime fixture {index}", Content = "No challenge payload.",
                TrainingCourseId = course.Id, IsEnabled = true, Type = ChallengeType.DynamicContainer,
                ContainerImage = "busybox:latest", ExposePort = 8080, FlagTemplate = null
            }
        }).ToArray();
        context.AddRange(labs);
        await context.SaveChangesAsync();

        try
        {
            foreach (var lab in labs)
            {
                using var current = execution.Push(new DeploymentExecutionContext(Guid.NewGuid(), true, Guid.NewGuid()));
                var result = await repository.CreateContainer(lab, user);
                Assert.Equal(GZCTF.Utils.TaskStatus.Success, result.Status);
                Assert.NotNull(result.Result);
            }
            Assert.Equal(3, labs.Select(lab => lab.ContainerId).Distinct().Count());
            Assert.All(labs, lab => Assert.True(lab.Container!.PublicPort > 0));
            Assert.Equal(3, labs.Select(lab => lab.Container!.PublicPort).Distinct().Count());
            Assert.Equal(3, labs.Select(lab => lab.FlagId).Distinct().Count());
            foreach (var lab in labs)
            {
                var info = await client.Containers.InspectContainerAsync(lab.Container!.ContainerId);
                Assert.True(info.State.Running);
                var stored = await context.FlagContexts.AsNoTracking().SingleAsync(flag => flag.Id == lab.FlagId);
                Assert.True(info.Config.Env.Contains($"GZCTF_FLAG={stored.Flag}"));
                Assert.Null(stored.ExerciseId);
                var check = await docker.ExecuteAsync(lab.Container,
                    ["sh", "-c", "test -s /tmp/injected-value && test \"$(cat /tmp/injected-value)\" = \"$GZCTF_FLAG\""],
                    TimeSpan.FromSeconds(10));
                Assert.True(check.Succeeded);
            }

            await docker.DestroyContainerAsync(labs[1].Container!);
            Assert.True((await client.Containers.InspectContainerAsync(labs[0].Container!.ContainerId)).State.Running);
            Assert.True((await client.Containers.InspectContainerAsync(labs[2].Container!.ContainerId)).State.Running);
        }
        finally
        {
            foreach (var container in manager.Created)
                await docker.DestroyContainerAsync(container);
        }
    }

    // Use the existing Docker bridge and a harmless fixture command, never a challenge image.
    sealed class TestDockerProvider(DockerClient client) : IContainerProvider<DockerClient, DockerMetadata>
    {
        public DockerClient GetProvider() => client;
        public DockerMetadata GetMetadata() => new()
        {
            PublicEntry = "127.0.0.1",
            NetworkNames = Enum.GetValues<NetworkMode>().ToDictionary(mode => mode, _ => "bridge")
        };
    }

    sealed class TrackingTestManager(AppDbContext context, DockerManager docker) : IContainerManager
    {
        public List<Container> Created { get; } = [];

        public async Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
        {
            config.StartCommand = "printf '%s' \"$GZCTF_FLAG\" > /tmp/injected-value; exec httpd -f -p 8080";
            var container = await docker.CreateContainerAsync(config, token);
            if (container is not null)
            {
                Created.Add(container);
                context.Containers.Add(container);
            }
            return container;
        }

        public Task DestroyContainerAsync(Container container, CancellationToken token = default) =>
            docker.DestroyContainerAsync(container, token);
    }
}
