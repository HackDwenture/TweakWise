namespace TweakWise.Models
{
    public sealed class TemperatureSensorReading
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public float ValueCelsius { get; set; }
        public string HardwareName { get; set; } = string.Empty;
        public string SensorName { get; set; } = string.Empty;
    }
}
