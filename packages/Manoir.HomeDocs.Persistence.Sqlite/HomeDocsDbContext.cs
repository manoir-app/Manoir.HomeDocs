using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class HomeDocsDbContext(DbContextOptions<HomeDocsDbContext> options) : DbContext(options)
{
    internal DbSet<AdministratorEntity> Administrators => Set<AdministratorEntity>();
    internal DbSet<ContentPageEntity> ContentPages => Set<ContentPageEntity>();
    internal DbSet<ContentPageRedirectEntity> ContentPageRedirects => Set<ContentPageRedirectEntity>();
    internal DbSet<PageTreeNodeEntity> PageTreeNodes => Set<PageTreeNodeEntity>();
    internal DbSet<UsageContextEntity> UsageContexts => Set<UsageContextEntity>();

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

        modelBuilder.Entity<PageTreeNodeEntity>(entity =>
        {
            entity.ToTable("PageTreeNodes", table =>
                table.HasCheckConstraint("CK_PageTreeNodes_Position", "\"Position\" >= 0"));
            entity.HasKey(node => node.Id);
            entity.Property(node => node.Id).ValueGeneratedNever();
            entity.Property(node => node.PageId).IsRequired();
            entity.HasOne(node => node.Page).WithMany().HasForeignKey(node => node.PageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(node => node.ParentNode).WithMany(node => node.Children)
                .HasForeignKey(node => node.ParentNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(node => new { node.ParentNodeId, node.Position });
            entity.HasIndex(node => new { node.PageId, node.IsLink });
            entity.HasIndex(node => node.PageId).IsUnique().HasFilter("\"IsLink\" = 0");
        });

        modelBuilder.Entity<UsageContextEntity>(entity =>
        {
            entity.ToTable("UsageContexts", table =>
            {
                table.HasCheckConstraint("CK_UsageContexts_Id", "length(\"Id\") BETWEEN 1 AND 64 AND \"Id\" NOT GLOB '*[^a-z0-9-]*' AND \"Id\" NOT LIKE '-%' AND \"Id\" NOT LIKE '%-' AND \"Id\" NOT LIKE '%--%'");
                table.HasCheckConstraint("CK_UsageContexts_Name", "length(\"Name\") BETWEEN 1 AND 128");
            });
            entity.HasKey(context => context.Id);
            entity.Property(context => context.Id).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(context => context.Name).HasMaxLength(128).IsRequired();
            entity.HasOne(context => context.RootNode).WithMany()
                .HasForeignKey(context => context.RootNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(context => context.RootNodeId);
        });
    }
}
