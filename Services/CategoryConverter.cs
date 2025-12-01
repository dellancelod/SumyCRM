namespace SumyCRM.Services
{
    public class CategoryConverter
    {
        public static readonly Dictionary<string, Guid> MenuToCategory = new()
        {
            // =================== ВОДА ===================
            ["1.1"] = Guid.Parse("5ef8838e-3264-4277-9604-92b004d97224"),

        };
        public static readonly Dictionary<string, string> MenuToText = new()
        {
            // =================== ВОДА ===================
            ["1.1"] = "Відключення води"

        };

    }
}
