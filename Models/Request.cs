using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Request
    {
        public Request() => CreatedAt = DateTime.UtcNow;
        [Required]
        public Guid Id { get; set; }
        public int RequestNumber { get; set; }
        public string Caller { get; set; }
        public string Text { get; set; }
        public string Address { get; set; }
        public string AudioFilePath { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
