using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LiveStreamAppWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamController : ControllerBase
{
    [HttpGet]
    public async Task GetStream([FromQuery] string rtmpUrl)
    {
        if (string.IsNullOrWhiteSpace(rtmpUrl) || !rtmpUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid RTMP URL.");
            return;
        }

        Response.ContentType = "video/x-flv";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{rtmpUrl}\" -c:v copy -c:a copy -f flv -",
            RedirectStandardOutput = true,
            RedirectStandardError = true, // To avoid blocking
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(processStartInfo);
            
            if (process == null)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Failed to start ffmpeg process.");
                return;
            }

            // Fire and forget error reading to prevent blocking
            _ = Task.Run(() => process.StandardError.BaseStream.CopyToAsync(Stream.Null));

            // Pipe the ffmpeg standard output to the HTTP response body
            await process.StandardOutput.BaseStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected, this is expected.
        }
        catch (Exception ex)
        {
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync($"Internal server error: {ex.Message}");
            }
        }
    }

    [HttpGet("proxyflv")]
    public async Task ProxyFlv([FromQuery] string flvUrl, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(flvUrl) || !flvUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid HTTP FLV URL.");
            return;
        }

        Response.ContentType = "video/x-flv";

        try
        {
            var _httpClient = httpClientFactory.CreateClient("flvProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, flvUrl);
            
            // Add headers to masquerade as a normal browser/client
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "*/*");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            
            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                await Response.WriteAsync($"Upstream error: {response.ReasonPhrase}");
                return;
            }

            // Stream the response body directly to the client
            await response.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync($"Proxy error: {ex.Message}");
            }
        }
    }
}
