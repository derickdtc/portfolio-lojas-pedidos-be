using backend.Configuration;
using backend.Data;
using backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(LoadDotEnv(builder.Environment.ContentRootPath));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<ProductSpreadsheetImporter>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.Configure<R2StorageOptions>(options =>
{
    options.AccountId = GetConfigurationValue(builder.Configuration, "R2:AccountId", "R2_ACCOUNT_ID");
    options.AccessKeyId = GetConfigurationValue(builder.Configuration, "R2:AccessKeyId", "R2_ACCESS_KEY_ID");
    options.SecretAccessKey = GetConfigurationValue(builder.Configuration, "R2:SecretAccessKey", "R2_SECRET_ACCESS_KEY");
    options.BucketName = GetConfigurationValue(builder.Configuration, "R2:BucketName", "R2_BUCKET_NAME");
    options.PublicUrl = GetConfigurationValue(builder.Configuration, "R2:PublicUrl", "R2_PUBLIC_URL");
    options.Endpoint = GetConfigurationValue(builder.Configuration, "R2:Endpoint", "R2_ENDPOINT");
});
builder.Services.AddScoped<IProductImageStorage, R2ProductImageStorage>();
var connectionString = GetRequiredConnectionString(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtKey = GetRequiredJwtKey(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (UnauthorizedAccessException exception)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { message = exception.Message });
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();
app.MapControllers();

app.Run();

static IReadOnlyDictionary<string, string?> LoadDotEnv(string contentRootPath)
{
    var envPath = Path.Combine(contentRootPath, ".env");
    if (!File.Exists(envPath))
    {
        return new Dictionary<string, string?>();
    }

    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        if (key.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            key = key["export ".Length..].Trim();
        }

        var value = line[(separatorIndex + 1)..].Trim().Trim('"').Trim('\'');

        if (key.Length > 0 && Environment.GetEnvironmentVariable(key) is null)
        {
            values[key] = value;
        }
    }

    return values;
}

static string? GetConfigurationValue(IConfiguration configuration, params string[] keys)
{
    foreach (var key in keys)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string GetRequiredConnectionString(IConfiguration configuration)
{
    var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");
    var privateDatabaseUrl = configuration["DATABASE_PRIVATE_URL"];
    var publicDatabaseUrl = configuration["DATABASE_URL"];

    var connectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
        ? configuredConnectionString
        : !string.IsNullOrWhiteSpace(privateDatabaseUrl)
            ? privateDatabaseUrl
            : publicDatabaseUrl;

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Database connection is not configured. Set ConnectionStrings__DefaultConnection or DATABASE_URL.");
    }

    return NormalizePostgresConnectionString(connectionString);
}

static string GetRequiredJwtKey(IConfiguration configuration)
{
    var jwtKey = configuration["Jwt:Key"];

    if (string.IsNullOrWhiteSpace(jwtKey))
    {
        throw new InvalidOperationException("Jwt:Key is not configured.");
    }

    if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    {
        throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
    }

    return jwtKey;
}

static string NormalizePostgresConnectionString(string connectionString)
{
    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var databaseUri)
        || (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql"))
    {
        return connectionString;
    }

    var userInfo = databaseUri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
        Database = Uri.UnescapeDataString(databaseUri.AbsolutePath.TrimStart('/')),
    };

    if (userInfo.Length > 0 && !string.IsNullOrWhiteSpace(userInfo[0]))
    {
        builder.Username = Uri.UnescapeDataString(userInfo[0]);
    }

    if (userInfo.Length > 1)
    {
        builder.Password = Uri.UnescapeDataString(userInfo[1]);
    }

    ApplyPostgresUriQuery(databaseUri, builder);
    return builder.ConnectionString;
}

static void ApplyPostgresUriQuery(Uri databaseUri, NpgsqlConnectionStringBuilder builder)
{
    var query = databaseUri.Query.TrimStart('?');

    if (string.IsNullOrWhiteSpace(query))
    {
        return;
    }

    foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = item.Split('=', 2);
        var key = Uri.UnescapeDataString(parts[0]).Replace("_", string.Empty, StringComparison.Ordinal);
        var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<SslMode>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
        else if (key.Equals("applicationname", StringComparison.OrdinalIgnoreCase))
        {
            builder.ApplicationName = value;
        }
    }
}
