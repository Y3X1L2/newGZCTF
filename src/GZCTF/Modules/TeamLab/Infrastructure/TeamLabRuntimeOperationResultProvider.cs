using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationResultProvider(
    AppDbContext context,
    TeamLabAccessGrantService access,
    IDataProtectionProvider protection) : IApiOperationResultProvider
{
    private const string SecretPurpose = "GZCTF.TeamLab.Webhook.v1";
    public string Kind => TeamLabRuntimeOperationApplicationService.OperationKind;

    public async Task<JsonElement?> GetResultAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.AsNoTracking()
            .Where(item => item.OperationId == operationId)
            .Select(item => new { item.Kind, item.ResultJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null || string.IsNullOrWhiteSpace(job.ResultJson)) return null;
        if (job.Kind == TeamLabRuntimeOperationKind.WebhookCreate)
            return await GetWebhookCreationResultAsync(operationId, cancellationToken);
        if (job.Kind == TeamLabRuntimeOperationKind.AccessGrantCreate)
        {
            var result = await access.GetOperationResultAsync(operationId, cancellationToken);
            return result is null ? null : JsonSerializer.SerializeToElement(result);
        }
        using var document = JsonDocument.Parse(job.ResultJson);
        return document.RootElement.Clone();
    }

    private async Task<JsonElement?> GetWebhookCreationResultAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (transaction is not null)
        {
            var lockKey = $"teamlab:webhook-secret:{operationId:N}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }

        var job = await context.TeamLabRuntimeOperationJobs.SingleAsync(
            item => item.OperationId == operationId, cancellationToken);
        if (string.IsNullOrWhiteSpace(job.ResultJson)) return null;
        var state = DeserializeCreationState(job.ResultJson);
        string? secret = null;
        if (!state.SigningSecretIssued)
        {
            var encrypted = await context.TeamLabWebhookSubscriptions.AsNoTracking()
                .Where(item => item.ApiOperationId == operationId)
                .Select(item => item.SigningSecretEncrypted)
                .SingleAsync(cancellationToken);
            secret = protection.CreateProtector(SecretPurpose).Unprotect(encrypted);
            state = state with { SigningSecretIssued = true };
            job.ResultJson = JsonSerializer.Serialize(state);
            await context.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(
            new TeamLabWebhookCreationResultModel(state.Webhook, secret));
    }

    private static WebhookCreationState DeserializeCreationState(string json)
    {
        var state = JsonSerializer.Deserialize<WebhookCreationState>(json, JsonOptions);
        if (state?.Webhook is not null) return state;
        var webhook = JsonSerializer.Deserialize<TeamLabWebhookModel>(json, JsonOptions)
            ?? throw new JsonException("Webhook creation result is invalid.");
        return new WebhookCreationState(webhook, false);
    }

    private sealed record WebhookCreationState(
        TeamLabWebhookModel Webhook,
        bool SigningSecretIssued);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
