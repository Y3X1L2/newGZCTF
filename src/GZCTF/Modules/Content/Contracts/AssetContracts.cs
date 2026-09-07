using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.Content.Contracts;

public sealed record AssetDescriptor(string Hash, string Name, long Size, string RemoteUrl);

public sealed record AssetUploadResult(AssetDescriptor Asset, Guid OperationId, bool Reused);

public sealed class AssetApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
