using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(256)]
        public string? Name { get; set; }
    }
}