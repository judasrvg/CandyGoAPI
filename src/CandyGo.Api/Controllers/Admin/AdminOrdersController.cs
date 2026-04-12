using System.Security.Claims;
using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public AdminOrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetAll([FromQuery] int take = 300)
    {
        var data = await _orderService.GetAllAsync(take);
        return Ok(data);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<OrderDto>> GetById(long id)
    {
        var order = await _orderService.GetByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPatch("{id:long}/status")]
    public async Task<ActionResult<OrderDto>> UpdateStatus(long id, [FromBody] UpdateOrderStatusRequest request)
    {
        var changedBy = User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";

        var order = await _orderService.UpdateStatusAsync(id, request, changedBy);
        return Ok(order);
    }
}
