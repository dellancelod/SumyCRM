using System;
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

        public string Caller { get; set; } = "";
        public string Name { get; set; } = "";
        public string Subcategory { get; set; } = "";
        public string Address { get; set; } = "";
        public string Text { get; set; } = "";

        public string? NameAudioFilePath { get; set; }
        public string? AddressAudioFilePath { get; set; }
        public string? AudioFilePath { get; set; }

        public bool IsCompleted { get; set; }

        // ===================== FIELDS (for Print form) =====================

        /// <summary>Кому передано для виконання</summary>
        [MaxLength(2000)]
        public string? ForwardedTo { get; set; }

        /// <summary>Інформація про хід виконання і завершення роботи</summary>
        [MaxLength(4000)]
        public string? ExecutionProgressInfo { get; set; }

        /// <summary>Замовнику роз'яснено, повідомлено (дата)</summary>
        public DateTime? CustomerInformedOn { get; set; }

        /// <summary>Його відгук</summary>
        [MaxLength(4000)]
        public string? CustomerFeedback { get; set; }

        /// <summary>Дата виконання</summary>
        public DateTime? CompletedOn { get; set; }
    }
}
