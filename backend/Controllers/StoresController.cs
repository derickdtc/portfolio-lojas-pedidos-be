using backend.Dtos;
using backend.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class StoresController(StoreService storeService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<StoreResponse>> Register(
        RegisterStoreRequest request,
        CancellationToken cancellationToken)
    {
        var response = await storeService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyList<StoreResponse>>> GetMy(CancellationToken cancellationToken)
    {
        return Ok(await storeService.GetMyAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<StoreResponse>> Create(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var response = await storeService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetMy), response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreResponse>> Update(
        int id,
        UpdateStoreRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await storeService.UpdateAsync(id, request, cancellationToken));
    }
}
