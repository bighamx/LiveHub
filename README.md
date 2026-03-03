# LiveStream App Web

A full-stack web application designed for viewing various live streams. 

## Architecture
- **Backend:** ASP.NET Core 8.0 Web API
  - Handles proxying requests to bypass CORS issues for external APIs.
  - Features real-time RTMP stream conversion to HTTP-FLV using `ffmpeg` for web compatibility.
- **Frontend:** Vue 3 (Composition API) + Vite + Tailwind CSS
  - Custom fluid layout optimized for both desktop and vertical (mobile) video aspect ratios.
  - Native HLS `.m3u8` support.
  - Automatic fallback to `flv.js` for `.flv` streams.

## Features
- Dynamic streaming platform and creator discovery list.
- Immersive UI with Fullscreen mode natively supported.
- Quick navigation: Next / Previous streamer switching.
- Auto-switching stream decoder depending on whether it is standard HLS or requires server-side FFmpeg proxy.

## Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (v18+)
- [FFmpeg](https://ffmpeg.org/) installed and available in system PATH.

## Getting Started

### 1. Build & Run
The frontend is configured via `.csproj` to automatically install dependencies and be served alongside the API.

```bash
cd LiveStreamAppWeb
dotnet run
```

Then navigate to the URL specified in your console output (e.g., `http://localhost:5175`).

### 2. Development Mode
To enjoy hot-reloading for Vue components:
1. Start the ASP.NET Core backend in Visual Studio or via `dotnet run`.
2. The SPA Proxy will automatically instantiate `npm run dev` to boot the Vite server at `http://localhost:44417`, seamlessly routing browser requests to the C# proxy logic!
