using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed record VmSourceMigrationReference(
    string Path, string FileIdentity, string RegistryAddress, string Repository, string Tag, Guid? CleanupId = null)
{
    public string Encode()
    {
        var json = JsonSerializer.Serialize(this);
        if (json.Length > 512)
            throw new ImageSourceMigrationContractException("source_migration_reference_too_long",
                "The captured source identity exceeds the existing operation checkpoint limit.", 422);
        return json;
    }

    public static VmSourceMigrationReference Decode(string json) =>
        JsonSerializer.Deserialize<VmSourceMigrationReference>(json)
        ?? throw new ApiOperationTerminalException("source_migration_checkpoint_invalid", "Source checkpoint is invalid.");
}
