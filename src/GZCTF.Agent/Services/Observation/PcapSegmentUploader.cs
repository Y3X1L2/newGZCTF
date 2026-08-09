using System.Net.Http.Headers;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Observation;

public sealed class PcapSegmentUploader(
    IHttpClientFactory clients,
    IOptions<AgentConfig> options)
{
    private readonly AgentConfig _config = options.Value;

    public async Task<(bool Success, string Message)> UploadAsync(
        string uploadPath,
        string uploadToken,
        string filePath,
        string sha256,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return (false, "Capture segment file is unavailable.");
        var length = new FileInfo(filePath).Length;
        if (length <= 0 || length > maxBytes)
            return (false, "Capture segment size exceeds the upload authorization.");
        var endpoint = Uri.TryCreate(uploadPath, UriKind.Absolute, out var absolute) &&
                       (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? absolute
            : new Uri(new Uri(_config.ServerUrl.TrimEnd('/') + "/"), uploadPath.TrimStart('/'));
        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", uploadToken);
        request.Headers.TryAddWithoutValidation("X-GZCTF-Worker-Node", _config.NodeId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Content-SHA256", sha256);
        await using var stream = File.OpenRead(filePath);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.tcpdump.pcap");
        request.Content.Headers.ContentLength = length;
        using var response = await clients.CreateClient().SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
            return (true, "Capture segment uploaded.");
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        return (false, $"Capture upload failed with HTTP {(int)response.StatusCode}: {Trim(detail)}");
    }

    private static string Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? "no response detail" : value.Trim()[..Math.Min(value.Trim().Length, 512)];
}
