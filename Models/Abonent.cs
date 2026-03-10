using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    [Index(nameof(Phone), IsUnique = true)]
    public class Abonent : EntityBase
    {
        [Required]
        [Display(Name = "Телефон")]
        [MaxLength(20)]
        public string Phone { get; set; }

        [Display(Name = "ПІБ")]
        [MaxLength(200)]
        public string? Name { get; set; }

        [Display(Name = "Повна адреса")]
        [MaxLength(300)]
        public string? FullAddress { get; set; }

        [Display(Name = "Коментар")]
        [MaxLength(500)]
        public string? Comment { get; set; }

        public ICollection<Request>? Requests { get; set; }
    }
}