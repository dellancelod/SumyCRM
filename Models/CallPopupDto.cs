namespace SumyCRM.Models
{
    public class CallPopupDto
    {
        public string Phone { get; set; } = "";
        public string Name { get; set; } = "";
        public Guid? AbonentId { get; set; }
        public string EventName { get; set; } = "";
        public string? Data { get; set; }
    }
}