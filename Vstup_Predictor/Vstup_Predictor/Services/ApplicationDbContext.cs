using Microsoft.EntityFrameworkCore;
using Vstup_Predictor.Models;

namespace Vstup_Predictor.Services
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<City> Cities { get; set; }
        public DbSet<University> Universities { get; set; }
        public DbSet<Offer> Offers { get; set; }
        public DbSet<Application> Applications { get; set; }
        public DbSet<Person> Persons { get; set; }

        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }

    }
}
