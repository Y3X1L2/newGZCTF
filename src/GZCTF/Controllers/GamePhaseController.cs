using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/v1/phases")]
[Produces(MediaTypeNames.Application.Json)]
public class GamePhaseController : ControllerBase
{
    private readonly AppDbContext _context;

    public GamePhaseController(AppDbContext context) => _context = context;

    [HttpGet("{gameId:int}")]
    [Authorize]
    public async Task<IActionResult> List(int gameId)
    {
        var phases = await _context.GamePhases.Where(p => p.GameId == gameId).OrderBy(p => p.StartTime).ToListAsync();
        return Ok(phases);
    }

    [HttpPost("{gameId:int}")]
    [RequireTeacher]
    public async Task<IActionResult> Create(int gameId, [FromBody] GamePhase phase)
    {
        phase.GameId = gameId;
        _context.GamePhases.Add(phase);
        await _context.SaveChangesAsync();
        return Ok(phase);
    }

    [HttpPut("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> Update(int id, [FromBody] GamePhase updated)
    {
        var phase = await _context.GamePhases.FindAsync(id);
        if (phase is null) return NotFound();
        phase.Name = updated.Name;
        phase.StartTime = updated.StartTime;
        phase.EndTime = updated.EndTime;
        phase.CTFEnabled = updated.CTFEnabled;
        await _context.SaveChangesAsync();
        return Ok(phase);
    }

    [HttpDelete("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> Delete(int id)
    {
        var phase = await _context.GamePhases.FindAsync(id);
        if (phase is null) return NotFound();
        _context.GamePhases.Remove(phase);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
