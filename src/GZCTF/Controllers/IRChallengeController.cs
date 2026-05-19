using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using GZCTF.Hubs;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Services;
using GZCTF.Services.Scoring;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

/// <summary>
/// Incident Response challenge management APIs.
/// Provides CRUD for IR challenges, instance lifecycle, and checkpoint submission.
/// </summary>
[ApiController]
[Route("api/v1/ir-challenges")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class IRChallengeController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<IRChallengeController> _logger;
    private readonly UserManager<UserInfo> _userManager;
    private readonly VmManager _vmManager;
    private readonly ContainerOrchestrator _containerOrchestrator;
    private readonly GuacamoleProxy _guacamoleProxy;
    private readonly SSHAccessService _sshAccessService;
    private readonly IHubContext<ScenarioHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GamePhaseService _phaseService;

    public IRChallengeController(
        AppDbContext context,
        ILogger<IRChallengeController> logger,
        UserManager<UserInfo> userManager,
        VmManager vmManager,
        ContainerOrchestrator containerOrchestrator,
        GuacamoleProxy guacamoleProxy,
        SSHAccessService sshAccessService,
        IHubContext<ScenarioHub> hubContext,
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        GamePhaseService phaseService)
    {
        _context = context;
        _logger = logger;
        _userManager = userManager;
        _vmManager = vmManager;
        _containerOrchestrator = containerOrchestrator;
        _guacamoleProxy = guacamoleProxy;
        _sshAccessService = sshAccessService;
        _hubContext = hubContext;
        _config = config;
        _scopeFactory = scopeFactory;
        _phaseService = phaseService;
    }

    #region IR Challenge CRUD

    /// <summary>
    /// Create a new IR challenge with checkpoints.
    /// Requires admin or author role.
    /// </summary>
    /// <param name="model">IR challenge creation model</param>
    /// <param name="token"></param>
    /// <response code="200">Challenge created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="404">Game not found</response>
    [HttpPost]
    [RequirePrivilege(Role.Admin)]
    [ProducesResponseType(typeof(IRChallengeDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] IRChallengeCreateModel model, CancellationToken token)
    {
        var game = await _context.Games.FindAsync([model.GameId], cancellationToken: token);
        if (game is null)
            return NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

        var challenge = new GameChallenge
        {
            Title = model.Title,
            Content = model.Content,
            Category = model.Category,
            Type = ChallengeType.IRChallenge,
            IsEnabled = model.IsEnabled,
            OriginalScore = model.OriginalScore,
            MinScoreRate = model.MinScoreRate,
            Difficulty = model.Difficulty,
            ContainerImage = model.ContainerImage,
            MemoryLimit = model.MemoryLimit ?? 2048,
            CPUCount = model.CPUCount ?? 2,
            StorageLimit = model.StorageLimit ?? 10240,
            GameId = model.GameId
        };

        await _context.GameChallenges.AddAsync(challenge, token);
        game.Challenges.Add(challenge);
        await _context.SaveChangesAsync(token);

        // Create checkpoints
        var checkpoints = model.Checkpoints.Select(c => new IRCheckpoint
        {
            ChallengeId = challenge.Id,
            OrderIndex = c.OrderIndex,
            Description = c.Description,
            Score = c.Score,
            IsRequired = c.IsRequired,
            VerificationType = c.VerificationType,
            VerificationConfig = c.VerificationConfig
        }).ToList();

        await _context.IRCheckpoints.AddRangeAsync(checkpoints, token);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("IR challenge {Id} created with {Count} checkpoints in game {GameId}",
            challenge.Id, checkpoints.Count, model.GameId);

        return Ok(IRChallengeDetailModel.FromChallenge(challenge, checkpoints));
    }

    /// <summary>
    /// List IR challenges, optionally filtered by game.
    /// </summary>
    /// <param name="gameId">Optional game ID filter</param>
    /// <param name="count">Page size (max 50)</param>
    /// <param name="skip">Items to skip</param>
    /// <param name="token"></param>
    [HttpGet]
    [ProducesResponseType(typeof(ArrayResponse<IRChallengeListItemModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? gameId,
        [FromQuery][Range(0, 50)] int count = 10,
        [FromQuery] int skip = 0,
        CancellationToken token = default)
    {
        var query = _context.GameChallenges
            .Where(c => c.Type == ChallengeType.IRChallenge);

        if (gameId.HasValue)
            query = query.Where(c => c.GameId == gameId.Value);

        var total = await query.CountAsync(token);

        var challenges = await query
            .OrderBy(c => c.Id)
            .Skip(skip)
            .Take(count)
            .ToListAsync(token);

        var checkpointCounts = await _context.IRCheckpoints
            .Where(c => challenges.Select(ch => ch.Id).Contains(c.ChallengeId))
            .GroupBy(c => c.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(k => k.ChallengeId, v => v.Count, token);

        var items = challenges.Select(c =>
        {
            checkpointCounts.TryGetValue(c.Id, out var cc);
            return IRChallengeListItemModel.FromChallenge(c, cc);
        }).ToArray();

        return Ok(new ArrayResponse<IRChallengeListItemModel>(items, total));
    }

    /// <summary>
    /// Get detailed IR challenge with checkpoints.
    /// </summary>
    /// <param name="id">Challenge ID</param>
    /// <param name="token"></param>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(IRChallengeDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken token)
    {
        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.IRChallenge, token);

        if (challenge is null)
            return NotFound(new RequestResponse("IR challenge not found", StatusCodes.Status404NotFound));

        var checkpoints = await _context.IRCheckpoints
            .Where(c => c.ChallengeId == id)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync(token);

        return Ok(IRChallengeDetailModel.FromChallenge(challenge, checkpoints));
    }

    /// <summary>
    /// Update an IR challenge.
    /// Requires admin or author role.
    /// </summary>
    /// <param name="id">Challenge ID</param>
    /// <param name="model">Update model</param>
    /// <param name="token"></param>
    [HttpPut("{id}")]
    [RequirePrivilege(Role.Admin)]
    [ProducesResponseType(typeof(IRChallengeDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] IRChallengeUpdateModel model, CancellationToken token)
    {
        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.IRChallenge, token);

        if (challenge is null)
            return NotFound(new RequestResponse("IR challenge not found", StatusCodes.Status404NotFound));

        if (model.Title is not null)
            challenge.Title = model.Title;
        if (model.Content is not null)
            challenge.Content = model.Content;
        if (model.Category.HasValue)
            challenge.Category = model.Category.Value;
        if (model.IsEnabled.HasValue)
            challenge.IsEnabled = model.IsEnabled.Value;
        if (model.OriginalScore.HasValue)
            challenge.OriginalScore = model.OriginalScore.Value;
        if (model.MinScoreRate.HasValue)
            challenge.MinScoreRate = model.MinScoreRate.Value;
        if (model.Difficulty.HasValue)
            challenge.Difficulty = model.Difficulty.Value;
        if (model.ContainerImage is not null)
            challenge.ContainerImage = model.ContainerImage;

        // Replace checkpoints if provided
        if (model.Checkpoints is { Count: > 0 })
        {
            var existingCheckpoints = await _context.IRCheckpoints
                .Where(c => c.ChallengeId == id)
                .ToListAsync(token);

            _context.IRCheckpoints.RemoveRange(existingCheckpoints);

            var newCheckpoints = model.Checkpoints.Select(c => new IRCheckpoint
            {
                ChallengeId = id,
                OrderIndex = c.OrderIndex,
                Description = c.Description,
                Score = c.Score,
                IsRequired = c.IsRequired,
                VerificationType = c.VerificationType,
                VerificationConfig = c.VerificationConfig
            }).ToList();

            await _context.IRCheckpoints.AddRangeAsync(newCheckpoints, token);
        }

        await _context.SaveChangesAsync(token);

        var updatedCheckpoints = await _context.IRCheckpoints
            .Where(c => c.ChallengeId == id)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync(token);

        return Ok(IRChallengeDetailModel.FromChallenge(challenge, updatedCheckpoints));
    }

    /// <summary>
    /// Delete an IR challenge and all related data.
    /// Requires admin role.
    /// </summary>
    /// <param name="id">Challenge ID</param>
    /// <param name="token"></param>
    [HttpDelete("{id}")]
    [RequirePrivilege(Role.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken token)
    {
        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.IRChallenge, token);

        if (challenge is null)
            return NotFound(new RequestResponse("IR challenge not found", StatusCodes.Status404NotFound));

        // Remove checkpoints
        var checkpoints = await _context.IRCheckpoints
            .Where(c => c.ChallengeId == id)
            .ToListAsync(token);
        _context.IRCheckpoints.RemoveRange(checkpoints);

        // Remove instances
        var instances = await _context.IRInstances
            .Where(i => i.ChallengeId == id)
            .ToListAsync(token);
        _context.IRInstances.RemoveRange(instances);

        // Clean up game challenge references
        var game = await _context.Games
            .Include(g => g.Challenges)
            .FirstOrDefaultAsync(g => g.Challenges.Any(c => c.Id == id), token);
        game?.Challenges.Remove(challenge);

        _context.GameChallenges.Remove(challenge);
        await _context.SaveChangesAsync(token);

        _logger.LogInformation("IR challenge {Id} and all related data deleted", id);

        return NoContent();
    }

    #endregion

    #region IR Instance Management

    /// <summary>
    /// Create a player instance for an IR challenge.
    /// Validates time slot availability and creates the target environment.
    /// </summary>
    /// <param name="id">Challenge ID</param>
    /// <param name="timeSlotId">Registered time slot ID</param>
    /// <param name="token"></param>
    [HttpPost("{id}/instances")]
    [RequireUser]
    [ProducesResponseType(typeof(IRInstanceDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInstance(
        int id,
        [FromQuery][Required] int timeSlotId,
        CancellationToken token)
    {
        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == id && c.Type == ChallengeType.IRChallenge, token);

        if (challenge is null)
            return NotFound(new RequestResponse("IR challenge not found", StatusCodes.Status404NotFound));

        var phaseCheck = await _phaseService.CheckAsync(challenge.GameId, PhaseRequiredType.IR, token);
        if (phaseCheck != PhaseCheckResult.Allowed)
            return Forbid();

        if (!challenge.IsEnabled)
            return BadRequest(new RequestResponse("Challenge is not enabled"));

        // Validate time slot
        var timeSlot = await _context.TimeSlots
            .FirstOrDefaultAsync(t => t.Id == timeSlotId && t.ScenarioId == id, token);

        if (timeSlot is null)
            return BadRequest(new RequestResponse("Invalid time slot"));

        if (timeSlot.CurrentParticipants >= timeSlot.MaxParticipants)
            return BadRequest(new RequestResponse("Time slot is full"));

        if (timeSlot.StartTime > DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("Time slot has not started yet"));

        if (timeSlot.EndTime < DateTimeOffset.UtcNow)
            return BadRequest(new RequestResponse("Time slot has ended"));

        // Check user identity
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized(new RequestResponse("User not found", StatusCodes.Status401Unauthorized));

        // Check for existing active instance
        var existingInstance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.ChallengeId == id && i.UserId == user.Id
                && i.EnvironmentStatus != EnvironmentStatus.Destroyed, token);

        if (existingInstance is not null)
            return BadRequest(new RequestResponse("You already have an active instance for this challenge"));

        // Create instance
        var instance = new IRInstance
        {
            Id = Guid.NewGuid(),
            ChallengeId = id,
            UserId = user.Id,
            EnvironmentStatus = EnvironmentStatus.Creating,
            TimeSlotId = timeSlotId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _context.IRInstances.AddAsync(instance, token);

        // Update time slot participant count
        timeSlot.CurrentParticipants++;
        await _context.SaveChangesAsync(token);

        // Create environment in the background
        _ = CreateEnvironmentAsync(instance, challenge, token);

        // Return initial status immediately
        var detail = await IRInstanceDetailModel.FromInstanceAsync(instance, _context, token);
        return Ok(detail);
    }

    private async Task CreateEnvironmentAsync(
        IRInstance instance,
        GameChallenge challenge,
        CancellationToken cancellationToken)
    {
        var instanceId = instance.Id;
        try
        {
            _logger.LogInformation("Creating environment for IR instance {InstanceId}", instanceId);

            var osType = _config["IRSettings:DefaultOsType"] ?? "Linux";
            var isWindows = osType.Equals("Windows", StringComparison.OrdinalIgnoreCase);

            if (isWindows)
            {
                // KVM/Windows environment
                var vmName = $"ir-{instanceId:N}"[..12];
                var templatePath = _config["IRSettings:VmTemplatePath"]
                    ?? "/var/lib/gzctf/templates/windows.qcow2";

                await _vmManager.CreateFromTemplate(templatePath, vmName);
                await _vmManager.Start(vmName);

                // Wait for VM to be ready
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                // Get IP and set up Guacamole RDP with dynamic credentials
                var vmIp = await _vmManager.GetIpAddress(vmName);
                var rdpPort = int.Parse(_config["IRSettings:RdpPort"] ?? "3389");
                var sessionUser = _config["IRSettings:VmUsername"] ?? "player";
                var sessionPass = Codec.RandomPassword(16);
                var (connectionId, guacToken) = await _guacamoleProxy.CreateConnectionWithCredentialsAsync(
                    vmName, vmIp ?? "127.0.0.1", rdpPort, sessionUser, sessionPass);

                // Store access details
                var accessDetails = new Dictionary<string, object?>
                {
                    ["GuacamoleConnectionId"] = connectionId,
                    ["GuacamoleToken"] = guacToken,
                    ["VmName"] = vmName,
                    ["VmIp"] = vmIp,
                    ["AccessUrl"] = _guacamoleProxy.GetConnectionUrl(connectionId, guacToken),
                    ["OsType"] = "Windows"
                };

                instance.AccessDetails = JsonSerializer.Serialize(accessDetails);
            }
            else
            {
                // Docker/Linux environment
                if (!string.IsNullOrEmpty(challenge.ContainerImage))
                {
                    await _containerOrchestrator.PullImageFromRegistryAsync(
                        _config["ContainerSettings:RegistryUrl"] ?? string.Empty,
                        challenge.ContainerImage);

                    await _containerOrchestrator.CreateIsolatedNetwork($"ir-net-{instanceId:N}"[..12]);
                }

                // Generate SSH credentials
                var creds = await _sshAccessService.GenerateCredentialsAsync(instanceId);

                var accessDetails = new Dictionary<string, object?>
                {
                    ["SshHost"] = creds.Host,
                    ["SshPort"] = creds.Port,
                    ["SshUsername"] = creds.Username,
                    ["OsType"] = "Linux"
                };

                instance.AccessDetails = JsonSerializer.Serialize(accessDetails);
            }

            instance.EnvironmentStatus = EnvironmentStatus.Ready;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Environment ready for IR instance {InstanceId}", instanceId);

            // Notify via SignalR
            await _hubContext.Clients
                .Group($"ir_{instance.ChallengeId}")
                .SendAsync(ScenarioHub.EnvironmentReadyEvent, new
                {
                    InstanceId = instanceId,
                    ChallengeId = instance.ChallengeId,
                    Status = "Ready"
                }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create environment for IR instance {InstanceId}", instanceId);

            var fresh = await _context.IRInstances.FindAsync([instanceId], cancellationToken);
            if (fresh is not null)
            {
                fresh.EnvironmentStatus = EnvironmentStatus.Error;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Get status and checkpoint progress for an IR instance.
    /// </summary>
    /// <param name="instanceId">Instance ID</param>
    /// <param name="token"></param>
    [HttpGet("instances/{instanceId:guid}")]
    [RequireUser]
    [ProducesResponseType(typeof(IRInstanceDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstance(Guid instanceId, CancellationToken token)
    {
        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

        if (instance is null)
            return NotFound(new RequestResponse("IR instance not found", StatusCodes.Status404NotFound));

        var detail = await IRInstanceDetailModel.FromInstanceAsync(instance, _context, token);
        return Ok(detail);
    }

    /// <summary>
    /// Submit answer for a ManualAnswer checkpoint.
    /// </summary>
    /// <param name="instanceId">Instance ID</param>
    /// <param name="checkpointId">Checkpoint ID</param>
    /// <param name="model">Submission model</param>
    /// <param name="token"></param>
    [HttpPost("instances/{instanceId:guid}/checkpoints/{checkpointId:int}/submit")]
    [RequireUser]
    [ProducesResponseType(typeof(IRInstanceDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitCheckpoint(
        Guid instanceId,
        int checkpointId,
        [FromBody] CheckpointSubmitModel model,
        CancellationToken token)
    {
        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

        if (instance is null)
            return NotFound(new RequestResponse("IR instance not found", StatusCodes.Status404NotFound));

        if (instance.EnvironmentStatus != EnvironmentStatus.Ready)
            return BadRequest(new RequestResponse("Environment is not ready"));

        var challenge = await _context.GameChallenges
            .FirstOrDefaultAsync(c => c.Id == instance.ChallengeId, token);

        if (challenge is null)
            return NotFound(new RequestResponse("IR challenge not found", StatusCodes.Status404NotFound));

        var phaseCheck = await _phaseService.CheckAsync(challenge.GameId, PhaseRequiredType.IR, token);
        if (phaseCheck != PhaseCheckResult.Allowed)
            return Forbid();

        var checkpoint = await _context.IRCheckpoints
            .FirstOrDefaultAsync(c => c.Id == checkpointId && c.ChallengeId == instance.ChallengeId, token);

        if (checkpoint is null)
            return NotFound(new RequestResponse("Checkpoint not found", StatusCodes.Status404NotFound));

        if (checkpoint.VerificationType != VerificationType.ManualAnswer)
            return BadRequest(new RequestResponse("This checkpoint does not accept manual answer submission"));

        // Parse existing results
        var results = new Dictionary<string, CheckpointResultEntry>();
        if (!string.IsNullOrEmpty(instance.CheckpointResults))
        {
            try
            {
                results = JsonSerializer.Deserialize<Dictionary<string, CheckpointResultEntry>>(
                    instance.CheckpointResults) ?? [];
            }
            catch { }
        }

        var checkpointKey = checkpointId.ToString();
        if (results.TryGetValue(checkpointKey, out var existing) && existing.Completed)
            return BadRequest(new RequestResponse("Checkpoint already completed"));

        // Verify the answer
        var isCorrect = VerifyManualAnswer(checkpoint.VerificationConfig, model.Answer);

        if (!isCorrect)
            return BadRequest(new RequestResponse("Incorrect answer"));

        // Mark as completed
        results[checkpointKey] = new CheckpointResultEntry
        {
            Completed = true,
            Score = checkpoint.Score,
            VerifiedAt = DateTimeOffset.UtcNow
        };

        instance.CheckpointResults = JsonSerializer.Serialize(results);
        await _context.SaveChangesAsync(token);

        // ★PHASE 1 FIX★ Write Submission record for leaderboard visibility
        if (challenge is not null)
        {
            // Get teamId and participationId from the instance's user
            var participation = await _context.Participations
                .FirstOrDefaultAsync(p => p.GameId == challenge.GameId
                    && p.Team!.Members.Any(m => m.Id == instance.UserId), token);
            if (participation is not null)
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<UnifiedScoringEngine>();
                await engine.RecordIRCheckpointCompletionAsync(
                    instance.ChallengeId, instance.UserId, challenge.GameId,
                    participation.TeamId, participation.Id, token);
            }
        }

        _logger.LogInformation("Checkpoint {CheckpointId} completed by manual answer for instance {InstanceId}",
            checkpointId, instanceId);

        // Notify via SignalR
        await _hubContext.Clients
            .Group($"ir_{instance.ChallengeId}")
            .SendAsync(ScenarioHub.CheckpointCompletedEvent, new
            {
                InstanceId = instanceId,
                CheckpointId = checkpointId,
                Score = checkpoint.Score,
                IsRequired = checkpoint.IsRequired
            }, token);

        var detail = await IRInstanceDetailModel.FromInstanceAsync(instance, _context, token);
        return Ok(detail);
    }

    /// <summary>
    /// Request an environment reset for an IR instance.
    /// Rotates credentials, reverts VM snapshot, and resets checkpoint progress.
    /// </summary>
    /// <param name="instanceId">Instance ID</param>
    /// <param name="token"></param>
    [HttpPost("instances/{instanceId:guid}/reset")]
    [RequireUser]
    [ProducesResponseType(typeof(IRInstanceDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetInstance(Guid instanceId, CancellationToken token)
    {
        var instance = await _context.IRInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

        if (instance is null)
            return NotFound(new RequestResponse("IR instance not found", StatusCodes.Status404NotFound));

        if (instance.EnvironmentStatus == EnvironmentStatus.Creating)
            return BadRequest(new RequestResponse("Environment is still being created"));

        if (instance.ResetCount >= 5)
            return BadRequest(new RequestResponse("Maximum reset count reached"));

        instance.ResetCount++;
        instance.EnvironmentStatus = EnvironmentStatus.Creating;
        instance.CheckpointResults = "{}";
        instance.ShellLog = "[]";

        await _context.SaveChangesAsync(token);

        // Rotate credentials
        try
        {
            await _sshAccessService.RotateCredentialsAsync(instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rotate SSH credentials during reset for {InstanceId}", instanceId);
        }

        // Reset environment in background
        _ = ResetEnvironmentAsync(instance, token);

        var detail = await IRInstanceDetailModel.FromInstanceAsync(instance, _context, token);
        return Ok(detail);
    }

    private async Task ResetEnvironmentAsync(IRInstance instance, CancellationToken cancellationToken)
    {
        var instanceId = instance.Id;
        try
        {
            _logger.LogInformation("Resetting environment for IR instance {InstanceId}", instanceId);

            var accessDetails = instance.AccessDetails;
            var isWindows = false;

            if (!string.IsNullOrEmpty(accessDetails))
            {
                try
                {
                    var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(accessDetails);
                    isWindows = details?.GetValueOrDefault("OsType")?.ToString() == "Windows";
                }
                catch { }
            }

            if (isWindows)
            {
                if (!string.IsNullOrEmpty(accessDetails))
                {
                    var details = JsonSerializer.Deserialize<Dictionary<string, object?>>(accessDetails);
                    var vmName = details?.GetValueOrDefault("VmName")?.ToString();
                    if (!string.IsNullOrEmpty(vmName))
                    {
                        await _vmManager.SnapshotRevert(vmName);
                    }
                }
            }

            // Re-fetch instance and set ready
            var fresh = await _context.IRInstances.FindAsync([instanceId], cancellationToken);
            if (fresh is not null)
            {
                fresh.EnvironmentStatus = EnvironmentStatus.Ready;
                await _context.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("Environment reset complete for IR instance {InstanceId}", instanceId);

            // Notify via SignalR
            await _hubContext.Clients
                .Group($"ir_{instance.ChallengeId}")
                .SendAsync(ScenarioHub.EnvironmentResetCompleteEvent, new
                {
                    InstanceId = instanceId,
                    ResetCount = instance.ResetCount
                }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset environment for IR instance {InstanceId}", instanceId);

            var fresh = await _context.IRInstances.FindAsync([instanceId], cancellationToken);
            if (fresh is not null)
            {
                fresh.EnvironmentStatus = EnvironmentStatus.Error;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    #endregion

    #region Helpers

    private static bool VerifyManualAnswer(string? verificationConfig, string answer)
    {
        if (string.IsNullOrEmpty(verificationConfig))
            return false;

        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, object?>>(verificationConfig);
            if (config is null)
                return false;

            var expected = config.GetValueOrDefault("ExpectedAnswer")?.ToString();
            if (string.IsNullOrEmpty(expected))
                return false;

            var caseSensitive = config.TryGetValue("CaseSensitive", out var cs)
                && cs is JsonElement e && e.ValueKind == JsonValueKind.True;

            return caseSensitive
                ? string.Equals(answer, expected, StringComparison.Ordinal)
                : string.Equals(answer, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
