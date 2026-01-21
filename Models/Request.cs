using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Request : EntityBase
    {
        public int RequestNumber { get; set; }
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public Guid FacilityId { get; set; }
        public Facility? Facility { get; set; }
        public string Caller { get; set; }
        public string Name { get; set; }
        public string Subcategory { get; set; }
        public string Address { get; set; }
        public string Text { get; set; }
        public string? NameAudioFilePath { get; set; }
        public string? AddressAudioFilePath { get; set; }
        public string? AudioFilePath { get; set; }
        public bool IsCompleted { get; set; }
    }
}
