using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public enum PhaseRequiredType { CTF }
public enum PhaseCheckResult { Allowed, DisabledByPhase, NoActivePhase }

public class GamePhaseService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GamePhaseService> _logger;

    public GamePhaseService(AppDbContext context, ILogger<GamePhaseService> logger)
    { _context = context; _logger = logger; }

    public async Task<PhaseCheckResult> CheckAsync(int gameId, PhaseRequiredType requiredType, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var activePhase = await _context.GamePhases
            .Where(p => p.GameId == gameId && p.StartTime <= now && p.EndTime >= now)
            .FirstOrDefaultAsync(token);

        if (activePhase is null)
            return PhaseCheckResult.NoActivePhase;

        return requiredType switch
        {
            PhaseRequiredType.CTF => activePhase.CTFEnabled ? PhaseCheckResult.Allowed : PhaseCheckResult.DisabledByPhase,
            _ => PhaseCheckResult.Allowed
        };
    }
}
