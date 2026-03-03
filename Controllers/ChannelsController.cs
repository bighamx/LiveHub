using Microsoft.AspNetCore.Mvc;

namespace LiveStreamAppWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ChannelsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // Proxy the platforms JSON
    // GET /api/channels
    [HttpGet]
    public async Task<IActionResult> GetChannels()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync("http://api.vipmisss.com:81/mf/json.txt");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            
            return StatusCode((int)response.StatusCode, "Failed to fetch platforms.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    // Proxy the specific streamer list details by platform address
    // GET /api/channels/{platformAddress}
    [HttpGet("{platformAddress}")]
    public async Task<IActionResult> GetStreamers(string platformAddress)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"http://api.vipmisss.com:81/mf/{platformAddress}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            
            return StatusCode((int)response.StatusCode, "Failed to fetch streamers.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
