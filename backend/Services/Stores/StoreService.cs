using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Stores;

public sealed class StoreService(AppDbContext dbContext, ICurrentUserService currentUserService)
{
    public async Task<StoreResponse> RegisterAsync(
        RegisterStoreRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequired(request.Name)
            ?? throw ApiException.BadRequest("Informe o nome da loja.");
        var alreadyExists = await dbContext.Stores.AnyAsync(
            existingStore => existingStore.Name == name,
            cancellationToken);

        if (alreadyExists)
        {
            throw ApiException.Conflict("Loja ja cadastrada.");
        }

        var store = new Store
        {
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
        };

        dbContext.Stores.Add(store);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(store, StoreRoles.Owner);
    }

    public async Task<IReadOnlyList<StoreResponse>> GetMyAsync(CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        return await dbContext.StoreUsers
            .AsNoTracking()
            .Include(storeUser => storeUser.Store)
            .Where(storeUser => storeUser.UserId == userId)
            .Where(storeUser => storeUser.IsActive)
            .Where(storeUser => storeUser.Store.IsActive)
            .OrderBy(storeUser => storeUser.Store.Name)
            .Select(storeUser => ToResponse(storeUser.Store, storeUser.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreResponse> CreateAsync(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequired(request.Name)
            ?? throw ApiException.BadRequest("Informe o nome da loja.");
        var store = new Store
        {
            Name = name,
            Cnpj = NormalizeOptional(request.Cnpj),
            Phone = NormalizeOptional(request.Phone),
            Address = NormalizeOptional(request.Address),
            CreatedAtUtc = DateTime.UtcNow,
        };

        dbContext.Stores.Add(store);
        dbContext.StoreUsers.Add(new StoreUser
        {
            Store = store,
            UserId = currentUserService.GetUserId(),
            Role = StoreRoles.Owner,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(store, StoreRoles.Owner);
    }

    public async Task<StoreResponse> UpdateAsync(
        int id,
        UpdateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var storeUser = await dbContext.StoreUsers
            .Include(existingStoreUser => existingStoreUser.Store)
            .FirstOrDefaultAsync(
                existingStoreUser => existingStoreUser.UserId == userId && existingStoreUser.StoreId == id,
                cancellationToken)
            ?? throw ApiException.NotFound("Loja nao encontrada.");

        if (!StoreRoles.CanManageStore(storeUser.Role))
        {
            throw ApiException.Forbidden("Permissao insuficiente.");
        }

        var name = NormalizeOptional(request.Name);
        if (request.Name is not null && name is null)
        {
            throw ApiException.BadRequest("Informe o nome da loja.");
        }

        if (name is not null)
        {
            storeUser.Store.Name = name;
        }

        storeUser.Store.Cnpj = NormalizeOptional(request.Cnpj);
        storeUser.Store.Phone = NormalizeOptional(request.Phone);
        storeUser.Store.Address = NormalizeOptional(request.Address);
        storeUser.Store.IsActive = request.IsActive ?? storeUser.Store.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(storeUser.Store, storeUser.Role);
    }

    private static StoreResponse ToResponse(Store store, string role) =>
        new(
            store.Id,
            store.Name,
            store.Cnpj,
            store.Phone,
            store.Address,
            store.IsActive,
            role,
            store.CreatedAtUtc);

    private static string? NormalizeRequired(string? value)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
