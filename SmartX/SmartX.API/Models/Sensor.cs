namespace SmartX.API.Models;

public enum SensorCategory
{
    Environmental,
    Power,
    Actuator
}

public class Sensor
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MacAddress { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public List<string> AttachedFiles { get; set; } = new();

    
    public string Name { get; set; } = string.Empty;       
    public string Metric { get; set; } = string.Empty;     
    public string Unit { get; set; } = string.Empty;      
    public double? LastValue { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public bool IsOnline { get; set; }
    public bool IsOn { get; set; }                         
    public string? ActiveAlert { get; set; }

    
    public Sensor Snapshot()
    {
        var copy = (Sensor)MemberwiseClone();
        copy.AttachedFiles = new List<string>(AttachedFiles);
        return copy;
    }
}