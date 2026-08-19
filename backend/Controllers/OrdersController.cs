using backend.Dtos;
using backend.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class OrdersController(
    OrderQueryService orderQueryService,
    OrderService orderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryResponse>>> Get(
        [FromQuery] string? customerName,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? orderIds,
        CancellationToken cancellationToken)
    {
        return Ok(await orderQueryService.GetAsync(
            customerName,
            startDate,
            endDate,
            orderIds,
            cancellationToken));
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> GetPaged(
        [FromQuery] string? customerName,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? orderIds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await orderQueryService.GetPagedAsync(
            customerName,
            startDate,
            endDate,
            orderIds,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await orderService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<OrderResponse>> Update(
        int id,
        [FromBody] UpdateOrderRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await orderService.UpdateAsync(id, request, cancellationToken));
    }

    [HttpPost("{id:int}/edit")]
    public async Task<ActionResult<OrderResponse>> StartEdit(int id, CancellationToken cancellationToken)
    {
        return Ok(await orderService.StartEditAsync(id, cancellationToken));
    }

    [HttpDelete]
    public async Task<ActionResult<DeleteOrdersResponse>> Delete(
        [FromBody] DeleteOrdersRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await orderService.DeleteAsync(request, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        return Ok(await orderQueryService.GetByIdAsync(id, cancellationToken));
    }
}
