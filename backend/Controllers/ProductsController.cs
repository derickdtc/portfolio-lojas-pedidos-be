using backend.Dtos;
using backend.Models;
using backend.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductsController(
    ProductService productService,
    ProductImageService productImageService,
    ProductImportService productImportService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductStockResponse>>> Get(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await productService.GetAsync(search, cancellationToken));
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResponse<ProductStockResponse>>> GetPaged(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await productService.GetPagedAsync(search, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductStockResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        return Ok(await productService.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ProductStockResponse>> Create(
        [FromBody] CreateProductRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await productService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    [HttpPatch("{id:int}")]
    [HttpPut("{id:int}/sale-price")]
    [HttpPatch("{id:int}/sale-price")]
    public async Task<ActionResult<ProductStockResponse>> Update(
        int id,
        [FromBody] UpdateProductRequest? request,
        CancellationToken cancellationToken)
    {
        return Ok(await productService.UpdateAsync(id, request, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await productService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{productId:int}/images/{slot:int}")]
    [RequestSizeLimit(Product.PlannedMaxImageSizeBytes + 1_000_000)]
    public async Task<ActionResult<ProductStockResponse>> UploadImage(
        int productId,
        int slot,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        return Ok(await productImageService.UploadAsync(productId, slot, file, cancellationToken));
    }

    [HttpDelete("{productId:int}/images/{slot:int}")]
    public async Task<ActionResult<ProductStockResponse>> DeleteImage(
        int productId,
        int slot,
        CancellationToken cancellationToken)
    {
        return Ok(await productImageService.DeleteAsync(productId, slot, cancellationToken));
    }

    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ProductImportResponse>> Import(
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        return Ok(await productImportService.ImportAsync(file, cancellationToken));
    }
}
