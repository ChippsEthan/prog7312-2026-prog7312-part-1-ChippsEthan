using System.Text.Json.Serialization;
using SmartX.API.Models;

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowWpf");


var sensors = new Dictionary<string, Sensor>();
var telemetryLog = new List<object>();

var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
Directory.CreateDirectory(uploadsPath);

app.MapPost("/api/sensors", (Sensor sensor) =>
{
    sensors[sensor.Id] = sensor;
    return Results.Ok(sensor);
});

app.MapGet("/api/sensors", () => Results.Ok(sensors.Values));


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

app.MapPost("/api/sensors/{sensorId}/upload", async (string sensorId, IFormFile file) =>
{
    if (!sensors.ContainsKey(sensorId))
        return Results.NotFound("Sensor not found");

    var filePath = Path.Combine(uploadsPath, $"{sensorId}_{file.FileName}");
    using (var stream = File.Create(filePath))
    {
        await file.CopyToAsync(stream);
    }

    sensors[sensorId].AttachedFiles.Add(filePath);
    return Results.Ok(new { path = filePath });
}).DisableAntiforgery();


app.MapPost("/api/deployment/validate", (DeploymentNode root) =>
{
    bool isValid = DeploymentNode.ValidateTree(root);
    return Results.Ok(new { valid = isValid });
});

app.Run();