using SmartX.API.Models;

namespace SmartX.API.Services;

public class PredictionEngine
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);     
    private static readonly TimeSpan ContextWindow = TimeSpan.FromMinutes(10); 
    private const int MinSupport = 2;      
    private const int MaxSuggestions = 5;
    private const int TrendWindow = 8;    
    private const int MinTrendPoints = 6;
    private const int TrendHorizon = 10;   

    private readonly DeviceRegistry _registry;
    private readonly AlertTracker _alerts;
    private readonly HistoryStore _history;

    private readonly Dictionary<PatternEvent, Dictionary<PatternEvent, int>> _transitions = new();
    private readonly object _lock = new();

    private PatternEvent? _previous;
    private DateTime _previousAt;
    private PatternEvent? _lastUser;
    private DateTime _lastUserAt;
    private int _eventsLearned;

    public PredictionEngine(DeviceRegistry registry, AlertTracker alerts, HistoryStore history)
    {
        _registry = registry;
        _alerts = alerts;
        _history = history;
    }


    public void RecordEvent(PatternEvent ev)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            if (_previous is { } same && same == ev && now - _previousAt < TimeSpan.FromSeconds(30))
                return;

            if (_previous is { } prev && prev != ev && now - _previousAt <= MaxGap)
                AddTransition(prev, ev, 1);

            _previous = ev;
            _previousAt = now;

            if (ev.Kind != PatternKind.Alert)
            {
                _lastUser = ev;
                _lastUserAt = now;
            }

            _eventsLearned++;
        }
    }

    public void RecordCommand(string mac, CommandAction action)
    {
        var target = mac.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : DeviceRegistry.NormalizeMac(mac);

        RecordEvent(new PatternEvent(PatternKind.Command, target, action.ToString()));
    }

    public string? RecordSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var q = query.Trim();

        var matches = _registry.GetAll()
            .Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || d.MacAddress.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || d.Location.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count != 1) return null;

        RecordEvent(new PatternEvent(PatternKind.Search, matches[0].MacAddress, ""));
        return matches[0].Name;
    }

    public void Learn(PatternEvent from, PatternEvent to, int times)
    {
        lock (_lock)
        {
            AddTransition(from, to, times);
            _eventsLearned += times;
        }
    }

    private void AddTransition(PatternEvent from, PatternEvent to, int times)
    {
        if (!_transitions.TryGetValue(from, out var next))
        {
            next = new Dictionary<PatternEvent, int>();
            _transitions[from] = next;
        }

        next.TryGetValue(to, out var current);
        next[to] = current + times;
    }
    public void SeedHistoricalPatterns()
    {
        const string moistA = "24:6F:28:A1:00:01";
        const string moistB = "24:6F:28:A1:00:02";
        const string temp = "24:6F:28:A1:00:03";
        const string meter1 = "24:6F:28:B2:00:01";
        const string meter2 = "24:6F:28:B2:00:02";
        const string pump = "24:6F:28:C3:00:01";
        const string relay = "24:6F:28:C3:00:02";

        PatternEvent Alert(string mac, string type = AlertTypes.OutOfRange) => new(PatternKind.Alert, mac, type);
        PatternEvent Search(string mac) => new(PatternKind.Search, mac, "");
        PatternEvent Cmd(string mac, CommandAction a) => new(PatternKind.Command, mac, a.ToString());

        Learn(Alert(moistB), Search(pump), 8);
        Learn(Alert(moistB), Search(moistB), 2);
        Learn(Alert(moistA), Search(pump), 6);
        Learn(Alert(moistA), Search(moistA), 2);
        Learn(Search(pump), Cmd(pump, CommandAction.TurnOn), 7);
        Learn(Search(pump), Cmd(pump, CommandAction.TurnOff), 1);
        Learn(Cmd(pump, CommandAction.TurnOn), Search(moistB), 4);
        Learn(Alert(meter1), Cmd(relay, CommandAction.TurnOff), 5);
        Learn(Alert(meter1), Search(meter2), 2);
        Learn(Alert(meter2), Cmd(relay, CommandAction.TurnOff), 4);
        Learn(Alert(temp, AlertTypes.Disconnected), Search(temp), 3);
        Learn(Alert(temp), Search(temp), 3);
    }


    public InsightsResponse GetInsights()
    {
        var devices = _registry.GetAll();
        var byMac = devices.ToDictionary(d => d.MacAddress);
        var activeAlerts = _alerts.GetActive();

        var contexts = new List<(PatternEvent Ctx, double Weight)>();
        foreach (var a in activeAlerts)
        {
            contexts.Add((new PatternEvent(PatternKind.Alert, a.Mac, a.Type),
                          a.Level == AlertLevel.Critical ? 1.0 : 0.85));
        }

        PatternEvent? lastUser = null;
        int learned;
        lock (_lock)
        {
            if (_lastUser is { } lu && DateTime.UtcNow - _lastUserAt <= ContextWindow)
                lastUser = lu;
            learned = _eventsLearned;
        }

        if (lastUser is { } user)
            contexts.Add((user, 0.8));

        var bag = new Dictionary<string, Suggestion>();

        foreach (var (ctx, weight) in contexts)
        {
            var (outgoing, total) = GetOutgoing(ctx);
            if (total == 0) continue;

            foreach (var (next, count) in outgoing)
            {
                if (count < MinSupport) continue;

                double p = (double)count / total;
                string why = $"After \"{Describe(ctx, byMac)}\" operators did this {count} of {total} times";

                if (next.Kind == PatternKind.Command)
                {
                    AddCommandSuggestion(bag, next, p * weight, why, byMac);
                }
                else if (next.Kind == PatternKind.Search)
                {
                    Upsert(bag, new Suggestion(
                        $"inv|{next.Mac}", "Investigate", $"Check {NameOf(next.Mac, byMac)}",
                        why, p * weight, "info", next.Mac, null));

                    var (outgoing2, total2) = GetOutgoing(next);
                    foreach (var (cmd, c2) in outgoing2)
                    {
                        if (cmd.Kind != PatternKind.Command || c2 < MinSupport) continue;

                        double p2 = (double)c2 / total2;
                        string chain = $"Typical sequence: {Describe(ctx, byMac)} \u2192 {Describe(next, byMac)} \u2192 {Describe(cmd, byMac)} ({c2} of {total2} times)";
                        AddCommandSuggestion(bag, cmd, p * p2 * weight, chain, byMac);
                    }
                }
            }
        }

        var ranking = new PriorityQueue<Suggestion, double>();
        foreach (var s in bag.Values)
            ranking.Enqueue(s, -s.Confidence);

        var suggested = new List<Suggestion>();
        while (suggested.Count < MaxSuggestions && ranking.TryDequeue(out var best, out _))
            suggested.Add(best);

       
        var insights = new List<Suggestion>();
        var criticalMacs = new HashSet<string>();

        foreach (var a in activeAlerts)
        {
            if (a.Level == AlertLevel.Critical) criticalMacs.Add(a.Mac);

            bool offline = a.Type == AlertTypes.Disconnected;
            int seconds = (int)Math.Round((DateTime.UtcNow - a.RaisedUtc).TotalSeconds);

            insights.Add(new Suggestion(
                $"insight|{a.Mac}|{a.Type}",
                offline ? "Offline" : "Alert",
                $"{a.DeviceName}: {a.Message}",
                $"Active for {seconds}s",
                1.0,
                a.Level == AlertLevel.Critical ? "critical" : "warning",
                a.Mac,
                null));
        }

        foreach (var d in devices.Where(d => d.Category != SensorCategory.Actuator && d.IsOnline))
        {
            if (criticalMacs.Contains(d.MacAddress)) continue;

            var trend = AnalyseTrend(d);
            if (trend is not null) insights.Add(trend);
        }

        insights = insights
            .OrderByDescending(i => SeverityRank(i.Severity))
            .ThenByDescending(i => i.Confidence)
            .Take(10)
            .ToList();

        return new InsightsResponse(
            suggested,
            insights,
            BuildPatternSummaries(byMac),
            learned,
            lastUser is { } lastEvent ? Describe(lastEvent, byMac) : null);
    }

    private Suggestion? AnalyseTrend(Sensor device)
    {
        var readings = _history.GetLatest(device.MacAddress, TrendWindow);
        int n = readings.Count;
        if (n < MinTrendPoints) return null;

        double meanX = (n - 1) / 2.0;
        double meanY = readings.Average(r => r.Value);
        double sxx = 0, sxy = 0, syy = 0;

        for (int i = 0; i < n; i++)
        {
            double dx = i - meanX;
            double dy = readings[i].Value - meanY;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        if (sxx == 0 || syy == 0) return null;

        double slope = sxy / sxx;                       
        double r = sxy / Math.Sqrt(sxx * syy);          
        if (Math.Abs(r) < 0.8) return null;             

        double last = readings[n - 1].Value;
        double projected = last + slope * TrendHorizon;

        var nowLevel = ThresholdPolicy.Evaluate(device, last).Level;
        var futureLevel = ThresholdPolicy.Evaluate(device, projected).Level;
        if (futureLevel != AlertLevel.Critical || nowLevel == AlertLevel.Critical) return null;

        double avgInterval = (readings[n - 1].Timestamp - readings[0].Timestamp).TotalSeconds / (n - 1);
        int eta = (int)Math.Round(avgInterval * TrendHorizon);
        string direction = slope < 0 ? "falling" : "rising";

        return new Suggestion(
            $"trend|{device.MacAddress}",
            "Trend",
            $"{device.Name} is {direction} towards a critical level",
            $"Projected {Math.Max(projected, 0):0.#}{device.Unit} in about {eta}s (trend strength {Math.Abs(r):0.00})",
            Math.Abs(r),
            "warning",
            device.MacAddress,
            null);
    }

    private (List<(PatternEvent Next, int Count)> Items, int Total) GetOutgoing(PatternEvent from)
    {
        lock (_lock)
        {
            if (!_transitions.TryGetValue(from, out var next))
                return (new List<(PatternEvent Next, int Count)>(), 0);

            return (next.Select(kv => (kv.Key, kv.Value)).ToList(), next.Values.Sum());
        }
    }

    private List<PatternSummary> BuildPatternSummaries(Dictionary<string, Sensor> byMac)
    {
        var all = new List<PatternSummary>();

        lock (_lock)
        {
            foreach (var (from, outgoing) in _transitions)
            {
                int total = outgoing.Values.Sum();
                foreach (var (to, count) in outgoing)
                    all.Add(new PatternSummary(Describe(from, byMac), Describe(to, byMac), count, (double)count / total));
            }
        }

        return all.OrderByDescending(p => p.Count).Take(10).ToList();
    }

    private static void AddCommandSuggestion(
        Dictionary<string, Suggestion> bag, PatternEvent cmd, double confidence, string reason,
        Dictionary<string, Sensor> byMac)
    {
        if (!Enum.TryParse<CommandAction>(cmd.Detail, out var action)) return;

        string target;
        if (cmd.Mac == "ALL")
        {
            target = "all actuators";
        }
        else
        {
            if (!byMac.TryGetValue(cmd.Mac, out var device)) return;

            if (action == CommandAction.TurnOn && device.IsOn) return;
            if (action == CommandAction.TurnOff && !device.IsOn) return;

            target = device.Name;
        }

        string verb = action switch
        {
            CommandAction.TurnOn => "Turn ON",
            CommandAction.TurnOff => "Turn OFF",
            _ => "Toggle"
        };

        Upsert(bag, new Suggestion(
            $"cmd|{cmd.Mac}|{action}", "Command", $"{verb} {target}",
            reason, confidence, "warning", cmd.Mac, action.ToString()));
    }

    private static void Upsert(Dictionary<string, Suggestion> bag, Suggestion s)
    {
        if (!bag.TryGetValue(s.Key, out var existing) || s.Confidence > existing.Confidence)
            bag[s.Key] = s;
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 2,
        "warning" => 1,
        _ => 0
    };

    private static string NameOf(string mac, Dictionary<string, Sensor> byMac) =>
        mac == "ALL" ? "all actuators" : byMac.TryGetValue(mac, out var d) ? d.Name : mac;

    private static string Describe(PatternEvent ev, Dictionary<string, Sensor> byMac)
    {
        string name = NameOf(ev.Mac, byMac);

        return ev.Kind switch
        {
            PatternKind.Alert => ev.Detail == AlertTypes.Disconnected ? $"{name} disconnected" : $"{name} out of range",
            PatternKind.Search => $"search for {name}",
            PatternKind.Command => ev.Detail switch
            {
                "TurnOn" => $"turn ON {name}",
                "TurnOff" => $"turn OFF {name}",
                _ => $"toggle {name}"
            },
            _ => ev.Mac
        };
    }
}