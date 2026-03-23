using Microsoft.EntityFrameworkCore;
using PhotoViewer.Api.Models;

namespace PhotoViewer.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DownloadRequest> DownloadRequests => Set<DownloadRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.HasIndex(e => e.Sha256Checksum);
            entity.HasIndex(e => e.RelativePath).IsUnique();
            entity.HasIndex(e => e.FileName);
            entity.HasIndex(e => e.MediaType);
            entity.HasIndex(e => e.TakenDate);
            entity.HasIndex(e => e.IsDeleted);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
        });

        // Seed admin user (admin/admin)
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            Role = UserRole.Admin,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
