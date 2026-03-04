# LiveHub

[English Version Below](#livehub-english-version)

一个全栈 Web 应用程序，专为观看各类直播流而设计。

## 架构 (Architecture)
- **后端:** ASP.NET Core 8.0 Web API
  - 代理请求以自动绕过外部 API 的 CORS 跨域限制。
  - 借助 `ffmpeg` 实现 RTMP 直播流到 HTTP-FLV 的实时转换，从而保证极佳的 Web 浏览器兼容性。
- **前端:** Vue 3 (Composition API / 组合式 API) + Vite + Tailwind CSS
  - 定制的流式布局，无论在桌面端宽屏还是移动端竖屏都能提供最佳的视频观看比例。
  - 原生支持 HLS `.m3u8` 流媒体格式。
  - 针对 `.flv` 直播流可自动无缝降级使用 `flv.js` 进行播放。

## 核心特性 (Features)
- 动态直播平台生态与创作者发现列表。
- 沉浸式 UI 体验，原生支持全屏模式。
- 快捷导航：支持“上一个 / 下一个”主播一键切换。
- 智能流媒体解码器：根据是标准 HLS 流还是需要服务器端 FFmpeg 代理的流，自动识别并切换相应的解码方式。

## 环境要求 (Prerequisites)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (v18 或更高版本)
- [FFmpeg](https://ffmpeg.org/) (需已安装并配置到系统环境变量 PATH 中)

## 快速开始 (Getting Started)

### 1. 构建与运行 (Build & Run)
前端项目已通过 `.csproj` 文件进行了关联配置，在启动 API 服务时会自动安装前端依赖项并协同运行服务。

```bash
cd LiveStreamAppWeb
dotnet run
```

运行后，即可在浏览器中访问控制台输出的地址（例如：`http://localhost:5175`）。

### 2. 开发模式 (Development Mode)
如果你希望在开发时享受 Vue 组件的模块热替换 (HMR / Hot-Reloading) 体验：
1. 在 Visual Studio 中或通过命令行运行 `dotnet run` 启动 ASP.NET Core 后端服务。
2. SPA 代理功能会在后台自动执行 `npm run dev` 以启动基于 Vite 的开发服务器（地址通常为 `http://localhost:44417`），并将浏览器的直接请求无缝路由至 C# 的代理逻辑中！

---

# LiveHub (English Version)

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
