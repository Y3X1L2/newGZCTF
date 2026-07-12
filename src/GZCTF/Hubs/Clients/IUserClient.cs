using GZCTF.Modules.Penetration.Contracts;

namespace GZCTF.Hubs.Clients;

public interface IUserClient
{
    public Task ReceivedGameNotice(GameNotice notice);

    public Task ReceivedPenetrationWorkspaceUpdate(PenetrationWorkspaceUpdateModel update);
}
