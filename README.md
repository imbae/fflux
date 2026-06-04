# fflux

🌐 **English** | [한국어](README.ko.md)

**Developer-focused WPF video player built on ffmpeg.autogen + .NET 10**

No external `ffmpeg.exe` process — uses the ffmpeg.autogen API directly. Fully LGPL-compliant with no GPL codec linking.

[![License: LGPL](https://img.shields.io/badge/License-LGPL-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-lightgrey.svg)]()
[![ffmpeg.autogen](https://img.shields.io/badge/ffmpeg.autogen-8.1.0-green.svg)](https://github.com/Ruslan-B/FFmpeg.AutoGen)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/imbae?style=flat&logo=github&label=Sponsor&color=ea4aaa)](https://github.com/sponsors/imbae)

---

![fflux Player](docs/screenshots/player.png)

---

## Table of Contents

- [Features](#features)
- [Screenshots](#screenshots)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [FFmpeg Setup](#ffmpeg-setup)
- [Player Usage](#player-usage)
- [Subtitle Editor](#subtitle-editor)
- [FFmpeg Explorer](#ffmpeg-explorer)
- [AI Subtitle Generation (PRO)](#ai-subtitle-generation-pro)
- [MISB KLV Metadata (PRO)](#misb-klv-metadata-pro)
- [Building from Source](#building-from-source)
- [License](#license)

---

## Features

| Feature | Description | Free |
|---------|-------------|:----:|
| Video Playback | MP4 · MKV · AVI · MOV · WMV · WebM · TS and more | ✅ |
| Live Streaming | RTSP · RTP · UDP direct URL input | ✅ |
| Audio Output | WASAPI low-latency output, volume/mute | ✅ |
| Playback Control | Speed (0.25×–2×), frame step, seek | ✅ |
| Real-time Stats | FPS · bitrate · frame number live display | ✅ |
| Media Info Panel | Codec · resolution · stream info sliding panel | ✅ |
| Segment Recording | Lossless Stream Copy recording, GIF export | ✅ |
| Subtitle Editor | SRT/VTT read · edit · save, playback sync | ✅ |
| FFmpeg Explorer | FFmpeg option GUI builder + command generator | ✅ |
| **AI Subtitle** | Whisper transcription + Groq/DeepL translation → .srt | 🔒 PRO |
| **Real-time AI Translation** | Live transcription + translation overlay during playback | 🔒 PRO |
| **MISB KLV** | KLV metadata parsing + VMTI bounding box overlay | 🔒 PRO |

---

## Screenshots

### Player — Media Info Panel · Segment Recording

![Player with media info panel](docs/screenshots/player.png)

The right-side panel shows real-time codec, resolution, and bitrate information. Stream Copy recording saves without re-encoding.

### Subtitle Editor

![Subtitle Editor](docs/screenshots/subtitle-editor.png)

Load SRT/VTT files, edit timestamps and text inline, and preview the corresponding video frame.

### FFmpeg Explorer

![FFmpeg Explorer](docs/screenshots/ffmpeg-explorer.png)

Build complex FFmpeg commands via GUI. Copy to clipboard or execute directly.

### AI Subtitle Generation (PRO)

![AI Subtitle](docs/screenshots/ai-subtitle.png)

Start the Whisper server from the built-in UI, transcribe audio, and translate with Groq (Llama 4) or DeepL.

### Settings

![Settings](docs/screenshots/settings.png)

Configure FFmpeg path, theme, hardware acceleration (D3D11VA · DXVA2 · CUDA · QSV), and decoder thread count.

---

## Getting Started

### Prerequisites

| Item | Version | Notes |
|------|---------|-------|
| Windows | 10 / 11 (x64) | |
| [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ | Required to run |
| [FFmpeg LGPL build](https://github.com/BtbN/FFmpeg-Builds/releases) | 7.x / 8.x | Folder containing `ffmpeg.exe` |
| Python *(AI subtitle only)* | 3.9+ | Must be in PATH |

> ⚠️ **Do not use FFmpeg GPL builds** — fflux is LGPL-licensed. Using builds with GPL codecs (x264, x265, etc.) violates LGPL terms.  
> Download `ffmpeg-master-latest-win64-lgpl.zip` from [BtbN FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases).

### FFmpeg Setup

1. Launch fflux and go to **Settings** in the left menu
2. Under **FFmpeg Settings**, enter the folder path containing `ffmpeg.exe`
3. Click **Save** and confirm the FFmpeg initialization message

---

## Player Usage

### Opening Files

- Click **[Open]** in the bottom control bar, or drag and drop a file onto the video area
- Dropping a subtitle file (`.srt`, `.vtt`) replaces the current subtitle only

### Live Streaming

Click **[Stream]** → enter URL → **[Open]**

```
Supported schemes: rtsp://  rtp://  udp://  srt://  rtmp://  rtmps://

Examples:
  rtsp://192.168.0.1:554/stream
  udp://@239.0.0.1:1234
```

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Space` | Play / Pause |
| `←` / `→` | Seek −5s / +5s |
| `Ctrl+←` / `Ctrl+→` | Previous frame / Next frame |
| `M` | Toggle mute |
| `F` | Toggle fullscreen |
| `V` | Toggle subtitle display |
| `Escape` | Exit fullscreen |

### Segment Recording

Click **[Record]** in the bottom bar → click **[Stop]** at the desired end point.  
(Stream Copy — saves at original quality as MKV/MP4/TS without re-encoding)

---

## Subtitle Editor

Go to **Subtitle Editor** in the left menu.

![Subtitle Editor](docs/screenshots/subtitle-editor.png)

- Drop an SRT/VTT file or click **[Open Subtitle]**
- Dropping a video file also loads a same-named subtitle if present
- Inline editing of timestamps and text in the DataGrid
- **[Go to Position]** button → preview the video frame at that subtitle's timestamp
- `Ctrl+S` save / `Insert` add row / `Ctrl+Del` delete row

---

## FFmpeg Explorer

Go to **FFmpeg Explorer** in the left menu.

![FFmpeg Explorer](docs/screenshots/ffmpeg-explorer.png)

Assemble complex FFmpeg command lines via GUI.

- Select input and output files
- Configure video/audio/filter options via dropdowns and sliders
- Copy the generated command to clipboard or run it directly

---

## AI Subtitle Generation (PRO)

> PRO feature — only activated in builds that include the `fflux.AiSubtitle` private submodule.

![AI Subtitle](docs/screenshots/ai-subtitle.png)

Transcribes audio from a video file using **Whisper** and translates it with **Groq (Llama 4)** or **DeepL** to produce a `.srt` file.

### Environment Variables (`.env`)

Create a `.env` file in the project root or the executable directory  
(copy `.env.example` and edit).

```dotenv
# Groq API key (required) — https://console.groq.com
GROQ_API_KEY=gsk_xxxxxxxxxxxxxxxxxxxxxxxxxxxx

# Translation model (optional — default works fine)
AI_MODEL=meta-llama/llama-4-scout-17b-16e-instruct

# OpenAI-compatible endpoint (optional — no change needed)
AI_PROVIDER=https://api.groq.com/openai/v1

# Python Whisper server URL
PYTHON_API_URL=http://localhost:8765

# DeepL API key (optional)
# DEEPL_API_KEY=
```

| Variable | Required | Description | Get it at |
|----------|:--------:|-------------|-----------|
| `GROQ_API_KEY` | ✅ | Groq LLM/Whisper API key | [console.groq.com](https://console.groq.com) |
| `AI_MODEL` | — | Llama model name for translation | — |
| `AI_PROVIDER` | — | OpenAI-compatible endpoint URL | — |
| `PYTHON_API_URL` | ✅ | Whisper transcription server URL | Run locally or use remote |
| `DEEPL_API_KEY` | — | DeepL translation engine (Groq-only without it) | [deepl.com/pro-api](https://www.deepl.com/pro-api) |

### Python Whisper Server

AI subtitle generation uses a local Python server based on [faster-whisper](https://github.com/SYSTRAN/faster-whisper).

```bash
cd fflux.AiSubtitle/python
pip install -r requirements.txt
```

> For GPU (CUDA), install the matching PyTorch version first from [pytorch.org](https://pytorch.org/get-started/locally/). CUDA 12.6 recommended.

**Start from within the app (recommended)**: Go to **AI Subtitle** → select a model in the Whisper Server card → click **[Start Server]**

**Manual start**:
```bash
python whisper_server.py                                  # CPU, base model
python whisper_server.py --model large-v3 --device cuda  # GPU, high accuracy
```

#### Whisper Model Comparison

| Model | Size | Speed | Accuracy |
|-------|------|------:|----------|
| `tiny` | ~75 MB | 32× | Low |
| `base` | ~145 MB | 16× | Fair |
| `small` | ~465 MB | 6× | Good |
| `medium` | ~1.5 GB | 2× | Very good |
| `large-v3` | ~3 GB | 1× | Best |

### Workflow

```
① Start Python server → ② Confirm "● Server connected"
        ↓
③ Select source video file
        ↓
④ Configure translation (source language · target language · engine · style)
        ↓
⑤ Click [🎬 Generate Subtitle]
        ↓
⑥ Monitor progress in the log panel (transcribe → translate → save)
        ↓
⑦ .srt file saved to the same folder as the video
```

Translated phrases are cached in SQLite (`%APPDATA%\fflux\AiSubtitle\translation_cache.db`) to avoid redundant API calls.

---

## MISB KLV Metadata (PRO)

> PRO feature — only activated in builds that include the `fflux.Misb` private submodule.

Parses **MISB ST 0601** KLV metadata and overlays sensor position, platform attitude, and VMTI target information in real time on aerial video footage.

| Feature | Description |
|---------|-------------|
| VMTI Bounding Boxes | Per-frame target bounding boxes with classification labels |
| Metadata Panel | Sensor lat/lon/alt, platform heading/pitch/roll, sensor FOV |
| Frame Center | Frame center coordinates and 4-corner GeoPoints |
| Timeline Sync | KLV metadata updates instantly on seek |

Supported standards: **MISB ST 0601** (UAS Datalink Local Set) · **MISB ST 0903** (VMTI)

---

## Building from Source

```
.NET 10 SDK
Visual Studio 2022 17.10+ or JetBrains Rider 2024.1+
```

### Public Build (no PRO features)

```bash
git clone https://github.com/imbae/fflux.git
cd fflux
dotnet build fflux/fflux.UI.csproj
```

`Directory.Build.props` auto-detects submodule presence. Without submodules, `#if MISB` / `#if AI_SUBTITLE` blocks are excluded and the build succeeds with PRO features simply disabled.

### PRO Build (with submodules)

```bash
git clone --recurse-submodules https://github.com/imbae/fflux.git
cd fflux
dotnet build fflux.slnx
```

### Project Structure

```
fflux/
├── fflux.Core/            ← ffmpeg.autogen engine (decoders, parsers, models)
├── fflux/                 ← WPF UI (fflux.UI.csproj)
│   └── Modules/
│       ├── Player/        ← Video player
│       ├── SubtitleEditor/← Subtitle editor
│       ├── FFmpegExplorer/← FFmpeg command builder
│       ├── AiSubtitle/    ← AI subtitle page (PRO UI)
│       └── MisbViewer/    ← MISB overlay controls (PRO UI)
├── tests/
│   └── fflux.Core.Tests/
│
│   (Separate private repositories)
├── fflux.Misb/            ← MISB KLV parsing engine (PRO)
└── fflux.AiSubtitle/      ← AI subtitle/translation engine (PRO)
    └── python/
        ├── whisper_server.py
        └── requirements.txt
```

---

## License

fflux is distributed under the **LGPL (GNU Lesser General Public License)**.

- Uses FFmpeg **LGPL builds** to maintain LGPL compliance
- No GPL codec linking (x264, x265, etc.)
- Modified source distributions must disclose changes

---

## Contributing & Contact

- Bug reports and feature requests: [Issues](https://github.com/imbae/fflux/issues)
- PRO feature inquiries (AI subtitle, MISB): please contact separately
- If fflux is useful to you, consider [sponsoring ☕](https://github.com/sponsors/imbae)
