using Microsoft.AspNetCore.Identity;

namespace SumyCRM.Models
{
    public class UserFacility
    {
        public string UserId { get; set; } = default!;
        public Guid FacilityId { get; set; }

        public IdentityUser User { get; set; } = default!;
        public Facility Facility { get; set; } = default!;
    }
}
