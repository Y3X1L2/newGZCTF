using System.Text.RegularExpressions;

namespace GZCTF.TeamLab.Contracts.Execution;

/// <summary>A dedicated, already configured layer-two interface. No host address or route is changed.</summary>
public sealed record TeamLabConnectorAttachmentV2(Guid ConnectorId, Guid NodeId, string InterfaceName, string MacAddress)
{
    public string PortKey => $"connector-{ConnectorId:N}";

    public bool IsValid() => ConnectorId != Guid.Empty && NodeId != Guid.Empty &&
        InterfaceName is { Length: > 0 and <= 15 } && InterfaceName != "lo" &&
        Regex.IsMatch(InterfaceName, "^[a-zA-Z0-9_-]+$") &&
        MacAddress is not null && Regex.IsMatch(MacAddress, "^[0-9a-fA-F]{2}(:[0-9a-fA-F]{2}){5}$");
}
