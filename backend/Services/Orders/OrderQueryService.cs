using backend.Data;
using backend.Dtos;
using backend.Exceptions;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services.Orders;

public sealed class OrderQueryService(AppDbContext dbContext, ICurrentUserService currentUserService)
{
    private const string CreatedStatus = "created";

    public async Task<IReadOnlyList<OrderSummaryResponse>> GetAsync(
        string? customerName,
        DateTime? startDate,
        DateTime? endDate,
        string? orderIds,
        CancellationToken cancellationToken)
    {
        var filters = CreateFilters(customerName, startDate, endDate, orderIds);
        var storeId = currentUserService.GetCurrentStoreId();
        var orders = await BuildFilteredQuery(storeId, filters)
            .Include(order => order.Items)
            .AsSplitQuery()
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken);

        return orders.Select(OrderMapper.ToSummaryResponse).ToList();
    }

    public async Task<PagedResponse<OrderSummaryResponse>> GetPagedAsync(
        string? customerName,
        DateTime? startDate,
        DateTime? endDate,
        string? orderIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var filters = CreateFilters(customerName, startDate, endDate, orderIds);
        var storeId = currentUserService.GetCurrentStoreId();
        var query = BuildFilteredQuery(storeId, filters);
        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .Include(order => order.Items)
            .AsSplitQuery()
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<OrderSummaryResponse>(
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            orders.Select(OrderMapper.ToSummaryResponse).ToList());
    }

    public async Task<OrderResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var storeId = currentUserService.GetCurrentStoreId();
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(existingOrder => existingOrder.Items)
            .FirstOrDefaultAsync(
                existingOrder => existingOrder.Id == id && existingOrder.StoreId == storeId,
                cancellationToken)
            ?? throw ApiException.NotFound();

        return OrderMapper.ToResponse(order);
    }

    private IQueryable<Order> BuildFilteredQuery(int storeId, OrderFilters filters)
    {
        var query = dbContext.Orders
            .AsNoTracking()
            .Where(order => order.StoreId == storeId)
            .Where(order => order.Status == CreatedStatus);

        if (!filters.HasAny)
        {
            return query;
        }

        return query.Where(order =>
            (filters.CustomerNamePattern != null
                && order.CustomerName != null
                && EF.Functions.ILike(order.CustomerName, filters.CustomerNamePattern))
            || (filters.OrderIds.Length > 0 && filters.OrderIds.Contains(order.Id))
            || (filters.HasDateFilter
                && (!filters.StartDateUtc.HasValue || order.CreatedAtUtc >= filters.StartDateUtc.Value)
                && (!filters.EndDateExclusiveUtc.HasValue || order.CreatedAtUtc < filters.EndDateExclusiveUtc.Value)));
    }

    private static OrderFilters CreateFilters(
        string? customerName,
        DateTime? startDate,
        DateTime? endDate,
        string? orderIds)
    {
        var parsedOrderIds = ParseOrderIds(orderIds);
        var normalizedCustomerName = NormalizeOptional(customerName);
        var startDateUtc = startDate.HasValue
            ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
            : (DateTime?)null;
        var endDateExclusiveUtc = endDate.HasValue
            ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc)
            : (DateTime?)null;

        if (startDateUtc.HasValue
            && endDateExclusiveUtc.HasValue
            && startDateUtc.Value >= endDateExclusiveUtc.Value)
        {
            throw ApiException.BadRequest("A data inicial deve ser menor ou igual a data final.");
        }

        return new OrderFilters(
            normalizedCustomerName is null ? null : $"%{normalizedCustomerName}%",
            parsedOrderIds,
            startDateUtc,
            endDateExclusiveUtc);
    }

    private static int[] ParseOrderIds(string? orderIds)
    {
        if (string.IsNullOrWhiteSpace(orderIds))
        {
            return [];
        }

        var parsedOrderIds = new List<int>();
        var parts = orderIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var orderId) || orderId <= 0)
            {
                throw ApiException.BadRequest(
                    "A lista orderIds deve conter apenas numeros positivos separados por virgula.");
            }

            parsedOrderIds.Add(orderId);
        }

        return parsedOrderIds.Distinct().ToArray();
    }

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1)
        {
            throw ApiException.BadRequest("O parametro page deve ser maior ou igual a 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw ApiException.BadRequest("O parametro pageSize deve estar entre 1 e 100.");
        }

        if (page > (int.MaxValue / pageSize) + 1)
        {
            throw ApiException.BadRequest("A combinacao de page e pageSize e muito grande.");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record OrderFilters(
        string? CustomerNamePattern,
        int[] OrderIds,
        DateTime? StartDateUtc,
        DateTime? EndDateExclusiveUtc)
    {
        public bool HasDateFilter => StartDateUtc.HasValue || EndDateExclusiveUtc.HasValue;

        public bool HasAny => CustomerNamePattern is not null || OrderIds.Length > 0 || HasDateFilter;
    }
}
