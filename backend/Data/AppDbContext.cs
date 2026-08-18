using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreUser> StoreUsers => Set<StoreUser>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");

            entity.HasKey(product => product.Id);

            entity.Property(product => product.ItemCode).IsRequired();
            entity.Property(product => product.Description).IsRequired();
            entity.Property(product => product.Cfop).IsRequired();
            entity.Property(product => product.Csosn).IsRequired();
            entity.Property(product => product.Ncm).IsRequired();
            entity.Property(product => product.Cst).IsRequired();
            entity.Property(product => product.Reference).IsRequired();
            entity.Property(product => product.ImageUrl1).HasMaxLength(2048);
            entity.Property(product => product.ImageKey1).HasMaxLength(512);
            entity.Property(product => product.ImageUrl2).HasMaxLength(2048);
            entity.Property(product => product.ImageKey2).HasMaxLength(512);
            entity.Property(product => product.CreatedAtUtc).HasDefaultValueSql("NOW()");

            entity.HasOne(product => product.Store)
                .WithMany(store => store.Products)
                .HasForeignKey(product => product.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(product => product.StoreId);
            entity.HasIndex(product => new { product.StoreId, product.ItemCode }).IsUnique();
            entity.HasIndex(product => new { product.StoreId, product.Description, product.Id });
            entity.HasIndex(product => product.Description);
            entity.HasIndex(product => product.Reference);
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("stores");

            entity.HasKey(store => store.Id);

            entity.Property(store => store.Name).IsRequired();
            entity.Property(store => store.IsActive).HasDefaultValue(true);
            entity.Property(store => store.CreatedAtUtc).HasDefaultValueSql("NOW()");

            entity.HasIndex(store => store.Name);
        });

        modelBuilder.Entity<StoreUser>(entity =>
        {
            entity.ToTable("store_users");

            entity.HasKey(storeUser => storeUser.Id);

            entity.Property(storeUser => storeUser.Role).IsRequired();
            entity.Property(storeUser => storeUser.IsActive).HasDefaultValue(true);
            entity.Property(storeUser => storeUser.CreatedAtUtc).HasDefaultValueSql("NOW()");

            entity.HasOne(storeUser => storeUser.Store)
                .WithMany(store => store.StoreUsers)
                .HasForeignKey(storeUser => storeUser.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(storeUser => storeUser.User)
                .WithMany(user => user.StoreUsers)
                .HasForeignKey(storeUser => storeUser.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(storeUser => storeUser.StoreId);
            entity.HasIndex(storeUser => storeUser.UserId);
            entity.HasIndex(storeUser => new { storeUser.StoreId, storeUser.UserId }).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(user => user.Id);

            entity.Property(user => user.Username).IsRequired();
            entity.Property(user => user.UsernameNormalized).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.IsActive).HasDefaultValue(true);
            entity.Property(user => user.CreatedAtUtc).HasDefaultValueSql("NOW()");

            entity.HasIndex(user => user.UsernameNormalized).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");

            entity.HasKey(order => order.Id);

            entity.Property(order => order.CreatedByUsername).IsRequired();
            entity.Property(order => order.CustomerName).HasMaxLength(120);
            entity.Property(order => order.Observations).HasMaxLength(1000);
            entity.Property(order => order.Status).IsRequired();
            entity.Property(order => order.CreatedAtUtc).HasDefaultValueSql("NOW()");

            entity.HasOne(order => order.Store)
                .WithMany(store => store.Orders)
                .HasForeignKey(order => order.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(order => order.StoreId);
            entity.HasIndex(order => new { order.StoreId, order.Status, order.CreatedAtUtc });
            entity.HasIndex(order => order.CreatedAtUtc);
            entity.HasIndex(order => order.CreatedByUserId);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");

            entity.HasKey(item => item.Id);

            entity.Property(item => item.ProductItemCode).IsRequired();
            entity.Property(item => item.ProductDescription).IsRequired();
            entity.Property(item => item.ProductReference).IsRequired();
            entity.Property(item => item.Cfop).IsRequired();
            entity.Property(item => item.Csosn).IsRequired();
            entity.Property(item => item.Ncm).IsRequired();
            entity.Property(item => item.Cst).IsRequired();

            entity.HasOne(item => item.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(item => item.OrderId);
            entity.HasIndex(item => item.ProductId);
        });
    }
}
