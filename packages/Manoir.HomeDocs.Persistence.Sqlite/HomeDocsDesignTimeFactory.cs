using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Manoir.HomeDocs.Persistence.Sqlite;

public sealed class HomeDocsDesignTimeFactory : IDesignTimeDbContextFactory<HomeDocsDbContext>
{
    public HomeDocsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HomeDocsDbContext>()
            .UseSqlite("Data Source=homedocs.design.db")
            .Options;
        return new HomeDocsDbContext(options);
    }
}
