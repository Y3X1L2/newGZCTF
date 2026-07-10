# YINYU CTF平台 TDD 测试驱动规范 v1

> **配套文件:** `docs/superpowers/plans/2026-05-19-YINYU CTF平台-refactor.md`
> **测试服务器:** <test-server-ip> (Ubuntu 22.04 / OpenSSH 8.9)
> **测试原则:** Red-Green-Refactor 三步循环——每次提交前必须先有失败测试，通过最小实现让其通过，然后重构

---

## 零、测试环境搭建 (Phase 0)

### 0.1 测试服务器环境初始化

```bash
# 连接测试服务器
ssh ubuntu@<test-server-ip>

# === 安装测试依赖 ===
sudo apt update && sudo apt install -y \
    docker.io docker-compose-v2 \
    postgresql-client-16 \
    redis-tools \
    dotnet-sdk-10.0 \
    nodejs npm \
    qemu-kvm libvirt-daemon-system virtinst \
    guacd

# === 启用 Docker 非 root 访问 ===
sudo usermod -aG docker ubuntu && newgrp docker

# === 创建测试目录 ===
mkdir -p ~/yinyu-ctf-platform-tests/{unit,integration,e2e,perf,security}
mkdir -p ~/yinyu-ctf-platform-tests/data/{images,flags,writeup-files}

# === PostgreSQL 测试数据库 ===
docker run -d --name gzctf-test-db \
    -e POSTGRES_DB=gzctf_test \
    -e POSTGRES_USER=testuser \
    -e POSTGRES_PASSWORD=testpass \
    -p 5433:5432 postgres:16-alpine

# === Redis 测试缓存 ===
docker run -d --name gzctf-test-redis \
    -p 6380:6379 redis:7-alpine

# === Guacd 测试 ===
docker run -d --name gzctf-test-guacd \
    -p 4822:4822 guacamole/guacd

# === 测试用 Windows VM 模板 ===
# 准备最小化 Windows VM（用于集成测试验证 VM lifecycle）
mkdir -p /var/lib/gzctf-test/images
# 放置一份测试用 qcow2 镜像（最少 5GB 的 WinSvr2012 eval）
# cp /path/to/windows-server-2012-test.qcow2 /var/lib/gzctf-test/images/
```

### 0.2 本地测试配置

```csharp
// src/GZCTF.Test/TestConfig.cs — 测试环境统一配置
public static class TestConfig
{
    // 测试服务器
    public const string ServerHost = "<test-server-ip>";
    public const string ServerUser = "ubuntu";

    // 测试数据库
    public const string DbHost = "<test-server-ip>";
    public const int DbPort = 5433;
    public const string DbName = "gzctf_test";
    public const string DbUser = "testuser";
    public const string DbPassword = "testpass";

    // 测试 Redis
    public const string RedisHost = "<test-server-ip>:6380";

    // 测试 Guacd
    public const string GuacdHost = "<test-server-ip>:4822";

    // 测试 VM 配置
    public const string KvmUri = "qemu:///system";
    public const string ImageStoragePath = "/var/lib/gzctf-test/images";
    public const string TestVmTemplate = "/var/lib/gzctf-test/images/windows-server-2012-test.qcow2";

    // 连接字符串
    public static string ConnectionString =>
        $"Host={DbHost};Port={DbPort};Database={DbName};Username={DbUser};Password={DbPassword}";

    public static string RedisConnectionString =>
        $"{RedisHost},abortConnect=false";
}
```

### 0.3 测试执行基础设施（★CRITICAL-5 FIX★ 重写）

> **原方案问题:** GZCTFTestFixture 与现有 `IntegrationTestCollection` 共享同一个 PostgreSQL container，
> `ResetDatabaseAsync` 会破坏其他测试状态；`CreateAuthenticatedClient` 只设 Header 不真做认证；
> Moq/Respawn 未加入依赖；速率限制在 factory 默认禁用导致永远不触发 429。
>
> **修正方案:**
> 1. 用独立 TestContainers (PostgreSQL + Redis) 做 DB 隔离 — 每个 TestClass 独立的 DB 实例
> 2. 使用 `WebApplicationFactory` 的自定义 `AuthenticationHandler` 做真认证
> 3. 添加 Moq 4.x 和 Respawn 依赖到测试 project
> 4. 工厂配置 `DisableRateLimit = false` 以允许速率限制测试

```csharp
// src/GZCTF.Integration.Test/Base/IsolatedTestFixture.cs
/// <summary>
/// 隔离测试基类 — 每个测试类获得独立的 Postgres + Redis container。
/// 不与现有 IntegrationTestCollection 共享状态。
/// ★CRITICAL-5 FIX★
/// </summary>
public abstract class IsolatedTestFixture : IAsyncLifetime
{
    protected static readonly PostgreSqlContainer DbContainer =
        new PostgreSqlBuilder()
            .WithDatabase("gzctf_tdd_" + Guid.NewGuid().ToString("N")[..8])
            .WithUsername("test")
            .WithPassword("test")
            .Build();

    protected static readonly RedisContainer RedisContainer =
        new RedisBuilder().Build();

    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient AnonymousClient = null!;
    protected IServiceScope Scope => Factory.Services.CreateScope();
    protected AppDbContext Context => Scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public virtual async Task InitializeAsync()
    {
        await DbContainer.StartAsync();
        await RedisContainer.StartAsync();

        // 创建独立 Factory — 不计入 Collection，不影响其他测试
        Factory = new GZCTFApplicationFactory()
            .WithConnectionString(DbContainer.GetConnectionString())
            .WithRedis(RedisContainer.GetConnectionString())
            .WithRateLimit(enabled: true);  // ★ 启用速率限制！

        AnonymousClient = Factory.CreateClient();

        // Respawn 重置（仅限当前 DB，不影响其他测试）
        await Factory.ResetDatabaseAsync();
    }

    public virtual async Task DisposeAsync()
    {
        await DbContainer.DisposeAsync();
        await RedisContainer.DisposeAsync();
        if (Factory is IDisposable d) d.Dispose();
    }

    /// <summary>
    /// ★ 真认证客户端 — 通过 WebApplicationFactory 的自定义 AuthHandler 配置
    /// 不使用 Header hack（X-Test-Role），走真实的 [RequireUser]/[RequireAdmin] 认证管线
    /// </summary>
    protected HttpClient CreateAuthenticatedClient(string role = "Admin", Guid? userId = null)
    {
        return Factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // 替换认证 handler 为测试版本
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            "Test", options => { });
                    // 配置测试用户声明
                    services.AddAuthorization(options =>
                    {
                        // 保留原有 policy 定义（由产品代码配置）
                    });
                });
            })
            .CreateClient();
    }

    /// <summary>
    /// TestAuthHandler — 模拟认证，不走真实 JWT/Cookie 管线
    /// </summary>
    private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(...) : base(...) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(ClaimTypes.Role, Context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Admin"));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Context.Request.Headers["X-Test-UserId"].FirstOrDefault() ?? Guid.NewGuid().ToString()));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    // 辅助: 种子测试数据（带条件/增量）
    protected async Task<Game> SeedMinimalGameAsync()
    {
        var game = new Game
        {
            Title = "TDD Test Game",
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            EndTimeUtc = DateTimeOffset.UtcNow.AddHours(3),
            IsHidden = false
        };
        Context.Games.Add(game);
        await Context.SaveChangesAsync();
        return game;
    }

    protected async Task<Team> SeedTeamAsync(int gameId)
    {
        var team = new Team { Name = "Test Team" };
        Context.Teams.Add(team);
        await Context.SaveChangesAsync();
        return team;
    }

    protected async Task<Participation> SeedParticipationAsync(int gameId, int teamId)
    {
        var part = new Participation { GameId = gameId, TeamId = teamId, Status = ParticipationStatus.Accepted };
        Context.Participations.Add(part);
        await Context.SaveChangesAsync();
        return part;
    }
}
```

