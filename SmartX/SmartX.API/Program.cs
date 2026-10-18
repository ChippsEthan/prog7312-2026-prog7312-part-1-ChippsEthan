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
builder.Services.AddSingleton<AlertTracker>();
builder.Services.AddSingleton<TelemetryQueues>();
builder.Services.AddSingleton<TelemetryIntake>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddHostedService<TelemetryProcessor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowWpf");

app.Services.GetRequiredService<DeviceRegistry>().SeedDefaults();

var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
Directory.CreateDirectory(uploadsPath);

IResult Respond<T>(IntakeResult result, T packet)
{
    if (result.Accepted)
        return Results.Ok(new { queued = true, priority = result.IsCritical, packet });

    return result.DeviceNotFound
        ? Results.NotFound(result.Error)
        : Results.BadRequest(result.Error);
}

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

app.MapPost("/api/telemetry/float", (TelemetryPacket<float> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value, "float"), packet));

app.MapPost("/api/telemetry/int", (TelemetryPacket<int> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value, "int"), packet));

app.MapPost("/api/telemetry/bool", (TelemetryPacket<bool> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value ? 1.0 : 0.0, "bool"), packet));

app.MapGet("/api/telemetry", () => Results.Ok(TelemetryStore.Snapshot()));

app.MapGet("/api/queues/stats", (TelemetryQueues queues) => Results.Ok(queues.GetStats()));

app.MapPost("/api/queues/delay", (int ms, TelemetryQueues queues) =>
{
    queues.SimulatedDelayMs = Math.Clamp(ms, 0, 1000);
    return Results.Ok(new { delayMs = queues.SimulatedDelayMs });
});


app.MapPost("/api/queues/demo-burst", (string mac, int? count, DeviceRegistry registry, TelemetryIntake intake) =>
{
    var device = registry.GetByMac(mac);
    if (device is null) return Results.NotFound($"No device with MAC {mac}");

    double normal, critical;
    switch (device.Metric)
    {
        case "Moisture": normal = 55; critical = 5; break;
        case "Temperature": normal = 28; critical = 45; break;
        case "Power": normal = 900; critical = 3200; break;
        default: return Results.BadRequest("Pick a Moisture, Temperature or Power device.");
    }

    int n = Math.Clamp(count ?? 50, 1, 500);
    for (int i = 0; i < n; i++)
        intake.Submit(device.MacAddress, normal, "float");

    intake.Submit(device.MacAddress, critical, "float");  

    return Results.Ok(new { standardQueued = n, criticalQueued = 1 });
});

app.MapPost("/api/commands", (CommandRequest request, CommandService commands) =>
{
    var result = commands.Execute(request.Mac, request.Action);
    return result.Success ? Results.Ok(result.Command) : Results.BadRequest(result.Error);
});

app.MapPost("/api/commands/undo", (CommandService commands) =>
{
    var result = commands.Undo();
    return result.Success ? Results.Ok(result.Command) : Results.BadRequest(result.Error);
});

app.MapGet("/api/commands", (CommandService commands) =>
    Results.Ok(new { undoDepth = commands.UndoDepth, nextUndo = commands.PeekUndo(), history = commands.GetLog() }));

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


app.MapGet("/api/alerts", (AlertTracker alerts) => Results.Ok(alerts.GetActive()));

app.MapPost("/api/alerts/test-duplicates", (string mac, DeviceRegistry registry, AlertTracker alerts) =>
{
    var device = registry.GetByMac(mac);
    if (device is null) return Results.NotFound($"No device with MAC {mac}");

    int newAlerts = 0, duplicates = 0;
    for (int i = 0; i < 5; i++)
    {
        if (alerts.Raise(device.MacAddress, device.Name, AlertTypes.Disconnected, AlertLevel.Critical, "Test disconnect"))
            newAlerts++;
        else
            duplicates++;
    }
    return Results.Ok(new { attempts = 5, newAlerts, duplicatesBlocked = duplicates });
});

app.MapDelete("/api/alerts/{mac}", (string mac, AlertTracker alerts) =>
    Results.Ok(new { cleared = alerts.ClearAll(mac) }));

app.MapPost("/api/deployment/validate", (DeploymentNode root) =>
{
    bool isValid = DeploymentNode.ValidateTree(root);
    return Results.Ok(new { valid = isValid });
});

app.Run();