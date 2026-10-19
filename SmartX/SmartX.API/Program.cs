using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartX.API.Models;
using SmartX.API.Services;

var builder = WebApplication.CreateBuilder(args);

var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (string.IsNullOrWhiteSpace(firebaseProjectId) || firebaseProjectId.StartsWith("PASTE"))
    throw new InvalidOperationException(
        "Firebase:ProjectId is not set. Open SmartX.API/appsettings.json and paste your Firebase Project ID.");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste your Firebase ID token (use 'Copy API token' in the app)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

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
builder.Services.AddSingleton<PredictionEngine>();
builder.Services.AddHostedService<TelemetryProcessor>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowWpf");
app.UseAuthentication();
app.UseAuthorization();

app.Services.GetRequiredService<DeviceRegistry>().SeedDefaults();
app.Services.GetRequiredService<PredictionEngine>().SeedHistoricalPatterns();

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

app.MapPost("/api/telemetry/float", (TelemetryPacket<float> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value, "float"), packet)).AllowAnonymous();

app.MapPost("/api/telemetry/int", (TelemetryPacket<int> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value, "int"), packet)).AllowAnonymous();

app.MapPost("/api/telemetry/bool", (TelemetryPacket<bool> packet, TelemetryIntake intake) =>
    Respond(intake.Submit(packet.DeviceId, packet.Value ? 1.0 : 0.0, "bool"), packet)).AllowAnonymous();

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
{
    email = user.FindFirst("email")?.Value,
    uid = user.FindFirst("user_id")?.Value ?? user.FindFirst("sub")?.Value
}));

api.MapPost("/sensors", (Sensor sensor, DeviceRegistry registry) =>
{
    var result = registry.Register(sensor);
    if (result.Success) return Results.Ok(result.Sensor);
    return result.IsConflict ? Results.Conflict(result.Error) : Results.BadRequest(result.Error);
});

api.MapGet("/sensors", (DeviceRegistry registry) => Results.Ok(registry.GetAll()));

api.MapGet("/devices/{mac}", (string mac, DeviceRegistry registry) =>
    registry.GetByMac(mac) is { } device
        ? Results.Ok(device)
        : Results.NotFound($"No device with MAC {mac}"));

api.MapGet("/devices/{mac}/history", (string mac, int? count, DeviceRegistry registry, HistoryStore history) =>
{
    if (!registry.Exists(mac)) return Results.NotFound($"No device with MAC {mac}");
    return Results.Ok(history.GetLatest(mac, count ?? 100));
});

api.MapGet("/telemetry", () => Results.Ok(TelemetryStore.Snapshot()));

api.MapGet("/queues/stats", (TelemetryQueues queues) => Results.Ok(queues.GetStats()));

api.MapPost("/queues/delay", (int ms, TelemetryQueues queues) =>
{
    queues.SimulatedDelayMs = Math.Clamp(ms, 0, 1000);
    return Results.Ok(new { delayMs = queues.SimulatedDelayMs });
});

api.MapPost("/queues/demo-burst", (string mac, int? count, DeviceRegistry registry, TelemetryIntake intake) =>
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


api.MapPost("/commands", (CommandRequest request, CommandService commands, PredictionEngine prediction) =>
{
    var result = commands.Execute(request.Mac, request.Action);
    if (!result.Success) return Results.BadRequest(result.Error);

    prediction.RecordCommand(request.Mac, request.Action);   
    return Results.Ok(result.Command);
});

api.MapPost("/commands/undo", (CommandService commands) =>
{
    var result = commands.Undo();
    return result.Success ? Results.Ok(result.Command) : Results.BadRequest(result.Error);
});

api.MapGet("/commands", (CommandService commands) =>
    Results.Ok(new { undoDepth = commands.UndoDepth, nextUndo = commands.PeekUndo(), history = commands.GetLog() }));


api.MapGet("/insights", (PredictionEngine prediction) => Results.Ok(prediction.GetInsights()));

api.MapPost("/insights/search", (string query, PredictionEngine prediction) =>
{
    var device = prediction.RecordSearch(query);
    return Results.Ok(new { recorded = device is not null, device });
});

api.MapPost("/insights/demo-trend", async (string mac, DeviceRegistry registry, TelemetryIntake intake) =>
{
    var device = registry.GetByMac(mac);
    if (device is null) return Results.NotFound($"No device with MAC {mac}");

    var (start, step) = device.Metric switch
    {
        "Moisture" => (75.0, -7.0),
        "Temperature" => (30.0, -2.0),
        "Power" => (1000.0, 250.0),
        _ => (0.0, 0.0)
    };

    if (step == 0) return Results.BadRequest("Pick a Moisture, Temperature or Power device.");

    for (int i = 0; i < 7; i++)
    {
        intake.Submit(device.MacAddress, start + step * i, "float");
        await Task.Delay(250);
    }

    return Results.Ok(new { sent = 7, next = "Now call GET /api/insights" });
});


api.MapPost("/sensors/{sensorId}/upload", async (string sensorId, IFormFile file, DeviceRegistry registry) =>
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


api.MapGet("/alerts", (AlertTracker alerts) => Results.Ok(alerts.GetActive()));

api.MapPost("/alerts/test-duplicates", (string mac, DeviceRegistry registry, AlertTracker alerts) =>
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

api.MapDelete("/alerts/{mac}", (string mac, AlertTracker alerts) =>
    Results.Ok(new { cleared = alerts.ClearAll(mac) }));

api.MapPost("/deployment/validate", (DeploymentNode root) =>
{
    bool isValid = DeploymentNode.ValidateTree(root);
    return Results.Ok(new { valid = isValid });
});

app.Run();