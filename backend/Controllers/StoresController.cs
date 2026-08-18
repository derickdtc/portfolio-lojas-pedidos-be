using backend.Data;
using backend.Dtos;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class StoresController(AppDbContext dbContext, ICurrentUserService currentUserService)
    : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<StoreResponse>> Register(
        RegisterStoreRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequired(request.Name);
        if (name is null)
        {
            return BadRequest(new { message = "Informe o nome da loja." });
        }

        var alreadyExists = await dbContext.Stores
            .AnyAsync(existingStore => existingStore.Name == name, cancellationToken);

        if (alreadyExists)
        {
            return Conflict(new { message = "Loja ja cadastrada." });
        }

        var store = new Store
        {
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
        };

        dbContext.Stores.Add(store);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ToResponse(store, StoreRoles.Owner));
    }

    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyList<StoreResponse>>> GetMy(CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var stores = await dbContext.StoreUsers
            .AsNoTracking()
            .Include(storeUser => storeUser.Store)
            .Where(storeUser => storeUser.UserId == userId)
            .Where(storeUser => storeUser.IsActive)
            .Where(storeUser => storeUser.Store.IsActive)
            .OrderBy(storeUser => storeUser.Store.Name)
            .Select(storeUser => ToResponse(storeUser.Store, storeUser.Role))
            .ToListAsync(cancellationToken);

        return Ok(stores);
    }

    [HttpPost]
    public async Task<ActionResult<StoreResponse>> Create(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequired(request.Name);
        if (name is null)
        {
            return BadRequest(new { message = "Informe o nome da loja." });
        }

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

        var response = ToResponse(store, StoreRoles.Owner);
        return CreatedAtAction(nameof(GetMy), response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreResponse>> Update(
        int id,
        UpdateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.GetUserId();
        var storeUser = await dbContext.StoreUsers
            .Include(existingStoreUser => existingStoreUser.Store)
            .FirstOrDefaultAsync(
                existingStoreUser => existingStoreUser.UserId == userId && existingStoreUser.StoreId == id,
                cancellationToken);

        if (storeUser is null)
        {
            return NotFound(new { message = "Loja nao encontrada." });
        }

        if (!StoreRoles.CanManageStore(storeUser.Role))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Permissao insuficiente." });
        }

        var name = NormalizeOptional(request.Name);
        if (request.Name is not null && name is null)
        {
            return BadRequest(new { message = "Informe o nome da loja." });
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

        return Ok(ToResponse(storeUser.Store, storeUser.Role));
    }

    private static StoreResponse ToResponse(Store store, string role)
    {
        return new StoreResponse(
            store.Id,
            store.Name,
            store.Cnpj,
            store.Phone,
            store.Address,
            store.IsActive,
            role,
            store.CreatedAtUtc);
    }

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
