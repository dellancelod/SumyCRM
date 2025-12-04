using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Facility : EntityBase
    {
        [Required]
        [Display(Name = "Назва організації")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Опис")]
        public string? Description { get; set; }

        [Required]
        [Display(Name = "Адреса")]
        public string Address { get; set; } = string.Empty;

        [Display(Name = "Телефони")]
        public string Phones { get; set; } = string.Empty;
    }
}
