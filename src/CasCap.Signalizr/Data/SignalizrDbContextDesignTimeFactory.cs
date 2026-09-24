using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CasCap.Data;

/// <summary>Creates <see cref="SignalizrDbContext"/> for EF Core design-time tooling.</summary>
public sealed class SignalizrDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SignalizrDbContext>
{
    /// <inheritdoc/>
    public SignalizrDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SignalizrDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=signalizr_model_validation;Username=signalizr_model_validation")
            .Options;

        return new SignalizrDbContext(options);
    }
}
