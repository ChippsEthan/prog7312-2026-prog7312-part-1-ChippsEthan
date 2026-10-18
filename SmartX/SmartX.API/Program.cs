using System.Text.Json.Serialization;
using SmartX.API.Models;
using SmartX.API.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWpf", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});


builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<HistoryStore>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowWpf");

app.Services.GetRequiredService<DeviceRegistry>().SeedDefaults();

var telemetryLog = new List<object>();

var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
Directory.CreateDirectory(uploadsPath);


app.MapPost("/api/sensors", (Sensor sensor, DeviceRegistry registry) =>
{
    var result = registry.Register(sensor);
    if (result.Success) return Results.Ok(result.Sensor);
    return result.IsConflict ? Results.Conflict(result.Error) : Results.BadRequest(result.Error);
});

app.MapGet("/api/sensors", (DeviceRegistry registry) => Results.Ok(registry.GetAll()));

app.MapGet("/api/devices/{mac}", (string mac, DeviceRegistry registry) =>
    registry.GetByMac(mac) is { } device
        ? Results.Ok(device)
        : Results.NotFound($"No device with MAC {mac}"));

app.MapGet("/api/devices/{mac}/history", (string mac, int? count, DeviceRegistry registry, HistoryStore history) =>
{
    if (!registry.Exists(mac)) return Results.NotFound($"No device with MAC {mac}");
    return Results.Ok(history.GetLatest(mac, count ?? 100));
});


app.MapPost("/api/telemetry/float", (TelemetryPacket<float> packet) =>
{
    telemetryLog.Add(packet);
    TelemetryStore.AddBatch(new double[] { packet.Value }, packet.DeviceId);
    return Results.Ok(packet);
});

app.MapPost("/api/telemetry/int", (TelemetryPacket<int> packet) =>
{
    telemetryLog.Add(packet);
    return Results.Ok(packet);
});

app.MapPost("/api/telemetry/bool", (TelemetryPacket<bool> packet) =>
{
    telemetryLog.Add(packet);
    return Results.Ok(packet);
});

app.MapGet("/api/telemetry", () => Results.Ok(telemetryLog));


app.MapPost("/api/sensors/{sensorId}/upload", async (string sensorId, IFormFile file, DeviceRegistry registry) =>
{
    if (registry.GetById(sensorId) is null)
        return Results.NotFound("Sensor not found");

    var safeName = Path.GetFileName(file.FileName);
    var filePath = Path.Combine(uploadsPath, $"{sensorId}_{safeName}");
    await using (var stream = File.Create(filePath))
    {
        await file.CopyToAsync(stream);
    }

    registry.AddAttachment(sensorId, filePath);
    return Results.Ok(new { path = filePath });
}).DisableAntiforgery();


app.MapPost("/api/deployment/validate", (DeploymentNode root) =>
{
    bool isValid = DeploymentNode.ValidateTree(root);
    return Results.Ok(new { valid = isValid });
});

app.Run();