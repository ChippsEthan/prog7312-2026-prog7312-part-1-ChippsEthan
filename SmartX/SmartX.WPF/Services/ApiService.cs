using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using SmartX.WPF.Models;

namespace SmartX.WPF.Services;

public class ApiService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public ApiService()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5106"),
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    
    public Task<HttpResponseMessage> RegisterSensorAsync(object sensor) =>
        _client.PostAsJsonAsync("/api/sensors", sensor);

    public Task<HttpResponseMessage> SendTelemetryFloatAsync(object packet) =>
        _client.PostAsJsonAsync("/api/telemetry/float", packet);

    public Task<HttpResponseMessage> SendTelemetryIntAsync(object packet) =>
        _client.PostAsJsonAsync("/api/telemetry/int", packet);

    public Task<HttpResponseMessage> SendTelemetryBoolAsync(object packet) =>
        _client.PostAsJsonAsync("/api/telemetry/bool", packet);

    public async Task<HttpResponseMessage> UploadFileAsync(string sensorId, string filePath)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);   // streamed not loaded
        form.Add(new StreamContent(stream), "file", Path.GetFileName(filePath));
        return await _client.PostAsync($"/api/sensors/{sensorId}/upload", form);
    }

    public Task<HttpResponseMessage> ValidateDeploymentAsync(object tree) =>
        _client.PostAsJsonAsync("/api/deployment/validate", tree);


    public async Task<List<SensorDto>> GetDevicesAsync() =>
        await _client.GetFromJsonAsync<List<SensorDto>>("/api/sensors", Json) ?? new();

    public async Task<List<AlertDto>> GetAlertsAsync() =>
        await _client.GetFromJsonAsync<List<AlertDto>>("/api/alerts", Json) ?? new();

    public async Task<InsightsDto> GetInsightsAsync() =>
        await _client.GetFromJsonAsync<InsightsDto>("/api/insights", Json) ?? new();

    public async Task<QueueStatsDto> GetQueueStatsAsync() =>
        await _client.GetFromJsonAsync<QueueStatsDto>("/api/queues/stats", Json) ?? new();

    public async Task<CommandHistoryDto> GetCommandsAsync() =>
        await _client.GetFromJsonAsync<CommandHistoryDto>("/api/commands", Json) ?? new();

    public async Task<List<ReadingDto>> GetHistoryAsync(string mac, int count) =>
        await _client.GetFromJsonAsync<List<ReadingDto>>($"/api/devices/{mac}/history?count={count}", Json) ?? new();


    public async Task<ApiResult> SendCommandAsync(string mac, string action)
    {
        var response = await _client.PostAsJsonAsync("/api/commands", new { mac, action });
        return await ToResultAsync(response);
    }

    public async Task<ApiResult> UndoAsync()
    {
        var response = await _client.PostAsync("/api/commands/undo", null);
        return await ToResultAsync(response);
    }

    public async Task RecordSearchAsync(string query)
    {
        await _client.PostAsync($"/api/insights/search?query={Uri.EscapeDataString(query)}", null);
    }

    public async Task<ApiResult> SetQueueDelayAsync(int ms)
    {
        var response = await _client.PostAsync($"/api/queues/delay?ms={ms}", null);
        return await ToResultAsync(response, "Delay updated");
    }

    public async Task<ApiResult> DemoBurstAsync(string mac, int count)
    {
        var response = await _client.PostAsync($"/api/queues/demo-burst?mac={Uri.EscapeDataString(mac)}&count={count}", null);
        return await ToResultAsync(response, "Burst sent");
    }

    private static async Task<ApiResult> ToResultAsync(HttpResponseMessage response, string okFallback = "OK")
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new ApiResult(false, string.IsNullOrWhiteSpace(body) ? (response.ReasonPhrase ?? "Request failed") : body.Trim('"'));

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("summary", out var summary))
                return new ApiResult(true, summary.GetString() ?? okFallback);
        }
        catch (JsonException) { }

        return new ApiResult(true, okFallback);
    }
}