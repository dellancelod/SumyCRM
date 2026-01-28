using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class CallEvent : EntityBase
    {

        [MaxLength(64)]
        public string? CallId { get; set; }

        [MaxLength(32)]
        public string? Caller { get; set; }

        [Required, MaxLength(128)]
        public string Event { get; set; } = "";

        [MaxLength(1024)]
        public string? Data { get; set; }

    }
}
