namespace GZCTF.Utils;

public static class ResourceOwnershipPolicy
{
    public static bool CanManage(Guid? ownerId, Guid actorId, Role role) =>
        role >= Role.Admin || ownerId == actorId;
}
