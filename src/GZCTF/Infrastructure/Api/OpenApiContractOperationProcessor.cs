using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace GZCTF.Infrastructure.Api;

public sealed class OpenApiContractOperationProcessor : IOperationProcessor
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string DockerArchivePath = "/api/open/v1/images/docker-archives";

    public bool Process(OperationProcessorContext context)
    {
        var operation = context.OperationDescription.Operation;
        foreach (var parameter in operation.Parameters.Where(parameter =>
                     string.Equals(parameter.Name, IdempotencyKeyHeader, StringComparison.OrdinalIgnoreCase)))
            parameter.IsRequired = true;

        if (!string.Equals(
                context.OperationDescription.Path,
                DockerArchivePath,
                StringComparison.Ordinal))
            return true;

        var requestBody = operation.RequestBody;
        if (requestBody is null ||
            requestBody.Content is null ||
            !requestBody.Content.TryGetValue("multipart/form-data", out var content))
            return true;
        var schema = content.Schema;
        if (schema is null)
            return true;

        requestBody.IsRequired = true;
        foreach (var propertyName in new[] { "file", "name" })
        {
            schema.RequiredProperties.Add(propertyName);
            if (schema.Properties.TryGetValue(propertyName, out var property))
                property.IsNullableRaw = false;
        }

        return true;
    }
}
