using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Models
{
    public class UserEditViewModel : EntityBase
    {

        [Required]
        [Display(Name = "Логін")]
        public string UserName { get; set; } = "";

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Новий пароль")]
        public string? Password { get; set; }

        [Required]
        [Display(Name = "Роль")]
        public string Role { get; set; } = "operator";

        [Display(Name = "Підприємства")]
        public List<Guid> SelectedFacilityIds { get; set; } = new();

        public List<SelectListItem> Facilities { get; set; } = new();
        public List<SelectListItem> Roles { get; set; } = new();
    }
}