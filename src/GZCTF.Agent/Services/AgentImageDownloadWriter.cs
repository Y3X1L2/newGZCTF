using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services;

public sealed class AgentImageDownloadWriter(AgentImageStorageBudget budget)
{
    public async Task CopyAsync(HttpResponseMessage response, string temporaryPath, bool append,
        long? expectedSize, CancellationToken token, Action<long, long>? reportProgress = null)
    {
        // HttpClient.Timeout with ResponseHeadersRead does not bound the body stream.
        // A stalled writer must eventually unwind and release its in-memory allowance.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        token = deadline.Token;
        var existing = append && File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
        var range = response.Content.Headers.ContentRange;
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > long.MaxValue - existing || (!append && range is not null && range.From != 0))
            throw InvalidSize("Image response size or range is invalid.");
        if (append && (range?.From != existing || range.Length is null || range.To != range.Length - 1))
            throw InvalidSize("Image resume response does not cover the requested remaining bytes.");
        var announcedSize = range?.Length ?? (contentLength.HasValue ? checked(existing + contentLength.Value) : null);
        if (expectedSize is > 0 && announcedSize.HasValue && expectedSize != announcedSize)
            throw InvalidSize("Image response size does not match the expected artifact size.");
        var total = expectedSize is > 0 ? expectedSize.Value : announcedSize;
        if (total is null || total < existing || total <= 0)
            throw new AgentOperationException("Storage", "image.storage_size_unknown",
                "Image transfer requires an expected size or a valid response size before writing.", false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(temporaryPath))!;
        using var lease = budget.Reserve(directory, total.Value - existing, "image-download");
        await using var output = new FileStream(temporaryPath, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, bufferSize: 1, useAsync: true);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920];
        long transferred = existing;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            lease.CheckBeforeWrite(read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            lease.RecordWritten(read);
            transferred += read;
            reportProgress?.Invoke(transferred, total.Value);
        }
        await output.FlushAsync(token);
        if (transferred != total.Value)
            throw InvalidSize("Image response ended before the admitted artifact size was received.");
        lease.CheckCapacity();
    }

    static AgentOperationException InvalidSize(string message) =>
        new("ImageTransfer", "image.size_mismatch", message, false);
}
