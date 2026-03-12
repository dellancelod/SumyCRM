using System;
using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Event : EntityBase
    {
        public Guid RequestId { get; set; }
        public Request? Request { get; set; }

        public int RequestNumber { get; set; }

        [MaxLength(256)]
        public string CategoryName { get; set; } = "";

        [MaxLength(256)]
        public string StreetName { get; set; } = "";

        [MaxLength(512)]
        public string Address { get; set; } = "";

        [MaxLength(4000)]
        public string Text { get; set; } = "";

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool IsCompleted { get; set; }

    }
}
