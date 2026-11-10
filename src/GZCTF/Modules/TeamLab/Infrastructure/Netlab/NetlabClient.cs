using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Modules.TeamLab.Application;
using System.Text;
using Newtonsoft.Json;

namespace GZCTF.Modules.TeamLab.Infrastructure.Netlab;

public sealed class NetlabClient(NetlabConnection connection, IHttpClientFactory clients)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token,
        RequestScope? scope = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        using var request = connection.Request(method, "api/v1/" + path, scope);
        if (body is not null) request.Content = new StringContent(Serialize(body), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient("Netlab").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await EnsureSuccessAsync(response, token);
        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(token))!;
    }

    public Task<T> GetAsync<T>(string path, CancellationToken token, RequestScope? scope = null) =>
        SendAsync<T>(HttpMethod.Get, path, null, token, scope);

    internal static string Serialize(object value) => JsonConvert.SerializeObject(value,
        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    public async Task<IReadOnlyList<T>> ListAsync<T>(string path, Func<T, string> id, CancellationToken token,
        RequestScope? scope = null)
    {
        List<T> result = [];
        string? cursor = null;
        do
        {
            var page = await GetAsync<List<T>>($"{path}{(path.Contains('?') ? '&' : '?')}limit=100&cursor={Uri.EscapeDataString(cursor ?? "")}", token, scope);
            result.AddRange(page);
            cursor = page.Count == 100 ? id(page[^1]) : null;
        } while (cursor is not null);
        return result;
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(token);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true)
        {
            using var document = JsonDocument.Parse(detail);
            if (document.RootElement.TryGetProperty("detail", out var value)) detail = value.GetString() ?? detail;
        }
        throw new TeamLabApiContractException("netlab_request_failed", detail, (int)response.StatusCode);
    }
}