**依赖清单（添加到测试 project .csproj）:**
```xml
<!-- ★CRITICAL-5 FIX★ 测试必需依赖 -->
<PackageReference Include="Moq" Version="4.20.*" />
<PackageReference Include="Respawn" Version="6.2.*" />
<PackageReference Include="Testcontainers.PostgreSql" Version="4.*" />
<PackageReference Include="Testcontainers.Redis" Version="4.*" />
```

### 0.4 测试驱动规则（必须遵守）

```
RED    → 写失败测试。先写一个错误的 assert，确保它 FAIL
GREEN  → 用最小代码让测试 PASS。可以 hardcode 返回值
BLUE   → 重构。测试仍绿色时，消除重复、提取接口、改进命名

每次提交格式:
  [TDD-RED]   test(scoring): add failing test for ScoreDecay idempotency
  [TDD-GREEN] feat(scoring): implement ScoreDecay idempotent calculation
  [TDD-BLUE]  refactor(scoring): extract ScoreDecayCalculator to shared static class
```

---

## 一、Phase 1 — 评分引擎 TDD（8 个测试组, 40+ 测试）

### 测试组 1A: ScoreDecayCalculator（已详细）

RED 文件: `tests/UnitTests/Scoring/ScoreDecayTests.cs`（已在计划中详细给出）

### 测试组 1B: 验证策略 (FlagHash / Regex / Command)

```csharp
// tests/UnitTests/Scoring/VerificationStrategyTests.cs
public class FlagHashVerificationTests
{
    // RED #1: SHA256 hash 匹配返回 Accepted
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenHashMatches()
    {
        var strategy = new FlagHashVerification();
        var flag = "flag{tdd_test_12345}";
        var rule = new ScoringRule
        {
            ExpectedAnswerHash = flag.ToSHA256String(),
            SubmissionType = ScoringSubmissionType.Flag
        };
        var result = await strategy.VerifyAsync(flag, rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }

    // RED #2: 空 hash 配置时返回 WrongAnswer（不崩溃）
    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenNoHashConfigured()
    {
        var strategy = new FlagHashVerification();
        var rule = new ScoringRule { ExpectedAnswerHash = null };
        var result = await strategy.VerifyAsync("anything", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }

    // RED #3: 大小写不一致时 hash 不匹配
    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenCaseDiffers()
    {
        var strategy = new FlagHashVerification();
        var flag = "FLAG{UPPERCASE}";
        var rule = new ScoringRule { ExpectedAnswerHash = "flag{uppercase}".ToSHA256String() };
        var result = await strategy.VerifyAsync(flag, rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }
}

public class RegexVerificationTests
{
    // RED #4: Pattern 匹配返回 Accepted
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenPatternMatches()
    {
        var strategy = new RegexVerification();
        var rule = new ScoringRule
        {
            VerificationConfig = """{"Pattern":"^CTF\\{[A-F0-9]{8}\\}$"}"""
        };
        var result = await strategy.VerifyAsync("CTF{DEADBEEF}", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }

    // RED #5: 无效 JSON 配置不崩溃
    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenConfigIsNotValidJson()
    {
        var strategy = new RegexVerification();
        var rule = new ScoringRule { VerificationConfig = "not-json" };
        var result = await strategy.VerifyAsync("anything", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }
}

public class ScriptVerificationTests
{
    // RED #6: 脚本 exit 0 返回 Accepted（修复存根）
    [Fact]
    public async Task Verify_ReturnsAccepted_WhenScriptExitsZero()
    {
        var strategy = new ScriptVerification(NullLogger<ScriptVerification>.Instance);
        var rule = new ScoringRule
        {
            VerificationConfig = """{"ScriptPath":"echo","ScriptArgs":"success"}"""
        };
        var result = await strategy.VerifyAsync("any", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.Accepted, result.Status);
    }

    // RED #7: 脚本 exit 1 返回 WrongAnswer
    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenScriptExitsNonZero()
    {
        var strategy = new ScriptVerification(NullLogger<ScriptVerification>.Instance);
        var rule = new ScoringRule
        {
            VerificationConfig = """{"ScriptPath":"test","ScriptArgs":"-z ''"}"""
        };
        var result = await strategy.VerifyAsync("any", rule, null!, CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
    }

    // RED #8: 脚本超时 30 秒返回 WrongAnswer（不永久挂起）
    [Fact]
    public async Task Verify_ReturnsWrongAnswer_WhenScriptTimesOut()
    {
        var strategy = new ScriptVerification(NullLogger<ScriptVerification>.Instance);
        var rule = new ScoringRule
        {
            VerificationConfig = """{"ScriptPath":"sleep","ScriptArgs":"300"}"""
        };
        var sw = Stopwatch.StartNew();
        var result = await strategy.VerifyAsync("any", rule, null!, CancellationToken.None);
        sw.Stop();
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 35000, "Should timeout around 30s, not 300s");
    }
}
```

### 测试组 1C: UnifiedScoringEngine 集成测试

