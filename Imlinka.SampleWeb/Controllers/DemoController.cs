using Imlinka.SampleWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace Imlinka.SampleWeb.Controllers;

[ApiController]
[Route("api/demo")]
public class DemoController(
    ILogger<DemoController> logger,
    IWorker worker,
    ITester tester) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> WriteLogs()
    {
        logger.LogInformation("Demo endpoint called at {UtcNow}", DateTime.UtcNow);

        await worker.DoWork();
        await tester.Test();

        return Ok();
    }
}