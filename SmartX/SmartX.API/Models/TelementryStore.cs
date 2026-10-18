namespace SmartX.API.Models;

public static class TelemetryStore
{
    private const int MaxHistory = 5000;
    private static readonly object Sync = new();
   
    public static double[][] RawBatches = new double[10][];
    private static int _batchIndex = 0;

    public static List<TelemetryPacket<double>> History = new();

    public static void AddBatch(double[] batch, string deviceId, string sensorType = "float")
    {
        lock (Sync)
        {
            if (_batchIndex >= RawBatches.Length)
                _batchIndex = 0;

            RawBatches[_batchIndex] = batch;
            _batchIndex++;

            foreach (var val in batch)
            {
                History.Add(new TelemetryPacket<double>
                {
                    DeviceId = deviceId,
                    Value = val,
                    SensorType = sensorType,
                    Timestamp = DateTime.UtcNow
                });
            }

            if (History.Count > MaxHistory)
                History.RemoveRange(0, History.Count - MaxHistory);
        }
    }

    public static List<TelemetryPacket<double>> Snapshot(int count = 200)
    {
        lock (Sync)
        {
            return History.TakeLast(count).ToList();
        }
    }
}