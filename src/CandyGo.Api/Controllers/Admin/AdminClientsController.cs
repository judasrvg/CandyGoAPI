using CandyGo.Api.DTOs;
using CandyGo.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandyGo.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/clients")]
public sealed class AdminClientsController : ControllerBase
{
    private readonly IClientService _clientService;

    public AdminClientsController(IClientService clientService)
    {
        _clientService = clientService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> GetAll()
    {
        var data = await _clientService.GetAllAsync();
        return Ok(data);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ClientDto>> GetById(long id)
    {
        var data = await _clientService.GetByIdAsync(id);
        if (data is null)
        {
            return NotFound();
        }

        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create([FromBody] CreateClientByAdminRequest request)
    {
        var created = await _clientService.CreateByAdminAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
