using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace LiveStreamAppWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamController : ControllerBase
{
    private readonly ILogger<StreamController> _logger;
    private readonly IConfiguration _configuration;
    private static readonly SemaphoreSlim _ffmpegSemaphore = new(10, 10);

    private static readonly Regex SafeStreamUrlPattern = new(
        @"^(rtmp|rtsp|https?)://[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(:\d{1,5})?(/[^\s""'`<>]*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public StreamController(ILogger<StreamController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    private bool IsUrlAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        if (host == "localhost" || host == "127.0.0.1" || host.StartsWith("10.") ||
            host.StartsWith("192.168.") || host.StartsWith("172.") || host == "169.254.169.254")
            return false;

        var allowedDomains = _configuration.GetSection("Stream:AllowedDomains").Get<string[]>();
        if (allowedDomains is { Length: > 0 })
        {
            return allowedDomains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    [HttpGet]
    public async Task GetStream([FromQuery] string rtmpUrl)
    {
        if (string.IsNullOrWhiteSpace(rtmpUrl) ||
            (!rtmpUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) &&
             !rtmpUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid stream URL.");
            return;
        }

        if (!SafeStreamUrlPattern.IsMatch(rtmpUrl))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid stream URL format.");
            return;
        }

        var maxConcurrent = _configuration.GetValue("Stream:MaxConcurrentFfmpeg", 10);
        _ffmpegSemaphore.Wait(0); // non-blocking check
        if (_ffmpegSemaphore.CurrentCount == 0 && maxConcurrent > 0)
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("Too many concurrent streams. Please try again later.");
            return;
        }

        if (!await _ffmpegSemaphore.WaitAsync(TimeSpan.FromSeconds(5), HttpContext.RequestAborted))
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("Too many concurrent streams. Please try again later.");
            return;
        }

        try
        {
            Response.ContentType = "video/x-flv";

            string ffmpegPath = _configuration.GetValue("Stream:FfmpegPath", "ffmpeg")!;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string localFfmpeg = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg");
                if (System.IO.File.Exists(localFfmpeg))
                    ffmpegPath = localFfmpeg;
                else if (System.IO.File.Exists("/usr/bin/ffmpeg"))
                    ffmpegPath = "/usr/bin/ffmpeg";
                else if (System.IO.File.Exists("/usr/local/bin/ffmpeg"))
                    ffmpegPath = "/usr/local/bin/ffmpeg";
            }

            var sanitizedUrl = rtmpUrl.Replace("\"", "").Replace("'", "").Replace("`", "");

            string inputArgs = sanitizedUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                ? $"-rtsp_transport tcp -i \"{sanitizedUrl}\""
                : $"-i \"{sanitizedUrl}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"{inputArgs} -c:v libx264 -preset ultrafast -tune zerolatency -c:a aac -f flv -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);

            if (process == null)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Failed to start transcoding process.");
                return;
            }

            _ = Task.Run(() => process.StandardError.BaseStream.CopyToAsync(Stream.Null));

            await process.StandardOutput.BaseStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected during stream: {Url}", rtmpUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming {Url}", rtmpUrl);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Internal server error.");
            }
        }
        finally
        {
            _ffmpegSemaphore.Release();
        }
    }

    [HttpGet("proxyflv")]
    public async Task ProxyFlv([FromQuery] string flvUrl, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(flvUrl) ||
            !Uri.TryCreate(flvUrl, UriKind.Absolute, out _) ||
            !flvUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid HTTP FLV URL.");
            return;
        }

        if (!IsUrlAllowed(flvUrl))
        {
            Response.StatusCode = 403;
            await Response.WriteAsync("URL not allowed.");
            return;
        }

        Response.ContentType = "video/x-flv";

        try
        {
            var client = httpClientFactory.CreateClient("flvProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, flvUrl);
            request.Headers.Add("Accept", "*/*");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                await Response.WriteAsync($"Upstream error: {response.ReasonPhrase}");
                return;
            }

            await response.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected during FLV proxy: {Url}", flvUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying FLV: {Url}", flvUrl);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Proxy error.");
            }
        }
    }

    [HttpGet("proxym3u8")]
    public async Task ProxyM3u8([FromQuery] string url, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out _) ||
            !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Invalid HTTP M3U8 URL.");
            return;
        }

        if (!IsUrlAllowed(url))
        {
            Response.StatusCode = 403;
            await Response.WriteAsync("URL not allowed.");
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("m3u8Proxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "*/*");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                await Response.WriteAsync($"Upstream error: {response.ReasonPhrase}");
                return;
            }

            var content = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            var baseUri = new Uri(url);

            using var reader = new StringReader(content);
            using var writer = new StringWriter();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("#"))
                {
                    if (line.Contains("URI=\""))
                    {
                        line = Regex.Replace(line, @"URI=""([^""]+)""", m =>
                        {
                            string uriStr = m.Groups[1].Value;
                            if (uriStr.StartsWith("data:")) return m.Value;
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
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected during M3U8 proxy: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying M3U8: {Url}", url);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Proxy error.");
            }
        }
    }

    [HttpGet("proxysegment")]
    public async Task ProxySegment([FromQuery] string url, [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out _) ||
            !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = 400;
            return;
        }

        if (!IsUrlAllowed(url))
        {
            Response.StatusCode = 403;
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("segmentProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "*/*");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                Response.StatusCode = (int)response.StatusCode;
                return;
            }

            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "video/MP2T";

            await response.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected during segment proxy: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying segment: {Url}", url);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
            }
        }
    }
}
