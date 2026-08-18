using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public sealed class DatabaseInitializer(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("Applying database migrations.");
        await dbContext.Database.MigrateAsync(cancellationToken);
        await AppDbSeeder.SeedAsync(dbContext, configuration, logger, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
