using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>Delivery transport boundary so delivery logic stays testable without HTTP.</summary>
public interface ITeamLabWebhookDeliverer
{
    Task<TeamLabWebhookDeliveryResult> DeliverAsync(
        TeamLabWebhookSubscriptionView subscription,
        TeamLabWebhookEventEnvelope envelope,
        string body,
        string signature,
        CancellationToken cancellationToken);
}

public sealed record TeamLabWebhookSubscriptionView(
    Guid Id,
    string EndpointUrl,
    string SigningSecret);

public sealed record TeamLabWebhookDeliveryResult(
    bool Succeeded,
    string Error);
