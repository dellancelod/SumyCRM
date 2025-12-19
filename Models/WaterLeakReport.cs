namespace SumyCRM.Models
{
    public class WaterLeakReport : EntityBase
    {
        public string Address { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string? Notes { get; set; }
        public string Status { get; set; } = "New"; // New/InProgress/Done
        public bool Street { get; set; } // "point" or "street"

        // for street we store polyline coordinates (lat/lon pairs) as JSON
        public string? GeometryJson { get; set; }
    }
}
