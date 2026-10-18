namespace SmartX.API.Models;

public record QueuedPacket(string Mac, DateTime Timestamp, double Value, string SensorType, bool IsCritical);

public record ProcessedEntry(long Order, string DeviceName, string Mac, double Value, string Lane, DateTime ProcessedUtc);

public record QueueStats(
    int StandardDepth,
    int PriorityDepth,
    long TotalIngested,
    long ProcessedStandard,
    long ProcessedPriority,
    int SimulatedDelayMs,
    List<ProcessedEntry> RecentlyProcessed);