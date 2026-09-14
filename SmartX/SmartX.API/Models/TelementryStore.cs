namespace SmartX.API.Models;
public static class TelemetryStore
{
    public static double[][] RawBatches = new double[10][];
    private static int _batchIndex = 0;

    public static List<TelemetryPacket<double>> History = new();
    public static void AddBatch(double[] batch, string deviceId)
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
                SensorType = "float",
                Timestamp = DateTime.UtcNow
            });
        }}}
//}