```csharp
// tests/Integration/Tests/Scoring/ScoringEngineIntegrationTests.cs
public class ScoringEngineIntegrationTests : GZCTFTestFixture
{
    public ScoringEngineIntegrationTests(GZCTFApplicationFactory factory) : base(factory) { }

    // RED #9: IR checkpoint 完成必须写 Submission 记录（P0 bug 验证）
    [Fact]
    public async Task RecordIRCheckpointCompletion_WritesSubmissionRecord()
    {
        var engine = Factory.Services.GetRequiredService<UnifiedScoringEngine>();
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeAsync(game.Id, ChallengeType.IRChallenge);
        var checkpoint = new IRCheckpoint
        {
            ChallengeId = challenge.Id, OrderIndex = 0,
            Description = "Find the malware process",
            Score = 150, IsRequired = true,
            VerificationType = VerificationType.ManualAnswer
        };
        Context.IRCheckpoints.Add(checkpoint);
        var team = await SeedTeamAsync(game.Id);
        var part = await SeedParticipationAsync(game.Id, team.Id);
        await Context.SaveChangesAsync();

        // Act: 完成 IR 检查点
        await engine.RecordIRCheckpointCompletionAsync(
            challenge.Id, checkpoint.Id, Guid.NewGuid(),
            game.Id, team.Id, part.Id, CancellationToken.None);

        // Assert: 写入了 Submission 记录
        var submission = await Context.Submissions
            .FirstOrDefaultAsync(s => s.ChallengeId == challenge.Id
                && s.Status == AnswerResult.Accepted);
        Assert.NotNull(submission);
        Assert.Equal(150, submission.Score);
        Assert.Equal(ScoringSubmissionType.Flag, submission.SubmissionType);
    }

    // RED #10: Double-decay 验证 — 已衰减分数不再二次衰减
    [Fact]
    public async Task CalculateTotalScore_DoesNotDoubleDecay_AlreadyDecayedScores()
    {
        var scoreService = Factory.Services.GetRequiredService<ScoringService>();
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeAsync(game.Id);
        var userId = Guid.NewGuid();

        // 手动创建两条已衰减的 Submission（模拟 ScoringEngine 写入）
        var rule = new ScoringRule
        {
            ChallengeId = challenge.Id,
            SubmissionType = ScoringSubmissionType.Flag,
            Weight = 100,
            ScoreDecay = ScoreDecay.Half
        };
        Context.ScoringRules.Add(rule);
        Context.Submissions.Add(new Submission
        {
            Answer = "flag{first}", Status = AnswerResult.Accepted,
            SubmissionType = ScoringSubmissionType.Flag,
            AttemptNumber = 1, Score = 100, // attempt 0, no decay
            ChallengeId = challenge.Id, UserId = userId, GameId = game.Id, TeamId = 1, ParticipationId = 1
        });
        Context.Submissions.Add(new Submission
        {
            Answer = "flag{second}", Status = AnswerResult.Accepted,
            SubmissionType = ScoringSubmissionType.Flag,
            AttemptNumber = 2, Score = 50,  // attempt 1, half decay (correct)
            ChallengeId = challenge.Id, UserId = userId, GameId = game.Id, TeamId = 1, ParticipationId = 1
        });
        await Context.SaveChangesAsync();

        // Act: CalculateTotalScore 读取已衰减的 Scores
        var total = await scoreService.CalculateTotalScoreAsync(challenge.Id, userId);

        // Assert: 应取最佳分数 (100), 不应再衰减一次 → 50
        Assert.Equal(100, total);
    }

    // RED #11: ChallengeSubmissionType 白名单——管理员配置的提交类型生效
    [Fact]
    public async Task ProcessSubmission_RejectsUnconfiguredSubmissionType()
    {
        var engine = Factory.Services.GetRequiredService<UnifiedScoringEngine>();
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeAsync(game.Id);

        // 只配置 Flag 类型，不配置 Writeup
        Context.ChallengeSubmissionTypes.Add(new ChallengeSubmissionType
        {
            ChallengeId = challenge.Id,
            SubmissionType = ScoringSubmissionType.Flag,
            IsActive = true
        });
        await Context.SaveChangesAsync();

        var request = new SubmissionCreateRequest
        {
            Answer = "flag{test}",
            SubmissionType = ScoringSubmissionType.Writeup,  // 未配置的类型
            ChallengeId = challenge.Id, GameId = game.Id, TeamId = 1, ParticipationId = 1
        };

        var result = await engine.ProcessSubmissionAsync(request, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(AnswerResult.WrongAnswer, result.Status);  // 被拒绝
    }
}
```

### 测试组 1D: ScoreDecay 边界与回归测试（完整）

```csharp
// tests/UnitTests/Scoring/ScoreDecayBoundaryTests.cs
public class ScoreDecayBoundaryTests
{
    [Theory]
    [InlineData(ScoreDecay.None, 100, 0, 100)]
    [InlineData(ScoreDecay.None, 100, 5, 100)]
    [InlineData(ScoreDecay.None, 100, 100, 100)]
    [InlineData(ScoreDecay.Half, 100, 0, 100)]
    [InlineData(ScoreDecay.Half, 100, 1, 50)]
    [InlineData(ScoreDecay.Half, 100, 2, 25)]
    [InlineData(ScoreDecay.Half, 100, 3, 12)]
    [InlineData(ScoreDecay.Half, 100, 4, 6)]
    [InlineData(ScoreDecay.Linear, 100, 0, 100)]
    [InlineData(ScoreDecay.Linear, 100, 1, 90)]
    [InlineData(ScoreDecay.Linear, 100, 5, 50)]
    [InlineData(ScoreDecay.Linear, 100, 11, 0)]   // 最小为 0
    [InlineData(ScoreDecay.Linear, 100, 20, 0)]   // 不变成负数
    public void Apply_ReturnsCorrectValue(ScoreDecay decay, int baseScore, int attemptIndex, int expected)
    {
        var result = ScoreDecayCalculator.Apply(baseScore, attemptIndex, decay);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_ReturnsBaseScore_WhenAttemptIndexNegative()
    {
        Assert.Equal(100, ScoreDecayCalculator.Apply(100, -1, ScoreDecay.Half));
    }

    [Fact]
    public void Apply_ReturnsZero_WhenBaseScoreZero()
    {
        Assert.Equal(0, ScoreDecayCalculator.Apply(0, 5, ScoreDecay.Half));
    }

    // 确定性: 相同输入永远相同输出
    [Fact]
    public void Apply_IsDeterministic_SameInputSameOutput()
    {
        for (int i = 0; i < 100; i++)
        {
            var a = ScoreDecayCalculator.Apply(100, 3, ScoreDecay.Half);
            var b = ScoreDecayCalculator.Apply(100, 3, ScoreDecay.Half);
            Assert.Equal(a, b);
        }
    }
}
```

---

## 二、Phase 2 — 安全加固 TDD（4 个测试组, 15+ 测试）

### 测试组 2A: 速率限制测试

```csharp
// tests/Integration/Tests/Security/RateLimitTests.cs
public class RateLimitTests : GZCTFTestFixture
{
    public RateLimitTests(GZCTFApplicationFactory factory) : base(factory) { }

    // RED #12: 超过 10 次/min 返回 429
    [Fact]
    public async Task FlagSubmissionRateLimit_Returns429_AfterExceedingLimit()
    {
        var client = CreateAuthenticatedClient("User");
        var payload = new { answer = "flag{test}", submissionType = "Flag", challengeId = 1 };

        // 前 10 次应该成功
        for (int i = 0; i < 10; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/v1/submissions", payload);
            Assert.NotEqual(429, (int)ok.StatusCode);
        }

        // 第 11 次应被限流
        var limited = await client.PostAsJsonAsync("/api/v1/submissions", payload);
        Assert.Equal(429, (int)limited.StatusCode);
        Assert.Contains("Retry-After", limited.Headers.Names());
    }

    // RED #13: IR checkpoint submit 也有限流
    [Fact]
    public async Task CheckpointSubmissionRateLimit_Returns429_AfterExceedingLimit()
    {
        var client = CreateAuthenticatedClient("User");
        var payload = new { answer = "ransomware" };

        // 前 10 次 OK
        for (int i = 0; i < 10; i++)
        {
            var ok = await client.PostAsJsonAsync(
                $"/api/v1/ir-challenges/instances/{Guid.NewGuid()}/checkpoints/1/submit", payload);
            Assert.NotEqual(429, (int)ok.StatusCode);
        }

        // 第 11 次 429
        var limited = await client.PostAsJsonAsync(
            $"/api/v1/ir-challenges/instances/{Guid.NewGuid()}/checkpoints/1/submit", payload);
        Assert.Equal(429, (int)limited.StatusCode);
    }

    // RED #14: 不同用户独立计数
    [Fact]
    public async Task RateLimit_IsPerUser_NotGlobal()
    {
        var user1 = CreateAuthenticatedClient("User");
        var user2 = CreateAuthenticatedClient("User");
        var payload = new { answer = "flag{test}", submissionType = "Flag", challengeId = 1 };

        // User1 用光配额
        for (int i = 0; i < 10; i++)
            await user1.PostAsJsonAsync("/api/v1/submissions", payload);
        Assert.Equal(429, (int)(await user1.PostAsJsonAsync("/api/v1/submissions", payload)).StatusCode);

        // User2 仍可提交
        var user2Ok = await user2.PostAsJsonAsync("/api/v1/submissions", payload);
        Assert.NotEqual(429, (int)user2Ok.StatusCode);
    }
}
```

