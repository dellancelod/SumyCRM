namespace SumyCRM.Models
{
    public class Street : EntityBase
    {

        // Display name for UI
        public string Name { get; set; } = "";

        // For search (lowercase, trimmed, normalized)
        public string NameNorm { get; set; } = "";

    }
}
