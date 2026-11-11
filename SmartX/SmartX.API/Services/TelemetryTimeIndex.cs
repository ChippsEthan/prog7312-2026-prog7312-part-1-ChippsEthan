using SmartX.API.Models;

namespace SmartX.API.Services;

public record TimeRangeResult(
    string Mac,
    DateTime StartUtc,
    DateTime EndUtc,
    int Count,
    int NodesVisited,
    int TreeSize,
    int TreeHeight,
    List<TelemetryReading> Readings);

public record TimeIndexStats(string Mac, int Size, int Height, int PerfectHeight, int Rotations);

public class TelemetryTimeIndex
{
    private const int MaxPerDevice = 5000;

    private readonly Dictionary<string, AvlTree<DateTime, TelemetryReading>> _trees = new();
    private readonly object _lock = new();

    public void Add(TelemetryReading reading)
    {
        var mac = DeviceRegistry.NormalizeMac(reading.DeviceId);

        lock (_lock)
        {
            if (!_trees.TryGetValue(mac, out var tree))
            {
                tree = new AvlTree<DateTime, TelemetryReading>();
                _trees[mac] = tree;
            }

            var ts = reading.Timestamp;
            while (tree.ContainsKey(ts)) ts = ts.AddTicks(1);   

            tree.Insert(ts, reading with { DeviceId = mac, Timestamp = ts });

            while (tree.Count > MaxPerDevice)
                tree.RemoveMin();                                
        }
    }

    public TimeRangeResult Query(string mac, DateTime startUtc, DateTime endUtc)
    {
        var key = DeviceRegistry.NormalizeMac(mac);

        lock (_lock)
        {
            if (!_trees.TryGetValue(key, out var tree))
                return new TimeRangeResult(key, startUtc, endUtc, 0, 0, 0, 0, new List<TelemetryReading>());

            var readings = tree.Range(startUtc, endUtc);
            return new TimeRangeResult(key, startUtc, endUtc, readings.Count,
                tree.LastQueryNodesVisited, tree.Count, tree.Height, readings);
        }
    }

    public TimeIndexStats GetStats(string mac)
    {
        var key = DeviceRegistry.NormalizeMac(mac);

        lock (_lock)
        {
            if (!_trees.TryGetValue(key, out var tree))
                return new TimeIndexStats(key, 0, 0, 0, 0);

            int perfect = tree.Count == 0 ? 0 : (int)Math.Ceiling(Math.Log2(tree.Count + 1));
            return new TimeIndexStats(key, tree.Count, tree.Height, perfect, tree.Rotations);
        }
    }
}