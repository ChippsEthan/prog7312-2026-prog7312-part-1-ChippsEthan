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
}