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

    [HttpGet("proxym3u8")]
    public async Task ProxyM3u8([FromQuery] string url, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid HTTP M3U8 URL.");
            return;
        }

        try
        {
            var _httpClient = httpClientFactory.CreateClient("m3u8Proxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "*/*");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            
            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                await Response.WriteAsync($"Upstream error: {response.ReasonPhrase}");
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var baseUri = new Uri(url);

            using var reader = new StringReader(content);
            using var writer = new StringWriter();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("#"))
                {
                    // Handle URI="..." in tags like #EXT-X-KEY or #EXT-X-MEDIA
                    if (line.Contains("URI=\""))
                    {
                        line = System.Text.RegularExpressions.Regex.Replace(line, @"URI=""([^""]+)""", m => {
                            string uriStr = m.Groups[1].Value;
                            if (uriStr.StartsWith("data:")) return m.Value; // Leave data URIs alone
                            string absoluteUrl = new Uri(baseUri, uriStr).ToString();
                            string endpoint = absoluteUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) 
                                ? "/api/stream/proxym3u8" 
                                : "/api/stream/proxysegment";
                            return $"URI=\"{endpoint}?url={Uri.EscapeDataString(absoluteUrl)}\"";
                        });
                    }
                    await writer.WriteLineAsync(line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    // Segment or variant playlist URL
                    string absoluteUrl = new Uri(baseUri, line.Trim()).ToString();
                    string endpoint = absoluteUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) 
                        ? "/api/stream/proxym3u8" 
                        : "/api/stream/proxysegment";
                    await writer.WriteLineAsync($"{endpoint}?url={Uri.EscapeDataString(absoluteUrl)}");
                }
                else
                {
                    await writer.WriteLineAsync(line);
                }
            }

            Response.ContentType = "application/vnd.apple.mpegurl";
            await Response.WriteAsync(writer.ToString());
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

    [HttpGet("proxysegment")]
    public async Task ProxySegment([FromQuery] string url, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            return;
        }

        try
        {
            var _httpClient = httpClientFactory.CreateClient("segmentProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "*/*");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            
            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                return;
            }

            if (response.Content.Headers.ContentType != null)
            {
                Response.ContentType = response.Content.Headers.ContentType.ToString();
            }
            else
            {
                Response.ContentType = "video/MP2T"; // Default for ts
            }

            // Stream the response body directly to the client
            await response.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
            }
        }
    }
}
