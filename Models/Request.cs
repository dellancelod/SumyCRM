using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Request : EntityBase
    {
        public int RequestNumber { get; set; }
        public Guid CategoryId { get; set; }
        public Category Category { get; set; }
        public string Caller { get; set; }
        public string Subcategory { get; set; }
        public string Text { get; set; }
        public string AudioFilePath { get; set; }
        public bool IsCompleted { get; set; }
    }
}
