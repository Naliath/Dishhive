using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Health check controller to verify the API is running.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            application = "Dishhive",
            timestamp = DateTime.UtcNow
        });
    }
}
