using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartX.WPF.Services;

public class ApiService
{
    private readonly HttpClient _client;

    public ApiService()
    {
        _client = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5106")
        };
    }

    public async Task<HttpResponseMessage> RegisterSensorAsync(object sensor)
    {
        return await _client.PostAsJsonAsync("/api/sensors", sensor);
    }

    public async Task<List<JsonElement>> GetSensorsAsync()
    {
        var result = await _client.GetFromJsonAsync<List<JsonElement>>("/api/sensors");
        return result ?? new List<JsonElement>();
    }

    public async Task<HttpResponseMessage> SendTelemetryFloatAsync(object packet)
    {
        return await _client.PostAsJsonAsync("/api/telemetry/float", packet);
    }

    public async Task<HttpResponseMessage> SendTelemetryIntAsync(object packet)
    {
        return await _client.PostAsJsonAsync("/api/telemetry/int", packet);
    }

    public async Task<HttpResponseMessage> SendTelemetryBoolAsync(object packet)
    {
        return await _client.PostAsJsonAsync("/api/telemetry/bool", packet);
    }

    public async Task<List<JsonElement>> GetTelemetryHistoryAsync()
    {
        var result = await _client.GetFromJsonAsync<List<JsonElement>>("/api/telemetry");
        return result ?? new List<JsonElement>();
    }

    public async Task<HttpResponseMessage> UploadFileAsync(string sensorId, string filePath)
    {
        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        return await _client.PostAsync($"/api/sensors/{sensorId}/upload", form);
    }

    public async Task<HttpResponseMessage> ValidateDeploymentAsync(object tree)
    {
        return await _client.PostAsJsonAsync("/api/deployment/validate", tree);
    }
}