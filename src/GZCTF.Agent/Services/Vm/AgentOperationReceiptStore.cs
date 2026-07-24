using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Vm;

public sealed class AgentOperationReceiptStore(
    AgentResourceLock resourceLock,
    IOptions<AgentConfig> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(
        Path.GetFullPath(options.Value.OperationStateRoot), "operation-receipts");

    public async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        string operationKind,
        Guid operationId,
        TRequest request,
        Func<CancellationToken, Task<TResponse>> action,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || operationKind.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-'))
            throw Conflict("operation_identity_invalid", "Operation identity is invalid.");

        await using var operationLock = await resourceLock.AcquireAsync(
            $"operation-receipt:{operationKind}:{operationId:N}", cancellationToken);
        var directory = Path.Combine(_root, operationKind, operationId.ToString("N"));
        Directory.CreateDirectory(directory);
        var requestHash = ComputeRequestHash(request);
        var identityPath = Path.Combine(directory, "identity.json");
        var resultPath = Path.Combine(directory, "result.json");

        var identity = await ReadAsync<ReceiptIdentity>(identityPath, cancellationToken);
        if (identity is not null && !string.Equals(identity.RequestHash, requestHash, StringComparison.Ordinal))
            throw Conflict("operation_identity_conflict",
                "The operation id was already used with a different request.");
        if (identity is null)
            await WriteAtomicAsync(identityPath, new ReceiptIdentity(requestHash), cancellationToken);

        var receipt = await ReadAsync<ReceiptResult<TResponse>>(resultPath, cancellationToken);
        if (receipt is not null)
        {
            if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
                throw Conflict("operation_identity_conflict",
                    "The operation id was already used with a different request.");
            return receipt.Response;
        }

        var response = await action(cancellationToken);
        await WriteAtomicAsync(resultPath, new ReceiptResult<TResponse>(requestHash, response), cancellationToken);
        return response;
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(input, JsonOptions, cancellationToken);
    }

    private static string ComputeRequestHash<T>(T request)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions));
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, document.RootElement);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported receipt JSON kind {element.ValueKind}.");
        }
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, value, JsonOptions, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    private static AgentOperationException Conflict(string code, string message) =>
        new("Conflict", code, message, false, StatusCodes.Status409Conflict);

    private sealed record ReceiptIdentity(string RequestHash);
    private sealed record ReceiptResult<T>(string RequestHash, T Response);
}
