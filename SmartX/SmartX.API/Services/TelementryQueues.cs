using SmartX.API.Models;

namespace SmartX.API.Services;

public class TelemetryQueues
{
    private readonly Queue<QueuedPacket> _standard = new();

   
    private readonly PriorityQueue<QueuedPacket, (int Priority, long Sequence)> _priority = new();

    private readonly Queue<ProcessedEntry> _recent = new();
    private readonly object _lock = new();

    private long _sequence, _totalIngested, _processedStandard, _processedPriority, _order;
    private int _delayMs;

    
    public int SimulatedDelayMs
    {
        get => Volatile.Read(ref _delayMs);
        set => Volatile.Write(ref _delayMs, value);
    }

    public void Enqueue(QueuedPacket packet)
    {
        lock (_lock)
        {
            _totalIngested++;

            if (packet.IsCritical)
                _priority.Enqueue(packet, (0, _sequence++));
            else
                _standard.Enqueue(packet);
        }
    }

    
    public bool TryDequeue(out QueuedPacket? packet, out string lane)
    {
        lock (_lock)
        {
            if (_priority.TryDequeue(out var urgent, out _))
            {
                packet = urgent;
                lane = "PRIORITY";
                return true;
            }

            if (_standard.TryDequeue(out var normal))
            {
                packet = normal;
                lane = "STANDARD";
                return true;
            }

            packet = null;
            lane = "";
            return false;
        }
    }

    public void RecordProcessed(QueuedPacket packet, string deviceName, string lane)
    {
        lock (_lock)
        {
            if (lane == "PRIORITY") _processedPriority++; else _processedStandard++;

            _recent.Enqueue(new ProcessedEntry(++_order, deviceName, packet.Mac, packet.Value, lane, DateTime.UtcNow));
            while (_recent.Count > 30) _recent.Dequeue();
        }
    }

    public QueueStats GetStats()
    {
        lock (_lock)
        {
            return new QueueStats(
                _standard.Count,
                _priority.Count,
                _totalIngested,
                _processedStandard,
                _processedPriority,
                SimulatedDelayMs,
                _recent.Reverse().ToList());   
        }
    }
}