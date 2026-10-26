using Microsoft.EntityFrameworkCore;
using RunStats.Data;

namespace RunStats.Tests.Data;

// A model change committed without a migration would otherwise only fail at startup in the cluster.
public class MigrationsTests
{
    [Fact]
    public void Model_changes_all_have_a_migration()
    {
        // Compares the model with the last migration's snapshot; the database is never opened.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options);

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model has changes without a migration. Add one: dotnet ef migrations add <Name> --project src/RunStats");
    }
}