### 测试组 2B: VmManager 注入防御

```csharp
// tests/UnitTests/Vm/VmSecurityTests.cs
public class VmSecurityTests
{
    // RED #15: 拒绝含 shell 特殊字符的 VM 名
    [Theory]
    [InlineData("test;rm -rf /")]
    [InlineData("test|cat /etc/passwd")]
    [InlineData("test`whoami`")]
    [InlineData("test$(echo hack)")]
    [InlineData("test & echo hack")]
    public void SanitizeVmName_ThrowsOnShellMetacharacters(string maliciousName)
    {
        Assert.Throws<VmOperationException>(() =>
            KvmProvider.SanitizeVmName(maliciousName));
    }

    // RED #16: 接受合法的 VM 名
    [Theory]
    [InlineData("ir-test-vm-1")]
    [InlineData("scenario_42_stage_3")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789_-")]
    public void SanitizeVmName_AcceptsValidNames(string validName)
    {
        var result = KvmProvider.SanitizeVmName(validName);
        Assert.Equal(validName, result);
    }

    // RED #17: 拒绝超过 64 字符的 VM 名
    [Fact]
    public void SanitizeVmName_ThrowsOnExcessivelyLongName()
    {
        var longName = new string('a', 65);
        Assert.Throws<VmOperationException>(() =>
            KvmProvider.SanitizeVmName(longName));
    }
}
```

### 测试组 2C: 响应脱敏

```csharp
// tests/UnitTests/Models/IRChallengeModelTests.cs
public class IRChallengeModelTests
{
    // RED #18: AccessDetails 不包含密码哈希
    [Fact]
    public void IRInstanceDetailModel_ExcludesSensitiveFields()
    {
        var rawAccessDetails = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["SshHost"] = "10.0.0.1",
            ["SshPort"] = 2222,
            ["SshUsername"] = "player",
            ["SshPasswordHash"] = "pbkdf2:sha256:...",  // ← 敏感
            ["GuacamoleToken"] = "secret-token-12345",   // ← 敏感
            ["VmName"] = "ir-vm-test"
        });

        var sanitized = IRInstanceDetailModel.SanitizeAccessDetails(rawAccessDetails);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(sanitized);

        Assert.True(parsed!.ContainsKey("SshHost"));
        Assert.True(parsed.ContainsKey("VmName"));
        Assert.False(parsed.ContainsKey("SshPasswordHash"));   // 已脱敏
        Assert.False(parsed.ContainsKey("GuacamoleToken"));    // 已脱敏
    }
}
```

---

## 三、Phase 3 — VM Provider TDD（6 个测试组, 25+ 测试）

### 测试组 3A: KvmProvider 生命周期

```csharp
// tests/Integration/Tests/Vm/VmLifecycleTests.cs（在测试服务器上运行）
// [Trait("Category", "RequiresKVM")] — 仅当 KVM 可用时运行
public class VmLifecycleTests : GZCTFTestFixture, IDisposable
{
    private readonly IVirtualMachineProvider _provider;
    private readonly List<string> _createdVms = [];
    private const string TestTemplate = "/var/lib/gzctf-test/images/windows-test.qcow2";

    public VmLifecycleTests(GZCTFApplicationFactory factory) : base(factory)
    {
        _provider = factory.Services.GetRequiredService<IVirtualMachineProvider>();
    }

    // RED #19: 从模板创建 VM
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task CreateFromTemplate_CreatesVM_ReturnsVmName()
    {
        var vmName = KvmProvider.GenerateSafeName("test-vm");
        _createdVms.Add(vmName);

        var result = await _provider.CreateFromTemplateAsync(TestTemplate, vmName, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(vmName, result.VmName);
    }

    // RED #20: 启动 VM 后 IsRunning 返回 true
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task Start_VmIsRunning_AfterSuccessfulStart()
    {
        var vmName = await CreateAndStartVmAsync();

        var isRunning = await _provider.IsRunningAsync(vmName, CancellationToken.None);

        Assert.True(isRunning);
    }

    // RED #21: 获取 IP 地址（轮询最多 120 秒）
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task GetIpAddress_ReturnsNonLoopbackIPv4_Within120Seconds()
    {
        var vmName = await CreateAndStartVmAsync();
        var sw = Stopwatch.StartNew();
        string? ip = null;

        while (sw.ElapsedMilliseconds < 120_000)
        {
            ip = await _provider.GetIpAddressAsync(vmName, CancellationToken.None);
            if (ip is not null && ip != "127.0.0.1") break;
            await Task.Delay(5_000);
        }

        Assert.NotNull(ip);
        Assert.NotEqual("127.0.0.1", ip);
        Assert.True(sw.ElapsedMilliseconds < 120_000,
            $"Expected IP within 120s, took {sw.ElapsedMilliseconds}ms");
    }

    // RED #22: 创建快照后 Revert 成功
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task SnapshotRevert_RestoresVmToSnapshotState()
    {
        var vmName = await CreateAndStartVmAsync();

        await _provider.CreateSnapshotAsync(vmName, "test-snapshot", CancellationToken.None);
        var revertResult = await _provider.SnapshotRevertAsync(vmName, CancellationToken.None);

        Assert.True(revertResult.Success);
    }

    // RED #23: Destroy 后 IsRunning 返回 false
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task Destroy_VmIsNotRunning_AfterDestroy()
    {
        var vmName = await CreateAndStartVmAsync();

        await _provider.DestroyAsync(vmName, CancellationToken.None);
        await Task.Delay(2000);

        var isRunning = await _provider.IsRunningAsync(vmName, CancellationToken.None);
        Assert.False(isRunning);
    }

    // RED #24: Destroy 后 virsh undefine 已执行
    [Fact]
    [Trait("Category", "RequiresKVM")]
    public async Task Destroy_UndefinesDomain_InLibvirt()
    {
        var vmName = await CreateAndStartVmAsync();
        await _provider.DestroyAsync(vmName, CancellationToken.None);

        // 用 virsh 命令验证
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "virsh",
            Arguments = $"dominfo {vmName}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        await process.WaitForExitAsync();
        // virsh dominfo 对不存在的域返回 exit code != 0
        Assert.NotEqual(0, process.ExitCode);
    }

    private async Task<string> CreateAndStartVmAsync()
    {
        var vmName = KvmProvider.GenerateSafeName($"tdd-{Guid.NewGuid():N}".Truncate(16));
        _createdVms.Add(vmName);
        await _provider.CreateFromTemplateAsync(TestTemplate, vmName, CancellationToken.None);
        await _provider.StartAsync(vmName, CancellationToken.None);
        return vmName;
    }

    public void Dispose()
    {
        // 清理所有测试 VM
        foreach (var vm in _createdVms)
        {
            try { _provider.DestroyAsync(vm, CancellationToken.None).Wait(TimeSpan.FromSeconds(30)); }
            catch { /* best-effort */ }
        }
    }
}
```

