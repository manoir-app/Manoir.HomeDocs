using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class HomeDocsDbContext(DbContextOptions<HomeDocsDbContext> options) : DbContext(options)
{
    internal DbSet<AdministratorEntity> Administrators => Set<AdministratorEntity>();
    internal DbSet<ContentPageEntity> ContentPages => Set<ContentPageEntity>();
    internal DbSet<ContentPageRedirectEntity> ContentPageRedirects => Set<ContentPageRedirectEntity>();
    internal DbSet<PageTreeNodeEntity> PageTreeNodes => Set<PageTreeNodeEntity>();
    internal DbSet<UsageContextEntity> UsageContexts => Set<UsageContextEntity>();
    internal DbSet<MediaItemEntity> MediaItems => Set<MediaItemEntity>();
    internal DbSet<ContentPageVersionEntity> ContentPageVersions => Set<ContentPageVersionEntity>();
    internal DbSet<ThemeEntity> Themes => Set<ThemeEntity>();
    internal DbSet<ProfileSettingsEntity> ProfileSettings => Set<ProfileSettingsEntity>();

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

        modelBuilder.Entity<ContentPageVersionEntity>(entity =>
        {
            entity.ToTable("ContentPageVersions", table =>
            {
                table.HasCheckConstraint("CK_ContentPageVersions_Revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_ContentPageVersions_Event", "\"Event\" IN ('created','edited','published','unpublished','restored','baseline')");
            });
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Event).IsRequired();
            entity.Property(version => version.Title).HasMaxLength(200).IsRequired();
            entity.Property(version => version.Slug).HasMaxLength(80).IsRequired();
            entity.Property(version => version.Category).IsRequired();
            entity.Property(version => version.ContextsJson).IsRequired();
            entity.Property(version => version.Markdown).IsRequired();
            entity.HasOne(version => version.Page).WithMany().HasForeignKey(version => version.PageId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(version => new { version.PageId, version.Revision }).IsUnique();
        });

        modelBuilder.Entity<MediaItemEntity>(entity =>
        {
            entity.ToTable("MediaItems", table =>
            {
                table.HasCheckConstraint("CK_MediaItems_Kind", "\"Kind\" IN ('image','pdf')");
                table.HasCheckConstraint("CK_MediaItems_Size", "\"Size\" > 0");
                table.HasCheckConstraint("CK_MediaItems_Revision", "\"Revision\" > 0");
            });
            entity.HasKey(media => media.Id);
            entity.Property(media => media.Id).ValueGeneratedNever();
            entity.Property(media => media.Name).HasMaxLength(128).IsRequired();
            entity.Property(media => media.Kind).IsRequired();
            entity.Property(media => media.ContentType).IsRequired();
            entity.Property(media => media.Title).HasMaxLength(200).IsRequired();
            entity.Property(media => media.AltText).HasMaxLength(300).IsRequired();
            entity.Property(media => media.Folder).HasMaxLength(64).IsRequired();
            entity.Property(media => media.Revision).IsConcurrencyToken();
            entity.HasIndex(media => media.Folder);
        });

        modelBuilder.Entity<ThemeEntity>(entity =>
        {
            entity.ToTable("Themes", table =>
            {
                table.HasCheckConstraint("CK_Themes_ColorScheme", "\"ColorScheme\" IN ('light','dark')");
                table.HasCheckConstraint("CK_Themes_Revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_Themes_Data", "json_valid(\"DataJson\")");
            });
            entity.HasKey(theme => theme.Id);
            entity.Property(theme => theme.Id).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(theme => theme.Name).HasMaxLength(64).IsRequired();
            entity.Property(theme => theme.ColorScheme).IsRequired();
            entity.Property(theme => theme.DataJson).IsRequired();
            entity.Property(theme => theme.Revision).IsConcurrencyToken();
        });

        modelBuilder.Entity<ProfileSettingsEntity>(entity =>
        {
            entity.ToTable("ProfileSettings", table =>
            {
                table.HasCheckConstraint("CK_ProfileSettings_Revision", "\"Revision\" > 0");
                table.HasCheckConstraint("CK_ProfileSettings_Data", "json_valid(\"DataJson\")");
            });
            entity.HasKey(settings => settings.ProfileId);
            entity.Property(settings => settings.ProfileId).HasMaxLength(32).ValueGeneratedNever();
            entity.Property(settings => settings.DataJson).IsRequired();
            entity.Property(settings => settings.Revision).IsConcurrencyToken();
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
