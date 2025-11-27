using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class Category : EntityBase
    {
        [Required(ErrorMessage = "Заповніть назву категорії")]
        [Display(Name = "Назва категорії")]
        public string Title { get; set; } = "Категорія";
    }
}
