using AppISO.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppISO.Context
{
    public class AppISOContext : DbContext
    {
        public AppISOContext(DbContextOptions<AppISOContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }

        public DbSet<Employee> Employees { get; set; }
    }
}