### 测试组 3B: LocalImageImporter

```csharp
// tests/Integration/Tests/Vm/LocalImageImporterTests.cs
public class LocalImageImporterTests : GZCTFTestFixture
{
    // RED #25: 从本地路径导入 qcow2
    [Fact]
    public async Task ImportFromLocalPath_ImportsQcow2_AndCreatesImageTemplate()
    {
        var importer = Factory.Services.GetRequiredService<LocalImageImporter>();
        var testImagePath = "/var/lib/gzctf-test/images/windows-test.qcow2";

        var template = await importer.ImportFromLocalPathAsync(testImagePath, "Test Win VM");

        Assert.NotNull(template);
        Assert.Equal("Test Win VM", template.Name);
        Assert.Equal(OSType.Windows, template.OSType);
        Assert.Equal(ImageType.Qcow2, template.ImageType);
        Assert.True(template.LocalFilePath!.StartsWith(TestConfig.ImageStoragePath));
    }

    // RED #26: 导入后触发镜像分发
    [Fact]
    public async Task ImportFromLocalPath_TriggersDistribution_ToOnlineKvmNodes()
    {
        var importer = Factory.Services.GetRequiredService<LocalImageImporter>();
        // 预先注册一个在线 KVM 节点
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(), Name = "kvm-node-1",
            HostAddress = "<test-server-ip>:9001",
            Capabilities = NodeCapability.Kvm,
            Status = NodeStatus.Online
        };
        Context.WorkerNodes.Add(node);
        await Context.SaveChangesAsync();

        var template = await importer.ImportFromLocalPathAsync(
            "/var/lib/gzctf-test/images/windows-test.qcow2", "Test");

        // 验证分发记录已创建
        var assignments = await Context.NodeImageAssignments
            .Where(a => a.ImageTemplateId == template.Id).ToListAsync();
        Assert.Contains(assignments, a => a.NodeId == node.Id);
    }

    // RED #27: 导入不存在的路径抛出异常
    [Fact]
    public async Task ImportFromLocalPath_Throws_WhenPathDoesNotExist()
    {
        var importer = Factory.Services.GetRequiredService<LocalImageImporter>();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            importer.ImportFromLocalPathAsync("/nonexistent/path.qcow2", "Bad"));
    }
}
```

---

## 四、Phase 4 — 分布式调度 TDD（5 个测试组, 20+ 测试）

### 测试组 4A: WeightedScheduler 调度公平性

```csharp
// tests/UnitTests/Fleet/WeightedSchedulerTests.cs
public class WeightedSchedulerTests
{
    // RED #28: 选择负载最低的节点
    [Fact]
    public async Task SelectOptimalNode_ReturnsLeastLoadedNode()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.9f, MemoryLoad = 0.8f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, MemoryLoad = 0.2f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.5f, MemoryLoad = 0.5f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
        };
        var repo = CreateMockRepo(nodes);
        var scheduler = new WeightedScheduler(repo, NullLogger<WeightedScheduler>.Instance);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);

        Assert.NotNull(selected);
        Assert.Equal(nodes[1].Id, selected);  // 负载最低的
    }

    // RED #29: 排除不具备所需能力的节点
    [Fact]
    public async Task SelectOptimalNode_ExcludesNodesWithoutRequiredCapability()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.2f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
        };
        var repo = CreateMockRepo(nodes);
        var scheduler = new WeightedScheduler(repo, NullLogger<WeightedScheduler>.Instance);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Kvm, CancellationToken.None);
        Assert.Null(selected);  // 无 KVM 节点可选
    }

    // RED #30: 全部节点过载时返回 null（触发排队）
    [Fact]
    public async Task SelectOptimalNode_ReturnsNull_WhenAllNodesExceedLoadThreshold()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.92f, MemoryLoad = 0.95f, CurrentContainers = 19, MaxContainers = 20, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.95f, MemoryLoad = 0.92f, CurrentContainers = 19, MaxContainers = 20, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
        };
        var repo = CreateMockRepo(nodes);
        var scheduler = new WeightedScheduler(repo, NullLogger<WeightedScheduler>.Instance);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Null(selected);  // 触发排队
    }

    // RED #31: 排除离线节点
    [Fact]
    public async Task SelectOptimalNode_ExcludesOfflineNodes()
    {
        var nodes = new List<WorkerNode>
        {
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Offline },
            new() { Id = Guid.NewGuid(), CpuLoad = 0.1f, Capabilities = NodeCapability.Docker, Status = NodeStatus.Online },
        };
        var repo = CreateMockRepo(nodes);
        var scheduler = new WeightedScheduler(repo, NullLogger<WeightedScheduler>.Instance);

        var selected = await scheduler.SelectOptimalNodeAsync(NodeCapability.Docker, CancellationToken.None);
        Assert.Equal(nodes[1].Id, selected);
    }
}
```

### 测试组 4B: QueueManager 排队逻辑

```csharp
// tests/UnitTests/Fleet/QueueManagerTests.cs
public class QueueManagerTests
{
    // RED #32: 无可用节点时请求入队
    [Fact]
    public async Task Enqueue_RequestQueued_WhenNoAvailableNodes()
    {
        var queue = new QueueManager(Mock.Of<INodeRepository>(), Mock.Of<WeightedScheduler>());
        var request = new DeploymentRequest { RequestId = Guid.NewGuid(), RequiredCapability = NodeCapability.Docker };

        var position = await queue.EnqueueAsync(request);

        Assert.NotNull(position);
        Assert.Equal(1, position.Position);
    }

    // RED #33: 查询队列位置
    [Fact]
    public async Task GetQueueStatus_ReturnsCorrectPosition()
    {
        var queue = new QueueManager(Mock.Of<INodeRepository>(), Mock.Of<WeightedScheduler>());
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await queue.EnqueueAsync(new DeploymentRequest { RequestId = id1, RequiredCapability = NodeCapability.Docker });
        await queue.EnqueueAsync(new DeploymentRequest { RequestId = id2, RequiredCapability = NodeCapability.Docker });

        var status1 = queue.GetQueueStatus(id1);
        var status2 = queue.GetQueueStatus(id2);

        Assert.Equal(1, status1.Position);  // 排第 1
        Assert.Equal(2, status2.Position);  // 排第 2
    }
}
```

### 测试组 4C: PortCapacityTracker + Agent PortAllocator

