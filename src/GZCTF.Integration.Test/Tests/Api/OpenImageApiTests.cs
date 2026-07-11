using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Training.Domain;
using GZCTF.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class OpenImageApiTests(GZCTFApplicationFactory factory) : IAsyncLifetime
{
    private const string TestPassword = "0penImage!Pass123";

    public Task InitializeAsync() =>
        ContainerHelper.SetLocalNodeSchedulingAsync(factory.Services, true);

    public Task DisposeAsync() =>
        ContainerHelper.SetLocalNodeSchedulingAsync(factory.Services, false);

    [Fact]
    public async Task RegisterDockerReference_IsIdempotentAndAudited()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite, ApiTokenScopes.OperationsRead]);
        var key = Guid.NewGuid().ToString("N");
        var request = NewRequest($"image-{key[..8]}", "docker.io/library/alpine:3.20");

        var responses = await Task.WhenAll(
            PostReferenceAsync(issued.PlainTextToken, key, request),
            PostReferenceAsync(issued.PlainTextToken, key, request));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstOperation = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondOperation = await second.Content.ReadFromJsonAsync<JsonElement>();
        var operationId = firstOperation.GetProperty("id").GetGuid();
        Assert.Equal(operationId, secondOperation.GetProperty("id").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.ApiOperations.CountAsync(operation =>
            operation.Id == operationId &&
            operation.Kind == "image.import" &&
            operation.ApiTokenId == issued.TokenId));
        Assert.Equal(1, await context.ImageImportJobs.CountAsync(
            job => job.OperationId == operationId));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            context.ChangeTracker.Clear();
            var operation = await context.ApiOperations.AsNoTracking()
                .SingleAsync(item => item.Id == operationId);
            if (operation.Status == ApiOperationStatus.Succeeded)
            {
                var job = await context.ImageImportJobs.AsNoTracking()
                    .SingleAsync(item => item.OperationId == operationId);
                Assert.NotNull(job.ImageTemplateId);
                var template = await context.ImageTemplates.AsNoTracking()
                    .SingleAsync(item => item.Id == job.ImageTemplateId);
                Assert.Equal(issued.CreatorId, template.CreatedById);
                Assert.StartsWith("gzctf-internal://", template.RegistryUrl);
                var executor = scope.ServiceProvider.GetRequiredService<
                    Fixtures.FakeImageImportExecutor>();
                Assert.Equal(1, executor.ExecutionCount(operationId));

                var recoveryOwner = $"recovery-{Guid.NewGuid():N}";
                await context.ApiOperations.Where(item => item.Id == operationId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, ApiOperationStatus.Running)
                        .SetProperty(item => item.Stage, "recovering")
                        .SetProperty(item => item.LeaseOwner, recoveryOwner)
                        .SetProperty(item => item.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(1))
                        .SetProperty(item => item.CompletedAt, (DateTimeOffset?)null));
                await using var recoveryScope = factory.Services.CreateAsyncScope();
                var handler = recoveryScope.ServiceProvider
                    .GetServices<IApiOperationHandler>()
                    .Single(item => item.Kind == "image.import");
                await handler.ExecuteAsync(operationId, recoveryOwner, CancellationToken.None);
                Assert.Equal(1, executor.ExecutionCount(operationId));
                var operationService = recoveryScope.ServiceProvider
                    .GetRequiredService<ApiOperationService>();
                Assert.True(await operationService.CompleteAsync(
                    operationId,
                    recoveryOwner,
                    "image-template",
                    template.Id.ToString(),
                    CancellationToken.None));
                return;
            }

            Assert.NotEqual(ApiOperationStatus.Failed, operation.Status);
            await Task.Delay(100);
        }

        throw new TimeoutException("The durable image import operation did not complete.");
    }

    [Fact]
    public async Task RegisterDockerReference_RejectsChangedPayloadForSameKey()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite]);
        var key = Guid.NewGuid().ToString("N");

        using var first = await PostReferenceAsync(
            issued.PlainTextToken,
            key,
            NewRequest($"first-{key[..8]}", "docker.io/library/alpine:3.20"));
        using var conflict = await PostReferenceAsync(
            issued.PlainTextToken,
            key,
            NewRequest($"second-{key[..8]}", "docker.io/library/busybox:1.36"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await AssertExternalProblemAsync(conflict, HttpStatusCode.Conflict, "idempotency_conflict");
    }

    [Fact]
    public async Task RegisterDockerReference_RequiresWriteScope()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesRead]);

        using var response = await PostReferenceAsync(
            issued.PlainTextToken,
            Guid.NewGuid().ToString("N"),
            NewRequest("scope-denied", "docker.io/library/alpine:3.20"));

        await AssertExternalProblemAsync(response, HttpStatusCode.Forbidden, "insufficient_permission");
    }

    [Fact]
    public async Task RegisterDockerReference_EnforcesImageResourceGrant()
    {
        var allowedName = $"allowed-{Guid.NewGuid():N}";
        var issued = await IssueTokenAsync(
            [ApiTokenScopes.ImagesWrite],
            [new ApiTokenResourceGrantSpec("image", allowedName)]);

        using var denied = await PostReferenceAsync(
            issued.PlainTextToken,
            Guid.NewGuid().ToString("N"),
            NewRequest("denied", "docker.io/library/alpine:3.20"));
        using var allowed = await PostReferenceAsync(
            issued.PlainTextToken,
            Guid.NewGuid().ToString("N"),
            NewRequest(allowedName, "docker.io/library/alpine:3.20"));

        await AssertExternalProblemAsync(denied, HttpStatusCode.Forbidden, "insufficient_permission");
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    [Fact]
    public async Task RegisterDockerReference_RejectsPrivateRegistry()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite]);

        using var response = await PostReferenceAsync(
            issued.PlainTextToken,
            Guid.NewGuid().ToString("N"),
            NewRequest("private-registry", "10.24.0.31:5000/labs/private:latest"));

        await AssertExternalProblemAsync(
            response,
            HttpStatusCode.UnprocessableEntity,
            "image_reference_forbidden");
    }

    [Fact]
    public async Task OpenImage_GetAndDelete_UseDedicatedScopesAndPersistAudits()
    {
        var issued = await IssueTokenAsync([
            ApiTokenScopes.ImagesRead,
            ApiTokenScopes.ImagesWrite,
            ApiTokenScopes.ImagesDelete
        ]);
        var key = Guid.NewGuid().ToString("N");
        using var imported = await PostReferenceAsync(
            issued.PlainTextToken,
            key,
            NewRequest($"read-delete-{key[..8]}", "docker.io/library/alpine:3.20"));
        Assert.Equal(HttpStatusCode.Accepted, imported.StatusCode);
        var operation = await imported.Content.ReadFromJsonAsync<JsonElement>();
        var operationId = operation.GetProperty("id").GetGuid();
        var templateId = await WaitForImportedTemplateAsync(operationId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", issued.PlainTextToken);
        using var read = await client.GetAsync($"/api/open/v1/images/{templateId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var model = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(templateId, model.GetProperty("id").GetInt32());

        int courseId;
        await using (var bindingScope = factory.Services.CreateAsyncScope())
        {
            var bindingContext = bindingScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = new TrainingCourse
            {
                Title = $"image-reference-{key[..8]}",
                Slug = $"image-reference-{key[..8]}",
                CreatedById = issued.CreatorId,
                UpdatedById = issued.CreatorId
            };
            bindingContext.TrainingCourses.Add(course);
            await bindingContext.SaveChangesAsync();
            courseId = course.Id;
            bindingContext.TrainingCourseImageTemplateBindings.Add(
                new TrainingCourseImageTemplateBinding
                {
                    CourseId = courseId,
                    ImageTemplateId = templateId,
                    AddedById = issued.CreatorId
                });
            await bindingContext.SaveChangesAsync();
        }

        using var inUse = await client.DeleteAsync($"/api/open/v1/images/{templateId}");
        await AssertExternalProblemAsync(inUse, HttpStatusCode.Conflict, "asset_in_use");
        await using (var unbindScope = factory.Services.CreateAsyncScope())
        {
            var unbindContext = unbindScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await unbindContext.TrainingCourses.Where(course => course.Id == courseId).ExecuteDeleteAsync();
        }

        using var deleted = await client.DeleteAsync($"/api/open/v1/images/{templateId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.ImageTemplates.AnyAsync(template => template.Id == templateId));
        Assert.True(await context.ExternalApiRequestAudits.AnyAsync(audit =>
            audit.OperationId == operationId &&
            audit.ApiTokenId == issued.TokenId &&
            audit.IdempotencyReused == false));
        Assert.True(await context.ExternalApiRequestAudits.AnyAsync(audit =>
            audit.ApiTokenId == issued.TokenId &&
            audit.Method == "DELETE" &&
            audit.ResourceType == "image" &&
            audit.ResourceId == templateId.ToString() &&
            audit.StatusCode == StatusCodes.Status204NoContent));
    }

    [Fact]
    public async Task RegisterDockerArchive_PersistsStagingAndCompletesWithoutRequestReplay()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite, ApiTokenScopes.OperationsRead]);
        var key = Guid.NewGuid().ToString("N");
        var archive = "durable docker archive payload"u8.ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(archive));

        using var response = await PostArchiveAsync(
            issued.PlainTextToken,
            key,
            $"archive-{key[..8]}",
            archive,
            digest);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var operationModel = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operationId = operationModel.GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            context.ChangeTracker.Clear();
            var operation = await context.ApiOperations.AsNoTracking()
                .SingleAsync(item => item.Id == operationId);
            if (operation.Status == ApiOperationStatus.Succeeded)
            {
                var job = await context.ImageImportJobs.AsNoTracking()
                    .SingleAsync(item => item.OperationId == operationId);
                Assert.Equal(ImageImportSourceKind.DockerArchive, job.SourceKind);
                Assert.Equal("sample.tar", job.OriginalFileName);
                Assert.Equal(archive.LongLength, job.ContentLength);
                Assert.Equal(digest, job.ExpectedDigest);
                Assert.NotNull(job.ImageTemplateId);
                Assert.NotNull(job.StagedPath);
                Assert.False(File.Exists(job.StagedPath));

                var executor = scope.ServiceProvider.GetRequiredService<
                    Fixtures.FakeImageImportExecutor>();
                Assert.Equal(1, executor.ExecutionCount(operationId));

                var recoveryOwner = $"archive-recovery-{Guid.NewGuid():N}";
                await context.ApiOperations.Where(item => item.Id == operationId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, ApiOperationStatus.Running)
                        .SetProperty(item => item.Stage, "recovering")
                        .SetProperty(item => item.LeaseOwner, recoveryOwner)
                        .SetProperty(item => item.LeaseExpiresAt, DateTimeOffset.UtcNow.AddMinutes(1))
                        .SetProperty(item => item.CompletedAt, (DateTimeOffset?)null));
                await using var recoveryScope = factory.Services.CreateAsyncScope();
                var handler = recoveryScope.ServiceProvider
                    .GetServices<IApiOperationHandler>()
                    .Single(item => item.Kind == "image.import");
                await handler.ExecuteAsync(operationId, recoveryOwner, CancellationToken.None);
                Assert.Equal(1, executor.ExecutionCount(operationId));
                return;
            }

            Assert.NotEqual(ApiOperationStatus.Failed, operation.Status);
            await Task.Delay(100);
        }

        throw new TimeoutException("The durable Docker archive import did not complete.");
    }

    [Fact]
    public async Task RegisterDockerArchive_RecoversAfterWebHostRestart()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(), $"gzctf-restart-{Guid.NewGuid():N}");
        await using var database = await IsolatedPostgresDatabase.CreateAsync(
            factory.DatabaseConnectionString);
        var connectionString = database.ConnectionString;
        Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot"));
        await File.WriteAllTextAsync(
            Path.Combine(contentRoot, "wwwroot", "index.html"),
            "<!doctype html><html><body>test</body></html>");

        try
        {
            var blockingExecutor = new Fixtures.BlockingImageImportExecutor();
            Guid operationId;
            string stagedPath;

            await using (var firstHost = CreateRestartHost(
                             connectionString, contentRoot, blockingExecutor))
            {
                var issued = await IssueTokenAsync(
                    firstHost.Services,
                    [ApiTokenScopes.ImagesWrite, ApiTokenScopes.OperationsRead]);
                using var client = firstHost.CreateClient();
                var archive = "restart recovery archive"u8.ToArray();
                using var response = await PostArchiveAsync(
                    client,
                    issued.PlainTextToken,
                    Guid.NewGuid().ToString("N"),
                    $"restart-{Guid.NewGuid():N}",
                    archive,
                    Convert.ToHexStringLower(SHA256.HashData(archive)));
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                var operationModel = await response.Content.ReadFromJsonAsync<JsonElement>();
                operationId = operationModel.GetProperty("id").GetGuid();

                Assert.Equal(
                    operationId,
                    await blockingExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                await using var context = database.CreateContext();
                stagedPath = await context.ImageImportJobs.AsNoTracking()
                    .Where(job => job.OperationId == operationId)
                    .Select(job => job.StagedPath!)
                    .SingleAsync();
                Assert.True(File.Exists(stagedPath));
            }

            await using (var context = database.CreateContext())
            {
                await context.ApiOperations
                    .Where(operation => operation.Id == operationId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(operation => operation.LeaseExpiresAt,
                            DateTimeOffset.UtcNow.AddSeconds(-1)));
            }

            var recoveryExecutor = new Fixtures.FakeImageImportExecutor();
            await using (var secondHost = CreateRestartHost(
                             connectionString, contentRoot, recoveryExecutor))
            {
                using var client = secondHost.CreateClient();
                var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using var context = database.CreateContext();
                    var operation = await context.ApiOperations.AsNoTracking()
                        .SingleAsync(item => item.Id == operationId);
                    if (operation.Status == ApiOperationStatus.Succeeded)
                    {
                        var job = await context.ImageImportJobs.AsNoTracking()
                            .SingleAsync(item => item.OperationId == operationId);
                        Assert.NotNull(job.ImageTemplateId);
                        Assert.Equal(1, recoveryExecutor.ExecutionCount(operationId));
                        Assert.False(File.Exists(stagedPath));
                        return;
                    }

                    Assert.NotEqual(ApiOperationStatus.Failed, operation.Status);
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException("The restarted host did not recover the image import.");
        }
        finally
        {
            if (Directory.Exists(contentRoot))
                Directory.Delete(contentRoot, true);
        }
    }

    [Fact]
    public async Task RegisterDockerArchive_RejectsChangedPayloadForSameKey()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite]);
        var key = Guid.NewGuid().ToString("N");
        var name = $"archive-conflict-{key[..8]}";
        var firstArchive = "first archive payload"u8.ToArray();
        var changedArchive = "changed archive payload"u8.ToArray();

        using var first = await PostArchiveAsync(
            issued.PlainTextToken,
            key,
            name,
            firstArchive,
            Convert.ToHexStringLower(SHA256.HashData(firstArchive)));
        using var conflict = await PostArchiveAsync(
            issued.PlainTextToken,
            key,
            name,
            changedArchive,
            Convert.ToHexStringLower(SHA256.HashData(changedArchive)));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await AssertExternalProblemAsync(conflict, HttpStatusCode.Conflict, "idempotency_conflict");

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.ApiOperations.CountAsync(operation =>
            operation.ApiTokenId == issued.TokenId &&
            operation.IdempotencyKey == key));
    }

    [Fact]
    public async Task RegisterDockerArchive_RejectsDigestMismatchBeforePersistingOperation()
    {
        var issued = await IssueTokenAsync([ApiTokenScopes.ImagesWrite]);
        var key = Guid.NewGuid().ToString("N");

        using var response = await PostArchiveAsync(
            issued.PlainTextToken,
            key,
            $"archive-digest-{key[..8]}",
            "archive payload"u8.ToArray(),
            new string('0', 64));

        await AssertExternalProblemAsync(response, HttpStatusCode.BadRequest, "image_digest_mismatch");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.ApiOperations.AnyAsync(operation =>
            operation.ApiTokenId == issued.TokenId &&
            operation.IdempotencyKey == key));
    }

    [Fact]
    public async Task ImportedTemplate_EnforcesOwnerDeletionThroughBrowserApi()
    {
        var owner = await IssueTokenAsync([ApiTokenScopes.ImagesWrite]);
        var foreign = await IssueTokenAsync([ApiTokenScopes.ImagesRead]);
        var key = Guid.NewGuid().ToString("N");
        using var response = await PostReferenceAsync(
            owner.PlainTextToken,
            key,
            NewRequest($"owned-{key[..8]}", "docker.io/library/alpine:3.20"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = await WaitForImportedTemplateAsync(operation.GetProperty("id").GetGuid());

        using var foreignClient = factory.CreateClient();
        await LoginAsync(foreignClient, foreign);
        using var forbidden = await foreignClient.DeleteAsync($"/api/v1/image-templates/{templateId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var ownerClient = factory.CreateClient();
        await LoginAsync(ownerClient, owner);
        using var deleted = await ownerClient.DeleteAsync($"/api/v1/image-templates/{templateId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.ImageTemplates.AnyAsync(template => template.Id == templateId));
    }

    private Task<HttpResponseMessage> PostReferenceAsync(
        string token,
        string idempotencyKey,
        object request)
    {
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/open/v1/images/docker-references")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return factory.CreateClient().SendAsync(message);
    }

    private Task<HttpResponseMessage> PostArchiveAsync(
        string token,
        string idempotencyKey,
        string name,
        byte[] archive,
        string expectedDigest)
    {
        return PostArchiveAsync(
            factory.CreateClient(), token, idempotencyKey, name, archive, expectedDigest);
    }

    private static Task<HttpResponseMessage> PostArchiveAsync(
        HttpClient client,
        string token,
        string idempotencyKey,
        string name,
        byte[] archive,
        string expectedDigest)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(archive), "file", "sample.tar" },
            { new StringContent(name), "name" },
            { new StringContent("alpine:3.20"), "sourceImage" },
            { new StringContent("0"), "osType" },
            { new StringContent(expectedDigest), "expectedDigest" }
        };
        content.First().Headers.ContentType = new MediaTypeHeaderValue("application/x-tar");
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/open/v1/images/docker-archives")
        {
            Content = content
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(message);
    }

    private static object NewRequest(string name, string registryUrl) => new
    {
        name,
        registryUrl,
        osType = 0
    };

    private static async Task AssertExternalProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem["code"].GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem["traceId"].GetString()));
    }

    private async Task<int> WaitForImportedTemplateAsync(Guid operationId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await context.ApiOperations.AsNoTracking()
                .SingleAsync(item => item.Id == operationId);
            if (operation.Status == ApiOperationStatus.Succeeded)
            {
                var templateId = await context.ImageImportJobs.AsNoTracking()
                    .Where(job => job.OperationId == operationId)
                    .Select(job => job.ImageTemplateId)
                    .SingleAsync();
                return Assert.IsType<int>(templateId);
            }

            Assert.NotEqual(ApiOperationStatus.Failed, operation.Status);
            await Task.Delay(100);
        }

        throw new TimeoutException("The image import did not complete.");
    }

    private static async Task LoginAsync(HttpClient client, IssuedToken issued)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/Account/LogIn",
            new LoginModel { UserName = issued.UserName, Password = issued.Password });
        response.EnsureSuccessStatusCode();
    }

    private async Task<IssuedToken> IssueTokenAsync(
        IReadOnlyCollection<string> scopes,
        IReadOnlyCollection<ApiTokenResourceGrantSpec>? resources = null) =>
        await IssueTokenAsync(factory.Services, scopes, resources);

    private static async Task<IssuedToken> IssueTokenAsync(
        IServiceProvider services,
        IReadOnlyCollection<string> scopes,
        IReadOnlyCollection<ApiTokenResourceGrantSpec>? resources = null)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var issuer = scope.ServiceProvider.GetRequiredService<ApiTokenIssuer>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new UserInfo
        {
            Id = Guid.CreateVersion7(),
            UserName = $"oi-{suffix}",
            NormalizedUserName = $"OI-{suffix.ToUpperInvariant()}",
            Email = $"open-image-{suffix}@example.test",
            NormalizedEmail = $"OPEN-IMAGE-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Teacher,
            RegisterTimeUtc = DateTimeOffset.UtcNow
        };
        var created = await userManager.CreateAsync(user, TestPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var issued = await issuer.IssueAsync(
            new ActorContext(user.Id, user.Role),
            new IssueApiTokenCommand(
                "open image",
                scopes,
                resources ?? [],
                60,
                DateTimeOffset.UtcNow.AddHours(1)),
            CancellationToken.None);
        return new IssuedToken(
            issued.PlainTextToken,
            issued.Token.Id,
            user.Id,
            user.UserName!,
            TestPassword);
    }

    private WebApplicationFactory<Program> CreateRestartHost(
        string connectionString,
        string contentRoot,
        GZCTF.Modules.Content.Application.IImageImportExecutor executor) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(contentRoot);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = connectionString,
                    ["Agent:LocalNodeSchedulable"] = "true"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
                services.RemoveAll<GZCTF.Modules.Content.Application.IImageImportExecutor>();
                services.AddSingleton(executor);
            });
        });

    private sealed record IssuedToken(
        string PlainTextToken,
        Guid TokenId,
        Guid CreatorId,
        string UserName,
        string Password);
}
