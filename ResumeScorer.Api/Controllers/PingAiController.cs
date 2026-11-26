using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

[ApiController, Route("api/ping-ai")]
public class PingAiController(IChatClient chat) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var res = await chat.GetResponseAsync("Say hello in one short sentence.");
        return Ok(res.Text);
    }
}