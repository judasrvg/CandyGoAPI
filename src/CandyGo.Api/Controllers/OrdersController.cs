using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers;

[ApiController]
[Authorize(Roles = "client")]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetMyOrders([FromQuery] int take = 100)
    {
        var clientId = GetCurrentUserId();
        var data = await _orderService.GetByClientIdAsync(clientId, take);
        return Ok(data);
    }

    [HttpGet("mine/{orderId:long}")]
    public async Task<ActionResult<OrderDto>> GetMyOrderById(long orderId)
    {
        var clientId = GetCurrentUserId();
        var order = await _orderService.GetByIdAsync(orderId);

        if (order is null || order.ClientId != clientId)
        {
            return NotFound();
        }

        return Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var clientId = GetCurrentUserId();
        var created = await _orderService.CreateOrderAsync(clientId, request);
        return CreatedAtAction(nameof(GetMyOrderById), new { orderId = created.Id }, created);
    }

    private long GetCurrentUserId()
    {
        var subClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Sid)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("sub");

        if (!long.TryParse(subClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Token inválido.");
        }

        return userId;
    }
}
