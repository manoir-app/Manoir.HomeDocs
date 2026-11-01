using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class HomeDocsDbContext(DbContextOptions<HomeDocsDbContext> options) : DbContext(options)
{
    internal DbSet<AdministratorEntity> Administrators => Set<AdministratorEntity>();
    internal DbSet<ContentPageEntity> ContentPages => Set<ContentPageEntity>();
    internal DbSet<ContentPageRedirectEntity> ContentPageRedirects => Set<ContentPageRedirectEntity>();

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
            entity.Property(administrator => administrator.PinHash);
            entity.Property(administrator => administrator.Language).HasMaxLength(2).IsRequired();
            entity.Property(administrator => administrator.Revision).IsConcurrencyToken();
        });

        modelBuilder.Entity<ContentPageEntity>(entity =>
        {
            entity.ToTable("ContentPages", table =>
            {
                table.HasCheckConstraint("CK_ContentPages_Revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_ContentPages_Title", "length(\"Title\") BETWEEN 1 AND 200");
                table.HasCheckConstraint("CK_ContentPages_Slug", "length(\"Slug\") BETWEEN 1 AND 80 AND \"Slug\" NOT GLOB '*[^a-z0-9-]*' AND \"Slug\" NOT LIKE '-%' AND \"Slug\" NOT LIKE '%-' AND \"Slug\" NOT LIKE '%--%'");
                table.HasCheckConstraint("CK_ContentPages_Category", "\"Category\" IN ('page','piece','emplacement','procedure','equipement','depannage','technique')");
                table.HasCheckConstraint("CK_ContentPages_Contexts", "json_valid(\"ContextsJson\") AND json_type(\"ContextsJson\") = 'array'");
            });
            entity.HasKey(page => page.Id);
            entity.Property(page => page.Id).ValueGeneratedNever();
            entity.Property(page => page.Title).HasMaxLength(200).IsRequired();
            entity.Property(page => page.Slug).HasMaxLength(80).IsRequired();
            entity.Property(page => page.Category).IsRequired();
            entity.Property(page => page.ContextsJson).IsRequired();
            entity.Property(page => page.FileId).HasMaxLength(32).IsRequired();
            entity.Property(page => page.Revision).IsConcurrencyToken();
            entity.HasIndex(page => page.Slug).IsUnique();
            entity.HasIndex(page => page.FileId).IsUnique();
            entity.HasIndex(page => new { page.IsPublished, page.Category });
        });

        modelBuilder.Entity<ContentPageRedirectEntity>(entity =>
        {
            entity.ToTable("ContentPageRedirects");
            entity.HasKey(redirect => redirect.Slug);
            entity.Property(redirect => redirect.Slug).HasMaxLength(80);
            entity.HasOne(redirect => redirect.Page).WithMany().HasForeignKey(redirect => redirect.PageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
