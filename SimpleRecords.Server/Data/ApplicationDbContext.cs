
using Microsoft.EntityFrameworkCore;
using SimpleRecords.Server.Models;

namespace SimpleRecords.Server.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Position> Positions => Set<Position>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        modelBuilder.Entity<Employee>()
            .HasOne(e => e.Position)
            .WithMany()
            .HasForeignKey(e => e.PositionID)
            .IsRequired(false);


        modelBuilder.Entity<Employee>()
            .HasIndex(e => new
            {
                e.Name,
                e.Surname,
                e.BirthDate
            })
            .IsUnique();

        modelBuilder.Entity<Position>()
            .HasIndex(p => p.PositionName)
            .IsUnique();
    }
}
