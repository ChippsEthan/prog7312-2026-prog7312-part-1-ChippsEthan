namespace SmartX.API.Models;

public enum PatternKind
{
    Alert,
    Search,
    Command
}

public readonly record struct PatternEvent(PatternKind Kind, string Mac, string Detail);

public record Suggestion(
    string Key,
    string Kind,
    string Title,
    string Reason,
    double Confidence,
    string Severity,
    string? Mac,
    string? Action);

public record PatternSummary(string From, string To, int Count, double Probability);

public record InsightsResponse(
    List<Suggestion> SuggestedActions,
    List<Suggestion> Insights,
    List<PatternSummary> TopPatterns,
    int EventsLearned,
    string? LastUserEvent);