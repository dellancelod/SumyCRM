using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Request
    {
        public int Id { get; set; }
        public string Caller { get; set; }
        public string Text { get; set; }
        public string Address { get; set; }
        public string AudioFilePath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
