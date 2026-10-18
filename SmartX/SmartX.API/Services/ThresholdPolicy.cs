using SmartX.API.Models;

namespace SmartX.API.Services;

public record AlertEvaluation(AlertLevel Level, string Reason);

public static class ThresholdPolicy
{
    private static readonly AlertEvaluation Ok = new(AlertLevel.None, "");

    private static AlertEvaluation Warn(string reason) => new(AlertLevel.Warning, reason);
    private static AlertEvaluation Crit(string reason) => new(AlertLevel.Critical, reason);

    public static AlertEvaluation Evaluate(Sensor device, double value)
    {
        return device.Metric switch
        {
            "Temperature" => value switch
            {
                < 15.0 => Crit($"Temperature critically low ({value:0.#}\u00B0C)"),
                > 40.0 => Crit($"Temperature critically high ({value:0.#}\u00B0C)"),
                < 22.0 => Warn($"Temperature low ({value:0.#}\u00B0C)"),
                > 34.0 => Warn($"Temperature high ({value:0.#}\u00B0C)"),
                _ => Ok
            },

            "Moisture" => value switch
            {
                < 20.0 => Crit($"Moisture critically low ({value:0.#}%)"),
                < 30.0 => Warn($"Moisture low ({value:0.#}%)"),
                > 90.0 => Warn($"Moisture high ({value:0.#}%)"),
                _ => Ok
            },

            "Power" => value switch
            {
                > 2500.0 => Crit($"Power spike ({value:0}W)"),
                > 1800.0 => Warn($"Power high ({value:0}W)"),
                < 300.0 => Warn($"Power low ({value:0}W)"),
                _ => Ok
            },

            _ => Ok   
        };
    }
}