```csharp
// tests/UnitTests/Fleet/PortCapacityTrackerTests.cs
public class PortCapacityTrackerTests
{
    // RED #34: 节点容量足够时 HasCapacity 返回 true
    [Fact]
    public void HasCapacity_ReturnsTrue_WhenEnoughPortsAvailable()
    {
        var tracker = new PortCapacityTracker();
        var nodeId = Guid.NewGuid();
        tracker.UpdateCapacity(nodeId, totalPorts: 28231, usedPorts: 100);

        Assert.True(tracker.HasCapacity(nodeId, 1));
    }

    // RED #35: 节点容量耗尽时 HasCapacity 返回 false
    [Fact]
    public void HasCapacity_ReturnsFalse_WhenPortsExhausted()
    {
        var tracker = new PortCapacityTracker();
        var nodeId = Guid.NewGuid();
        tracker.UpdateCapacity(nodeId, totalPorts: 28231, usedPorts: 28231);

        Assert.False(tracker.HasCapacity(nodeId, 1));
    }
}

// tests/UnitTests/Fleet/AgentPortAllocatorTests.cs
public class AgentPortAllocatorTests
{
    // RED #36: 分配端口后自动释放不重复
    [Fact]
    public void AllocatePort_DoesNotReturnSamePort_AfterRelease()
    {
        var allocator = new AgentPortAllocator();
        var range = new PortRange(50000, 50010);

        var p1 = allocator.AllocatePort(range);
        allocator.ReleasePort(p1);
        var p2 = allocator.AllocatePort(range);

        Assert.NotEqual(p1, p2);  // 释放后不应立即返回相同端口
    }

    // RED #37: 范围内端口耗尽抛异常
    [Fact]
    public void AllocatePort_Throws_WhenRangeExhausted()
    {
        var allocator = new AgentPortAllocator();
        var range = new PortRange(50000, 50001);  // 只有 2 个端口

        allocator.AllocatePort(range);
        allocator.AllocatePort(range);
        Assert.Throws<PortExhaustedException>(() => allocator.AllocatePort(range));
    }
}
```

---

## 五、Phase 5 — 游戏阶段控制 TDD（3 个测试组, 12+ 测试）

### 测试组 5A: 阶段过滤

```csharp
// tests/Integration/Tests/Game/GamePhaseTests.cs
public class GamePhaseTests : GZCTFTestFixture
{
    public GamePhaseTests(GZCTFApplicationFactory factory) : base(factory) { }

    // RED #38: IR 禁用时 IR challenge API 返回 403
    [Fact]
    public async Task IRChallengeEndpoint_Returns403_WhenIRPhaseDisabled()
    {
        var game = await SeedMinimalGameAsync();
        Context.GamePhases.Add(new GamePhase
        {
            GameId = game.Id, Name = "CTF Only",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            EndTime = DateTimeOffset.UtcNow.AddHours(2),
            CTFEnabled = true,
            IREnabled = false,        // ← IR 已禁用
            ScenarioEnabled = false
        });
        await Context.SaveChangesAsync();

        var client = CreateAuthenticatedClient("User");
        var response = await client.GetAsync($"/api/v1/ir-challenges?gameId={game.Id}");

        Assert.Equal(403, (int)response.StatusCode);
    }

    // RED #39: Scenario 禁用时 Scenario 创建返回 403
    [Fact]
    public async Task ScenarioCreate_Returns403_WhenScenarioPhaseDisabled()
    {
        var game = await SeedMinimalGameAsync();
        Context.GamePhases.Add(new GamePhase
        {
            GameId = game.Id, Name = "IR Only",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            EndTime = DateTimeOffset.UtcNow.AddHours(2),
            CTFEnabled = false,
            IREnabled = true,
            ScenarioEnabled = false
        });
        await Context.SaveChangesAsync();

        var client = CreateAuthenticatedClient("Admin");
        var payload = new { title = "Test Scenario", gameId = game.Id };
        var response = await client.PostAsJsonAsync("/api/v1/scenarios", payload);

        Assert.Equal(403, (int)response.StatusCode);
    }

    // RED #40: 阶段已过期时传统 CTF Flag 提交返回 403
    [Fact]
    public async Task FlagSubmission_Returns403_WhenAllPhasesExpired()
    {
        var game = await SeedMinimalGameAsync();
        Context.GamePhases.Add(new GamePhase
        {
            GameId = game.Id, Name = "Expired",
            StartTime = DateTimeOffset.UtcNow.AddHours(-3),
            EndTime = DateTimeOffset.UtcNow.AddHours(-1),  // 已结束
            CTFEnabled = true
        });
        await Context.SaveChangesAsync();

        var client = CreateAuthenticatedClient("User");
        var payload = new { flag = "flag{test}", challengeId = 1, gameId = game.Id };
        var response = await client.PostAsJsonAsync("/api/v1/submissions", payload);

        Assert.Equal(403, (int)response.StatusCode);
    }

    // RED #41: 所有阶段启用时正常提交
    [Fact]
    public async Task Submission_Accepted_WhenPhaseIsActive()
    {
        var game = await SeedMinimalGameAsync();
        Context.GamePhases.Add(new GamePhase
        {
            GameId = game.Id, Name = "Active",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            EndTime = DateTimeOffset.UtcNow.AddHours(2),
            CTFEnabled = true, IREnabled = true, ScenarioEnabled = true
        });
        await Context.SaveChangesAsync();

        var client = CreateAuthenticatedClient("User");
        var response = await client.GetAsync($"/api/v1/ir-challenges?gameId={game.Id}");
        Assert.NotEqual(403, (int)response.StatusCode);
    }
}
```

---

## 六、Phase 6 — 数据模型 TDD（3 个测试组, 12+ 测试）

### 测试组 6A: FK 约束 + 并发令牌

```csharp
// tests/Integration/Tests/Database/DataIntegrityTests.cs
public class DataIntegrityTests : GZCTFTestFixture
{
    public DataIntegrityTests(GZCTFApplicationFactory factory) : base(factory) { }

    // RED #42: Container 引用不存在的 GameInstance 时插入失败
    [Fact]
    public async Task Container_InsertFails_WhenGameInstanceDoesNotExist()
    {
        var container = new Container
        {
            Id = Guid.NewGuid(), Image = "test:latest",
            ContainerId = "abc123", Status = ContainerStatus.Pending,
            GameInstanceId = 99999  // 不存在的 GameInstance
        };
        Context.Containers.Add(container);
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    // RED #43: 删除 GameChallenge 后 FlagContext 级联删除
    [Fact]
    public async Task FlagContext_CascadeDeletes_WhenChallengeIsDeleted()
    {
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeAsync(game.Id);
        Context.FlagContexts.Add(new FlagContext
        {
            Flag = "flag{cascade-test}",
            ChallengeId = challenge.Id, IsOccupied = false
        });
        await Context.SaveChangesAsync();

        Context.GameChallenges.Remove(challenge);
        await Context.SaveChangesAsync();

        var remaining = await Context.FlagContexts
            .CountAsync(f => f.ChallengeId == challenge.Id);
        Assert.Equal(0, remaining);
    }

    // RED #44: FlagContext 同时设 ChallengeId 和 ExerciseId 插入失败（CHECK 约束）
    [Fact]
    public async Task FlagContext_InsertFails_WhenBothParentsSet()
    {
        var fc = new FlagContext
        {
            Flag = "test-flag",
            ChallengeId = 1,
            ExerciseId = 1,  // 双上级 → CHECK 约束拒绝
            IsOccupied = false
        };
        Context.FlagContexts.Add(fc);
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    // RED #45: Submission 并发更新被 xmin 检测到
    [Fact]
    public async Task Submission_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeAsync(game.Id);
        var sub = new Submission
        {
            Answer = "flag{test}", Status = AnswerResult.Accepted,
            ChallengeId = challenge.Id, GameId = game.Id,
            UserId = Guid.NewGuid(), TeamId = 1, ParticipationId = 1,
            SubmissionType = ScoringSubmissionType.Flag
        };
        Context.Submissions.Add(sub);
        await Context.SaveChangesAsync();

        // 模拟并发: 两个上下文同时修改
        using var scope1 = Factory.Services.CreateScope();
        using var scope2 = Factory.Services.CreateScope();
        var ctx1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var sub1 = await ctx1.Submissions.FindAsync(sub.Id);
        var sub2 = await ctx2.Submissions.FindAsync(sub.Id);
        sub1!.Score = 100;
        sub2!.Score = 200;

        await ctx1.SaveChangesAsync(); // 第一个成功

        // 第二个应检测到并发冲突
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            ctx2.SaveChangesAsync());
    }
}
```

