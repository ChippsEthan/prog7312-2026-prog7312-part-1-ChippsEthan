using Microsoft.Extensions.Hosting;
using SmartX.API.Models;

namespace SmartX.API.Services;

public class TelemetryProcessor : BackgroundService
{
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(15);

    private readonly TelemetryQueues _queues;
    private readonly DeviceRegistry _registry;
    private readonly HistoryStore _history;
    private readonly AlertTracker _alerts;
    private readonly PredictionEngine _prediction;
    private readonly ILogger<TelemetryProcessor> _logger;

    public TelemetryProcessor(
        TelemetryQueues queues,
        DeviceRegistry registry,
        HistoryStore history,
        AlertTracker alerts,
        PredictionEngine prediction,
        ILogger<TelemetryProcessor> logger)
    {
        _queues = queues;
        _registry = registry;
        _history = history;
        _alerts = alerts;
        _prediction = prediction;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastWatchdog = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_queues.TryDequeue(out var packet, out var lane) && packet is not null)
                {
                    Process(packet, lane);

                    var delay = _queues.SimulatedDelayMs;
                    if (delay > 0) await Task.Delay(delay, stoppingToken);
                }
                else
                {
                    await Task.Delay(20, stoppingToken);
                }

                if (DateTime.UtcNow - lastWatchdog > TimeSpan.FromSeconds(1))
                {
                    CheckForDisconnects();
                    lastWatchdog = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing telemetry");
            }
        }
    }

    private void Process(QueuedPacket packet, string lane)
    {
        var before = _registry.GetByMac(packet.Mac);
        if (before is null) return;

        var evaluation = ThresholdPolicy.Evaluate(before, packet.Value);
        bool wasOffline = !before.IsOnline;

        var updated = _registry.Update(packet.Mac, d =>
        {
            d.LastValue = packet.Value;
            d.LastSeenUtc = DateTime.UtcNow;
            d.IsOnline = true;
            d.ActiveAlert = evaluation.Level == AlertLevel.None ? null : evaluation.Reason;
        });
        if (updated is null) return;

        _history.Add(new TelemetryReading(updated.MacAddress, packet.Timestamp, packet.Value, packet.SensorType));


        TelemetryStore.AddBatch(new[] { packet.Value }, updated.MacAddress, packet.SensorType);


        if (wasOffline)
            _alerts.Clear(packet.Mac, AlertTypes.Disconnected);

        if (evaluation.Level == AlertLevel.None)
        {
            _alerts.Clear(packet.Mac, AlertTypes.OutOfRange);
        }
        else
        {
            bool isNew = _alerts.Raise(packet.Mac, updated.Name, AlertTypes.OutOfRange, evaluation.Level, evaluation.Reason);

            if (isNew)
                _prediction.RecordEvent(new PatternEvent(PatternKind.Alert, updated.MacAddress, AlertTypes.OutOfRange));
        }

        _queues.RecordProcessed(packet, updated.Name, lane);
    }

    private void CheckForDisconnects()
    {
        var cutoff = DateTime.UtcNow - OfflineAfter;

        foreach (var device in _registry.GetAll())
        {
            if (device.IsOnline && device.LastSeenUtc is { } seen && seen < cutoff)
            {
                _registry.Update(device.MacAddress, d => d.IsOnline = false);

                bool isNew = _alerts.Raise(device.MacAddress, device.Name, AlertTypes.Disconnected, AlertLevel.Critical,
                    $"No telemetry for {OfflineAfter.TotalSeconds:0}s, node disconnected");

                if (isNew)
                    _prediction.RecordEvent(new PatternEvent(PatternKind.Alert, device.MacAddress, AlertTypes.Disconnected));
            }
        }
    }
}