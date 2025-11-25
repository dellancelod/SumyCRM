using Microsoft.EntityFrameworkCore;
using SumyCRM.Models;
using System.Collections.Generic;


namespace SumyCRM.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Request> Requests { get; set; }
    }
}
