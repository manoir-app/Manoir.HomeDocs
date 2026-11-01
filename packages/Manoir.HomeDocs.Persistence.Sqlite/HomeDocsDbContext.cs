using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class HomeDocsDbContext(DbContextOptions<HomeDocsDbContext> options) : DbContext(options)
{
    internal DbSet<AdministratorEntity> Administrators => Set<AdministratorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdministratorEntity>(entity =>
        {
            entity.ToTable("Administrators", table => table.HasCheckConstraint("CK_Administrators_Singleton", "\"Id\" = 1"));
            entity.HasKey(administrator => administrator.Id);
            entity.Property(administrator => administrator.Id).ValueGeneratedNever();
            entity.Property(administrator => administrator.UserName).HasMaxLength(128).IsRequired();
            entity.Property(administrator => administrator.NormalizedUserName).HasMaxLength(128).IsRequired();
            entity.Property(administrator => administrator.PasswordHash).IsRequired();
        });
    }
}
