using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Schedule : EntityBase
    {
        [Required(ErrorMessage = "Заповніть номер")]
        [Display(Name = "Номер")]
        public string Number { get; set; }
        public string? AudioFileName { get; set; }
        [Required(ErrorMessage = "Заповніть час")]
        [Display(Name = "Час")]
        public string Time { get; set; }
    }
}
