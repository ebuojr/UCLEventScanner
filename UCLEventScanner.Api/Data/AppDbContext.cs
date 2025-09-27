using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Shared.Models;

namespace UCLEventScanner.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Event> Events { get; set; }
    public DbSet<Student> Students { get; set; }
    public DbSet<Registration> Registrations { get; set; }
    public DbSet<Scanner> Scanners { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Registration>()
            .HasOne(r => r.Event)
            .WithMany(e => e.Registrations)
            .HasForeignKey(r => r.EventId);

        modelBuilder.Entity<Registration>()
            .HasOne(r => r.Student)
            .WithMany(s => s.Registrations)
            .HasForeignKey(r => r.StudentId);

        modelBuilder.Entity<Registration>()
            .HasIndex(r => new { r.EventId, r.StudentId })
            .IsUnique();

        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>().HasData(
            new Event { Id = 1, Name = "UCL Tech Conference 2024", Date = DateTime.Now.AddDays(7) }
        );

        modelBuilder.Entity<Student>().HasData(
            new Student { Id = "STU001", Name = "Alice Johnson", Email = "alice.johnson@ucl.ac.uk" },
            new Student { Id = "STU002", Name = "Bob Smith", Email = "bob.smith@ucl.ac.uk" },
            new Student { Id = "STU003", Name = "Charlie Brown", Email = "charlie.brown@ucl.ac.uk" },
            new Student { Id = "STU004", Name = "Diana Prince", Email = "diana.prince@ucl.ac.uk" },
            new Student { Id = "STU005", Name = "Eve Wilson", Email = "eve.wilson@ucl.ac.uk" }
        );

        modelBuilder.Entity<Registration>().HasData(
            new Registration { Id = 1, EventId = 1, StudentId = "STU001", RegisteredAt = DateTime.Now.AddDays(-2) },
            new Registration { Id = 2, EventId = 1, StudentId = "STU003", RegisteredAt = DateTime.Now.AddDays(-1) },
            new Registration { Id = 3, EventId = 1, StudentId = "STU005", RegisteredAt = DateTime.Now.AddHours(-12) }
        );

        modelBuilder.Entity<Scanner>().HasData(
            new Scanner { Id = 1, Name = "Line1", IsActive = true },
            new Scanner { Id = 2, Name = "Line2", IsActive = true },
            new Scanner { Id = 3, Name = "Line3", IsActive = true }
        );
    }
}