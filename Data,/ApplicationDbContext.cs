using Microsoft.EntityFrameworkCore;
using IpTracker.Models;

namespace IpTracker.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        
        public DbSet<TrackingLink> TrackingLinks { get; set; }
        public DbSet<LinkVisit> LinkVisits { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<TrackingLink>()
                .HasKey(t => t.Id);
                
            modelBuilder.Entity<LinkVisit>()
                .HasKey(v => v.Id);
                
            modelBuilder.Entity<LinkVisit>()
                .HasOne(v => v.Link)
                .WithMany(l => l.Visits)
                .HasForeignKey(v => v.LinkId);
        }
    }
}