using System.Text.Json;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Application;

internal static class TeamLabConnectorConfiguration
{
    public static bool TryGet(TeamLabConnector connector, out TeamLabConnectorAttachmentV2 attachment)
    {
        attachment = default!;
        if (connector.Kind != TeamLabConnectorKind.ManagedNic || connector.SupportsSharedUse || connector.IsArchived)
            return false;
        try
        {
            var binding = JsonSerializer.Deserialize<TeamLabManagedNicModel>(connector.AttachmentReference ?? "null");
            if (binding is null) return false;
            var result = new TeamLabConnectorAttachmentV2(
                connector.PublicId, binding.NodeId, binding.InterfaceName, binding.MacAddress);
            if (!result.IsValid()) return false;
            attachment = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static TeamLabConnectorAttachmentV2 Require(TeamLabConnector connector)
    {
        if (connector.Kind != TeamLabConnectorKind.ManagedNic || connector.SupportsSharedUse || connector.IsArchived)
            throw new TeamLabApiContractException("connector_provider_unsupported", "当前仅支持独占专用网卡的实际接入；其他连接器类型尚不可执行。", 422);
        if (TryGet(connector, out var attachment)) return attachment;
        throw new TeamLabApiContractException("connector_configuration_required", "连接器缺少有效的节点、专用网卡名称及 MAC 登记，请重新登记。", 422);
    }
}