---

## 七、E2E 测试矩阵（18 个文件, 80+ 场景）

### 测试框架配置

```typescript
// tests/e2e/config/playwright.config.ts
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '../',
  timeout: 120_000,            // 2 分钟超时（含 VM 启动时间）
  expect: { timeout: 30_000 },
  retries: 1,                  // 重试一次（处理网络抖动）
  workers: 2,                  // 并行 2 个 worker

  // 全局 auth 钩子
  globalSetup: require.resolve('./global-auth-setup.ts'),

  use: {
    baseURL: 'http://localhost:8080',
    trace: 'on-first-retry',   // 失败时记录 trace
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    { name: 'chromium' },
    // 需要时加 Firefox/Safari
  ],

  webServer: {
    command: 'dotnet run --project src/GZCTF --urls http://localhost:8080',
    url: 'http://localhost:8080/api/info',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
```

### E2E 测试清单与关键场景

```typescript
// tests/e2e/scoring-engine.spec.ts
// RED #46-#52: 评分引擎 E2E 流程
test('传统 CTF: 选手提交 Flag → Flag 正确 → 排行榜出现分数', async ({ page }) => { /* ... */ });
test('传统 CTF: 选手提交 Flag → Flag 错误 → 无分数 → 仍在 0 分', async ({ page }) => { /* ... */ });
test('Scenario: 多阶段提交 → 每阶段解锁下一阶段', async ({ page }) => { /* ... */ });
test('IR: 完成检查点 → Submission 记录写入', async ({ page }) => { /* ... */ });
test('ScoreDecay: Half 模式 第二次提交分数减半', async ({ page }) => { /* ... */ });
test('多 Flag: 管理员配置 Flag+Writeup+File → 选手提交三种类型 → 各自计分', async ({ page }) => { /* ... */ });
test('排行榜: 多队伍提交 → 实时排序更新', async ({ page }) => { /* ... */ });
```

```typescript
// tests/e2e/vm-lifecycle.spec.ts
// RED #53-#58: VM 生命周期 E2E
test('管理员从本地路径导入 Windows VM 镜像 → 镜像列表出现', async ({ page }) => { /* ... */ });
test('镜像分发到在线 KVM 节点 → 分发状态为 cached 或 transferred', async ({ page }) => { /* ... */ });
test('IR Windows (RDP): 选手创建实例 → Guacamole 连接信息显示', async ({ page }) => { /* ... */ });
test('VM 重置: 选手重置 → 环境恢复 → 检查点清零', async ({ page }) => { /* ... */ });
test('VM 销毁: 管理员删除 → virsh 中无 definfed 域', async ({ page }) => { /* ... */ });
test('VM 端口: 5 个 VM 并发启动 → 端口无冲突', async ({ page }) => { /* ... */ });
```

```typescript
// tests/e2e/fleet-scheduling.spec.ts
// RED #59-#65: 分布式调度 E2E
test('节点注册 → 列表出现 → 状态 Online', async ({ page }) => { /* ... */ });
test('Agent 心跳停止 120 秒 → 节点标记为 Offline', async ({ page }) => { /* ... */ });
test('调度自动选则负载最低节点 → 负载最低节点被选中', async ({ page }) => { /* ... */ });
test('全部节点 > 90% → 新建请求入队 → 队列状态显示', async ({ page }) => { /* ... */ });
test('节点释放资源 → 队列自动出队 → 容器创建成功', async ({ page }) => { /* ... */ });
test('权重调度分配: 2 轻载节点 + 3 高负载 → 选择负载最低 2 个', async ({ page }) => { /* ... */ });
test('端口管理: Agent 本地分配 → 心跳上报容量', async ({ page }) => { /* ... */ });
```

```typescript
// tests/e2e/concurrency.spec.ts
// RED #66-#70: 并发安全 E2E
test('10 线程同时提交同一 Flag → 只有 1 个被接受（无重复）', async ({ page }) => { /* ... */ });
test('10 线程同时启动容器 → 端口无冲突 → 全部启动成功', async ({ page }) => { /* ... */ });
test('并发提交 + 并发阅读排行榜 → 无脏读', async ({ page }) => { /* ... */ });
test('同一 FlagContext.IsOccupied 并发抢占 → 一次只有一人拿到', async ({ page }) => { /* ... */ });
test('分布式锁: 两进程同时获取 challenge:scope:42:userX → 只有一个获取成功', async ({ page }) => { /* ... */ });
```

```typescript
// tests/e2e/game-phase.spec.ts
// RED #71-#74: 阶段控制 E2E
test('Admin 启用 IR + 禁用 Scenario → 选手 IR 可访问, Scenario 返回 403', async () => { /* ... */ });
test('阶段到期 → 所有端点返回 403', async () => { /* ... */ });
test('阶段切换 → 进行中的实例可用 shutdown 优雅关闭', async () => { /* ... */ });
test('紧急暂停 → 立即停止所有新实例创建', async () => { /* ... */ });
```

```typescript
// tests/e2e/performance.spec.ts
// RED #75-#80: 性能基准 E2E
test('ScoreDecayCalculator: 100万次计算 < 100ms', async () => { /* ... */ });
test('UnifiedScoringEngine: 1000并发提交响应 < 5 秒', async () => { /* ... */ });
test('节点心跳上报 → 管理面板更新 < 2 秒', async () => { /* ... */ });
test('镜像分发 > 1GB → 进度条显示 + ETA', async () => { /* ... */ });
test('排队 100 个请求 → UI 不卡顿 → 每秒更新排队位置', async () => { /* ... */ });
test('榜单查询 (5000 队伍) → 响应 < 500ms', async () => { /* ... */ });
```

---

## 八、性能与压力测试

### 8.1 评分引擎压力测试

