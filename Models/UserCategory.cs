using Microsoft.AspNetCore.Identity;

namespace SumyCRM.Models
{
    public class UserCategory
    {
        public string UserId { get; set; } = default!;
        public Guid CategoryId { get; set; }

        public IdentityUser User { get; set; } = default!;
        public Category Category { get; set; } = default!;
    }
}
