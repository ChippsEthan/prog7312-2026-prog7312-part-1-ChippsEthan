namespace SmartX.WPF.Models;

public record ApiResult(bool Ok, string Message);

public class SensorDto
{
    public string Id { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> AttachedFiles { get; set; } = new();
    public string Name { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Unit { get; set; } = "";
    public double? LastValue { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public bool IsOnline { get; set; }
    public bool IsOn { get; set; }
    public string? ActiveAlert { get; set; }
}

public class AlertDto
{
    public string Mac { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedUtc { get; set; }
}

public class CommandChangeDto
{
    public string Mac { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool PreviousState { get; set; }
    public bool NewState { get; set; }
}

public class CommandDto
{
    public long Id { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<CommandChangeDto> Changes { get; set; } = new();
    public DateTime IssuedUtc { get; set; }
    public bool IsUndo { get; set; }
}

public class CommandHistoryDto
{
    public int UndoDepth { get; set; }
    public string? NextUndo { get; set; }
    public List<CommandDto> History { get; set; } = new();
}

public class SuggestionDto
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Confidence { get; set; }
    public string Severity { get; set; } = "";
    public string? Mac { get; set; }
    public string? Action { get; set; }
}

public class PatternSummaryDto
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int Count { get; set; }
    public double Probability { get; set; }
}

public class InsightsDto
{
    public List<SuggestionDto> SuggestedActions { get; set; } = new();
    public List<SuggestionDto> Insights { get; set; } = new();
    public List<PatternSummaryDto> TopPatterns { get; set; } = new();
    public int EventsLearned { get; set; }
    public string? LastUserEvent { get; set; }
}

public class ProcessedEntryDto
{
    public long Order { get; set; }
    public string DeviceName { get; set; } = "";
    public string Mac { get; set; } = "";
    public double Value { get; set; }
    public string Lane { get; set; } = "";
    public DateTime ProcessedUtc { get; set; }
}

public class QueueStatsDto
{
    public int StandardDepth { get; set; }
    public int PriorityDepth { get; set; }
    public long TotalIngested { get; set; }
    public long ProcessedStandard { get; set; }
    public long ProcessedPriority { get; set; }
    public int SimulatedDelayMs { get; set; }
    public List<ProcessedEntryDto> RecentlyProcessed { get; set; } = new();
}

public class ReadingDto
{
    public string DeviceId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    public string SensorType { get; set; } = "";
}