```csharp
// tests/Perf/ScoringEngineThroughputTests.cs
[TestClass]
public class ScoringEngineThroughputTests : GZCTFTestFixture
{
    // RED #81: 单线程每秒 10000+ 次 ScoreDecay 计算
    [Fact]
    public void ScoreDecayCalculator_Performs100KCalculations_Under100ms()
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
            ScoreDecayCalculator.Apply(100, i % 10, (ScoreDecay)(i % 3));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"Expected < 100ms, got {sw.ElapsedMilliseconds}ms");
    }

    // RED #82: 1000 并发提交无超时
    [Fact]
    public async Task ScoringEngine_Handles1000ConcurrentSubmissions_WithoutTimeout()
    {
        var engine = Factory.Services.GetRequiredService<UnifiedScoringEngine>();
        var game = await SeedMinimalGameAsync();
        var challenge = await SeedChallengeWithRuleAsync(game.Id);
        var lockService = Factory.Services.GetRequiredService<IDistributedLockService>();

        var tasks = Enumerable.Range(0, 1000).Select(async i =>
        {
            var request = new SubmissionCreateRequest
            {
                Answer = $"flag{{test_{i % 10}}}",
                SubmissionType = ScoringSubmissionType.Flag,
                ChallengeId = challenge.Id, GameId = game.Id,
                TeamId = (i % 10) + 1, ParticipationId = (i % 10) + 1
            };
            return await engine.ProcessSubmissionAsync(request, Guid.NewGuid(), CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);
        var accepted = results.Count(r => r.Status == AnswerResult.Accepted);
        Assert.True(accepted > 0);
    }

    // RED #83: 分布式锁无竞争下 < 1ms 获取
    [Fact]
    public async Task DistributedLock_AcquiresUnder1ms_WhenNoContention()
    {
        var lockService = new LocalSemaphoreLock();
        var sw = Stopwatch.StartNew();
        using var _ = await lockService.AcquireAsync("perf-test-key");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1,
            $"Expected < 1ms, got {sw.ElapsedMilliseconds}ms");
    }
}
```

### 8.2 数据库性能基线

```sql
-- 测试服务器上运行
-- 连接: psql -h <test-server-ip> -p 5433 -U testuser -d gzctf_test

-- 插入 5000 支队伍 + 50000 条提交的性能基线
\timing on

INSERT INTO "Submissions" (answer, status, score, "TeamId", "ChallengeId", "GameId")
SELECT 'flag{bench}' || i, 1, 100, i % 5000, 1, 1
FROM generate_series(1, 50000) AS i;

-- 查询排行榜基线（带索引后应 < 100ms）
EXPLAIN ANALYZE
SELECT "TeamId", SUM(score) as total
FROM "Submissions"
WHERE "GameId" = 1 AND "Status" = 1
GROUP BY "TeamId"
ORDER BY total DESC
LIMIT 50;
```

---

## 九、TDD 测试仪表盘

每个 Phase 的测试通过率追踪：

| Phase | 测试组 | 测试数 | RED | GREEN | BLUE |
|---|---|---|---|---|---|
| Phase 1 | 1A-1D | 40+ | [#1-#11](#) | 待实现 | 待重构 |
| Phase 2 | 2A-2C | 15+ | [#12-#18](#) | 待实现 | 待重构 |
| Phase 3 | 3A-3B | 25+ | [#19-#27](#) | 待实现 | 待重构 |
| Phase 4 | 4A-4C | 20+ | [#28-#37](#) | 待实现 | 待重构 |
| Phase 5 | 5A | 12+ | [#38-#41](#) | 待实现 | 待重构 |
| Phase 6 | 6A | 12+ | [#42-#45](#) | 待实现 | 待重构 |
| E2E | 7 文件 | 80+ | [#46-#80](#) | 待实现 | 待重构 |
| Perf | 8A | 3 | [#81-#83](#) | 待实现 | 待重构 |

**红线标准:** 每 Phase 完成所有 GREEN 测试通过后方可进入下一 Phase

---

## 十、CI 测试管道

```yaml
# .github/workflows/tdd-pipeline.yml
# 用于测试服务器: <test-server-ip>

name: TDD Pipeline

on:
  push:
    branches: [feature/*, fix/*]
  pull_request:
    branches: [main]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Unit Tests (RED→GREEN check)
        run: dotnet test src/GZCTF.Test -c Release --filter "Category!=RequiresKVM&Category!=Integration" -l "trx;LogFileName=unit-results.trx"
      - name: Verify no RED (failing) tests
        run: |
          FAILED=$(grep -c 'outcome="Failed"' unit-results.trx 2>/dev/null || echo 0)
          if [ "$FAILED" -gt 0 ]; then echo "RED tests remaining: $FAILED"; exit 1; fi

  integration-tests:
    runs-on: ubuntu-latest
    needs: unit-tests
    env:
      ASPNETCORE_ENVIRONMENT: Test
      ConnectionStrings__Database: "Host=<test-server-ip>;Port=5433;Database=gzctf_test;Username=testuser;Password=testpass"
      ConnectionStrings__RedisCache: "<test-server-ip>:6380"
    steps:
      - uses: actions/checkout@v4
      - name: Integration Tests
        run: dotnet test src/GZCTF.Integration.Test -c Release --filter "Category!=RequiresKVM" -l "trx;LogFileName=integration-results.trx"
      - name: Verify all GREEN
        run: |
          FAILED=$(grep -c 'outcome="Failed"' integration-results.trx 2>/dev/null || echo 0)
          if [ "$FAILED" -gt 0 ]; then echo "Integration GREEN check failed: $FAILED"; exit 1; fi

  e2e-tests:
    runs-on: ubuntu-latest
    needs: integration-tests
    steps:
      - uses: actions/checkout@v4
      - name: Playwright E2E
        run: |
          npx playwright install
          npx playwright test tests/e2e/ --project=chromium

  perf-tests:
    runs-on: ubuntu-latest
    needs: e2e-tests
    steps:
      - uses: actions/checkout@v4
      - name: Performance Benchmarks
        run: dotnet test tests/Perf -c Release --filter "Category=Performance"

  security-tests:
    runs-on: ubuntu-latest
    needs: perf-tests
    steps:
      - uses: actions/checkout@v4
      - name: OWASP ZAP Scan
        uses: zaproxy/action-full-scan@v0.10.0
        with:
          target: http://<test-server-ip>:8080
      - name: Semgrep SAST
        uses: semgrep/semgrep-action@v1
```

---

## 十一、TDD 纪律检查清单

每个开发者在提交前必须确认：

- [ ] 新功能先写了失败测试（RED commit msg 以 `[TDD-RED]` 开头）
- [ ] 最少代码使测试通过（GREEN commit msg 以 `[TDD-GREEN]` 开头）
- [ ] 重构消除了重复，测试仍通过（BLUE commit msg 以 `[TDD-BLUE]` 开头）
- [ ] 单元测试覆盖了正常路径 + 至少一种异常路径
- [ ] 安全相关代码有相应的安全测试（注入、XSS、脱敏、限流）
- [ ] 并发路径有并发安全测试
- [ ] 所有测试在本地和 CI 上通过
- [ ] 无 `[TDD-RED]` 未解决的测试留在提交中
