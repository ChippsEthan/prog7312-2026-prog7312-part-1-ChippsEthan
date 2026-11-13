using System.Globalization;
using SmartX.API.Models;

namespace SmartX.API.Services;

public record ImportResult(int Imported, int Skipped, string Message);

public static class TelemetryFileImporter
{
    public const int MaxRows = 500;

    public static bool IsImportable(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".csv" or ".txt";
    }

    public static ImportResult Import(string filePath, Sensor device, TelemetryIntake intake)
    {
        string sensorType = device.Category switch
        {
            SensorCategory.Power => "int",
            SensorCategory.Actuator => "bool",
            _ => "float"
        };

        int imported = 0, skipped = 0;
        bool firstLine = true;

        foreach (var raw in File.ReadLines(filePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (firstLine)
            {
                firstLine = false;
                if (char.IsLetter(line[0])) continue;      
            }

            if (imported >= MaxRows)
            {
                skipped++;
                continue;
            }

            DateTime? timestamp = null;
            string valueText = line;

            int comma = line.LastIndexOf(',');
            if (comma >= 0)
            {
                valueText = line[(comma + 1)..].Trim();
                var timeText = line[..comma].Trim();

                if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    skipped++;
                    continue;
                }

                timestamp = parsed;
            }

            if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                skipped++;
                continue;
            }

            if (device.Category == SensorCategory.Actuator)
                value = value > 0.5 ? 1 : 0;

            var result = intake.Submit(device.MacAddress, value, sensorType, timestamp);
            if (result.Accepted) imported++; else skipped++;
        }

        string message = imported == 0
            ? "No readings found. Use lines like 2026-11-12T08:00:00Z,24.5 (UTC) or just a number."
            : $"Imported {imported} reading(s) into the live data" +
              (skipped > 0 ? $" ({skipped} line(s) skipped)." : ".");

        return new ImportResult(imported, skipped, message);
    }
}