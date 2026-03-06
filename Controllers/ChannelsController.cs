using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace LiveStreamAppWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChannelsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChannelsController> _logger;
    private readonly string _apiBaseUrl;

    private static readonly Regex SafePathPattern = new(
        @"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled);

    public ChannelsController(
        IHttpClientFactory httpClientFactory,
        ILogger<ChannelsController> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiBaseUrl = configuration.GetValue<string>("ExternalApi:BaseUrl") ?? "http://api.vipmisss.com:81/mf";
    }

    [HttpGet]
    public async Task<IActionResult> GetChannels()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var platformsPath = "json.txt";
            var response = await client.GetAsync($"{_apiBaseUrl}/{platformsPath}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }

            return StatusCode((int)response.StatusCode, "Failed to fetch platforms.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch platforms from external API");
            return StatusCode(500, "Internal server error.");
        }
    }

    [HttpGet("{platformAddress}")]
    public async Task<IActionResult> GetStreamers(string platformAddress)
    {
        if (string.IsNullOrWhiteSpace(platformAddress) || !SafePathPattern.IsMatch(platformAddress))
        {
            return BadRequest("Invalid platform address.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_apiBaseUrl}/{platformAddress}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }

            return StatusCode((int)response.StatusCode, "Failed to fetch streamers.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch streamers for platform: {Platform}", platformAddress);
            return StatusCode(500, "Internal server error.");
        }
    }
}
