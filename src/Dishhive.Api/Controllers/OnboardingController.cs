using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>Controls the one-time setup experience for a new, empty Dishhive database.</summary>
[ApiController]
[Route("api/[controller]")]
public class OnboardingController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly ILogger<OnboardingController> _logger;
    private readonly bool _demoModeEnabled;

    public OnboardingController(
        DishhiveDbContext context,
        ILogger<OnboardingController> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _demoModeEnabled = configuration.GetValue<bool>("Demo:Enabled");
    }

    /// <summary>
    /// Starts onboarding for a genuinely empty database. The in-progress marker makes
    /// the operation idempotent and lets an interrupted wizard resume after it has
    /// already saved preferences or family members.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(OnboardingStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingStatusDto>> Start(CancellationToken cancellationToken)
    {
        // DemoDataSeeder is a background service. Suppress onboarding immediately so
        // the first browser request cannot race the demo household being inserted.
        if (_demoModeEnabled)
        {
            return Ok(new OnboardingStatusDto(false));
        }

        var status = await _context.UserSettings.FindAsync([UserSettingKeys.OnboardingStatus], cancellationToken);
        if (status != null)
        {
            return Ok(new OnboardingStatusDto(IsInProgress(status.Value)));
        }

        if (!await IsDatabaseEmpty(cancellationToken))
        {
            return Ok(new OnboardingStatusDto(false));
        }

        var marker = new UserSetting
        {
            Key = UserSettingKeys.OnboardingStatus,
            Value = EnumNames.ToName(OnboardingState.InProgress)
        };
        _context.UserSettings.Add(marker);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests (for example, two newly opened tabs) can both observe
            // the empty database. The settings primary key makes one insert win; the
            // other request should return the winner's state instead of failing startup.
            _context.Entry(marker).State = EntityState.Detached;
            var concurrentStatus = await _context.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == UserSettingKeys.OnboardingStatus, cancellationToken);
            if (concurrentStatus != null)
            {
                return Ok(new OnboardingStatusDto(IsInProgress(concurrentStatus.Value)));
            }

            throw;
        }

        _logger.LogInformation("Started first-run onboarding");
        return Ok(new OnboardingStatusDto(true));
    }

    /// <summary>Completes or skips onboarding so it is never shown again.</summary>
    [HttpPost("complete")]
    [ProducesResponseType(typeof(OnboardingStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingStatusDto>> Complete(
        CompleteOnboardingDto dto,
        CancellationToken cancellationToken)
    {
        var status = await _context.UserSettings.FindAsync([UserSettingKeys.OnboardingStatus], cancellationToken);
        if (status == null)
        {
            status = new UserSetting { Key = UserSettingKeys.OnboardingStatus };
            _context.UserSettings.Add(status);
        }

        var finalState = dto.Skipped ? OnboardingState.Skipped : OnboardingState.Completed;
        status.Value = EnumNames.ToName(finalState);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("First-run onboarding {Outcome}", finalState);
        return Ok(new OnboardingStatusDto(false));
    }

    private async Task<bool> IsDatabaseEmpty(CancellationToken cancellationToken)
    {
        // Join/dependent tables cannot contain rows without one of these roots. AI test
        // records are infrastructure state and should not turn a fresh install into an
        // established household.
        return !await _context.FamilyMembers.AnyAsync(cancellationToken)
            && !await _context.Recipes.AnyAsync(cancellationToken)
            && !await _context.PlannedMeals.AnyAsync(cancellationToken)
            && !await _context.Cookbooks.AnyAsync(cancellationToken)
            && !await _context.UserSettings.AnyAsync(cancellationToken);
    }

    private static bool IsInProgress(string value) =>
        EnumNames.TryParse<OnboardingState>(value, out var state)
        && state == OnboardingState.InProgress;
}
