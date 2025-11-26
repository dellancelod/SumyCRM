using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using SumyCRM.Models;
using System.Collections.Generic;


namespace SumyCRM.Data
{
    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Request> Requests { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ================= Адміністратори ====================
            builder.Entity<IdentityRole>().HasData(new IdentityRole
            {
                Id = "b162d3e4-deb7-4f9b-8f31-b393f5c15a60",
                Name = "admin",
                NormalizedName = "ADMIN"
            });
            builder.Entity<IdentityUser>().HasData(new IdentityUser
            {
                Id = "94c19b57-7312-4a4d-9a14-98f5a123269a",
                UserName = "admin1",
                NormalizedUserName = "ADMIN1",
                Email = "my@email.com",
                NormalizedEmail = "MY@EMAIL.COM",
                EmailConfirmed = true,
                PasswordHash = new PasswordHasher<IdentityUser>().HashPassword(null, "crm2025"),
                SecurityStamp = string.Empty
            });
            builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>
            {
                RoleId = "b162d3e4-deb7-4f9b-8f31-b393f5c15a60",
                UserId = "94c19b57-7312-4a4d-9a14-98f5a123269a"
            });
            // ========================================================
        }
    }
}
