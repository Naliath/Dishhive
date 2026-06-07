using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/version")]
[Produces("application/json")]
public class VersionController : ControllerBase
{
    [HttpGet]
    public ActionResult<object> Get() => Ok(new { version = AppVersion.Version });
}
