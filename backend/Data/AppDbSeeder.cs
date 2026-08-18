using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public static class AppDbSeeder
{
    private const string SeedUsernameKey = "SeedUser:Username";
    private const string SeedPasswordKey = "SeedUser:Password";
    private const string ResetPasswordOnStartupKey = "SeedUser:ResetPasswordOnStartup";

    public static async Task SeedAsync(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var username = configuration[SeedUsernameKey];
        var password = configuration[SeedPasswordKey];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed user is not configured; skipping user seed.");
            return;
        }

        var normalizedUsername = NormalizeUsername(username);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(existingUser => existingUser.UsernameNormalized == normalizedUsername, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Username = username.Trim(),
                UsernameNormalized = normalizedUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            };

            dbContext.Users.Add(user);

            logger.LogInformation("Seed user {Username} was created.", username.Trim());
        }
        else if (configuration.GetValue<bool>(ResetPasswordOnStartupKey) && !IsPasswordValid(user.PasswordHash, password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            logger.LogInformation("Seed user {Username} password was reset from configuration.", username.Trim());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await EnsureUserStoreAsync(dbContext, user, logger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static string NormalizeUsername(string username)
    {
        return username.Trim().ToUpperInvariant();
    }

    private static bool IsPasswordValid(string passwordHash, string password)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static async Task EnsureUserStoreAsync(
        AppDbContext dbContext,
        User user,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var hasStore = await dbContext.StoreUsers
            .AnyAsync(storeUser => storeUser.UserId == user.Id, cancellationToken);

        if (hasStore)
        {
            return;
        }

        var store = await dbContext.Stores
            .OrderBy(existingStore => existingStore.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (store is null)
        {
            store = new Store
            {
                Name = "Loja Principal",
                CreatedAtUtc = DateTime.UtcNow,
            };

            dbContext.Stores.Add(store);
            logger.LogInformation("Default store was created.");
        }

        dbContext.StoreUsers.Add(new StoreUser
        {
            Store = store,
            User = user,
            Role = StoreRoles.Owner,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });

        logger.LogInformation("Seed user {Username} was linked to store {StoreName}.", user.Username, store.Name);
    }
}
