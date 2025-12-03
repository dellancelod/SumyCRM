namespace SumyCRM.Services
{
    public class CategoryConverter
    {
        public static readonly Dictionary<string, Guid> MenuToCategory = new()
        {
            // ================= ТРАНСПОРТ ==================
            ["1.2"] = Guid.Parse("0722954c-c775-4b56-9f4c-dca6ba72aee5"),
            ["1.3"] = Guid.Parse("0722954c-c775-4b56-9f4c-dca6ba72aee5"),

            // =================== ВОДА ===================
            ["2.1"] = Guid.Parse("5ef8838e-3264-4277-9604-92b004d97224"),

        };
        public static readonly Dictionary<string, string> MenuToText = new()
        {
            // ================= ТРАНСПОРТ ===================
            ["1.2"] = "Скарга на графік руху",
            ["1.3"] = "Скарга на пільговий проїзд",

            // =================== ВОДА ===================
            ["2.1"] = "Відключення води"
        };

    }
}
