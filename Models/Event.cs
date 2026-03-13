using System;
using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Event : EntityBase
    {
        public Guid? RequestId { get; set; }
        public Request? Request { get; set; }

        public Guid? WaterLeakReportId { get; set; }
        public WaterLeakReport? WaterLeakReport { get; set; }

        [MaxLength(50)]
        public string SourceType { get; set; } = "";

        [MaxLength(256)]
        public string CategoryName { get; set; } = "";

        [MaxLength(512)]
        public string Address { get; set; } = "";

        [MaxLength(4000)]
        public string Text { get; set; } = "";

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool IsCompleted { get; set; }
    }
}