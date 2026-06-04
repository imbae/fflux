# fflux

ffmpeg.autogen 기반 WPF 비디오 플레이어. 개발자 지향 고급 기능(실시간 통계, 구간 녹화, 자막 편집, AI 자막 생성, MISB KLV 파싱)을 제공합니다.

> **플랫폼**: Windows 10/11 (x64) · **.NET 10** · ffmpeg.autogen 8.1.0

---

## 목차

- [기능 개요](#기능-개요)
- [시작하기](#시작하기)
  - [사전 요구사항](#사전-요구사항)
  - [FFmpeg 바이너리 설정](#ffmpeg-바이너리-설정)
- [플레이어 사용법](#플레이어-사용법)
- [자막 편집기](#자막-편집기)
- [FFmpeg Explorer](#ffmpeg-explorer)
- [AI 자막 생성 (PRO)](#ai-자막-생성-pro)
  - [환경 변수 설정](#환경-변수-설정-env)
  - [Python Whisper 서버 설치 및 실행](#python-whisper-서버-설치-및-실행)
  - [AI 자막 생성 워크플로우](#ai-자막-생성-워크플로우)
- [MISB KLV 메타데이터 (PRO)](#misb-klv-메타데이터-pro)
- [소스 빌드](#소스-빌드)
- [라이선스](#라이선스)

---

## 기능 개요

| 기능 | 설명 | 무료 |
|------|------|:----:|
| 비디오 재생 | MP4 · MKV · AVI · MOV · WMV · WebM · TS 등 | ✅ |
| 라이브 스트리밍 | RTSP · RTP · UDP 주소 직접 입력 | ✅ |
| 오디오 출력 | WASAPI 저지연 출력, 볼륨/음소거 | ✅ |
| 재생 제어 | 배속 조정(0.25×~2×), 프레임 스텝, 시크 | ✅ |
| 실시간 통계 | FPS · 비트레이트 · 프레임 번호 실시간 표시 | ✅ |
| 미디어 정보 패널 | 코덱 · 해상도 · 스트림 정보 | ✅ |
| 구간 녹화 | Stream Copy 무손실 녹화, GIF 내보내기 | ✅ |
| 자막 편집기 | SRT/VTT 읽기·편집·저장, 재생 위치 연동 | ✅ |
| FFmpeg Explorer | FFmpeg 옵션 GUI 빌더 + 커맨드 생성기 | ✅ |
| **AI 자막 생성** | Whisper 전사 + Groq/DeepL 번역 → .srt 출력 | 🔒 PRO |
| **실시간 AI 번역** | 재생 중 음성을 실시간 전사·번역 오버레이 | 🔒 PRO |
| **MISB KLV** | KLV 메타데이터 파싱 + VMTI 바운딩 박스 오버레이 | 🔒 PRO |

---

## 시작하기

### 사전 요구사항

| 항목 | 버전 | 비고 |
|------|------|------|
| Windows | 10 / 11 (x64) | |
| [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0+ | 실행 필수 |
| [FFmpeg LGPL 빌드](https://github.com/BtbN/FFmpeg-Builds/releases) | 7.x / 8.x | `ffmpeg.exe` 포함 폴더 |
| Python *(AI 자막 기능만)* | 3.9 이상 | PATH 등록 필수 |

> ⚠️ **FFmpeg GPL 빌드 사용 금지** — fflux는 LGPL 라이선스를 준수합니다. GPL 코덱(x264, x265 등)이 포함된 빌드를 사용하면 LGPL 조건을 위반합니다.
> [BtbN FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases) 에서 `ffmpeg-master-latest-win64-lgpl.zip`을 다운로드하세요.

### FFmpeg 바이너리 설정

1. fflux 실행 후 좌측 메뉴 **Settings** 진입
2. **FFmpeg 설정** 카드 → `ffmpeg.exe`가 있는 폴더 경로 입력
3. **설정 저장** 클릭 → FFmpeg 초기화 성공 메시지 확인

---

## 플레이어 사용법

### 파일 열기

- 하단 컨트롤 바 **[열기]** 버튼 또는 비디오 영역에 파일 드래그앤드롭
- 자막 파일(`.srt`, `.vtt`)을 드롭하면 자막만 교체

### 스트리밍

하단 **[스트리밍]** 버튼 → URL 입력 → **[열기]**

```
지원 스킴: rtsp://  rtp://  udp://  srt://  rtmp://  rtmps://

예시:
  rtsp://192.168.0.1:554/stream
  udp://@239.0.0.1:1234
```

### 키보드 단축키

| 키 | 동작 |
|----|------|
| `Space` | 재생 / 일시정지 |
| `←` / `→` | 5초 뒤로 / 앞으로 |
| `Ctrl+←` / `Ctrl+→` | 이전 프레임 / 다음 프레임 |
| `M` | 음소거 토글 |
| `F` | 전체화면 토글 |
| `V` | 자막 표시/숨김 |
| `Escape` | 전체화면 해제 |

### 구간 녹화

하단 컨트롤 바 **[녹화]** 버튼 → 원하는 시점에 **[중지]** 버튼
(Stream Copy — 재인코딩 없이 원본 품질 그대로 MKV/MP4/TS로 저장)

---

## 자막 편집기

좌측 메뉴 **Subtitle Editor** 진입

- SRT / VTT 파일을 드롭하거나 **[자막 열기]** 버튼으로 불러오기
- 동영상 파일을 드롭하면 동일 이름의 자막이 있으면 함께 로드
- DataGrid에서 타임스탬프·텍스트 인라인 편집
- **[위치로 이동]** 버튼 → 해당 자막 구간의 비디오 프레임 미리보기
- `Ctrl+S` 저장 / `Insert` 행 추가 / `Ctrl+Del` 행 삭제

---

## FFmpeg Explorer

좌측 메뉴 **FFmpeg Explorer** 진입

복잡한 FFmpeg 커맨드 라인을 GUI로 조립합니다.

- 입력 파일 · 출력 파일 선택
- 비디오/오디오/필터 옵션을 드롭다운/슬라이더로 설정
- 생성된 커맨드를 클립보드 복사 또는 직접 실행

---

## AI 자막 생성 (PRO)

> PRO 기능 — `fflux.AiSubtitle` private 서브모듈이 포함된 빌드에서만 활성화됩니다.

동영상 파일에서 **Whisper**로 음성을 전사하고 **Groq(Llama 4)** 또는 **DeepL**로 번역하여 `.srt` 파일을 생성합니다.

### 환경 변수 설정 (`.env`)

프로젝트 루트 또는 실행 파일 디렉터리에 `.env` 파일을 생성합니다
(`.env.example`을 복사하여 편집하세요).

```dotenv
# ── Groq API 키 (필수) ────────────────────────────────────────
# 발급: https://console.groq.com → API Keys → Create API Key
# 무료 플랜 제공 (분당 요청 제한 있음)
GROQ_API_KEY=gsk_xxxxxxxxxxxxxxxxxxxxxxxxxxxx

# ── 번역 AI 모델 (선택 — 기본값으로 동작) ────────────────────
# Groq에서 제공하는 Llama 4 모델 식별자
# 최신 모델 목록: https://console.groq.com → Models
AI_MODEL=meta-llama/llama-4-scout-17b-16e-instruct

# ── OpenAI 호환 엔드포인트 (선택 — 변경 불필요) ──────────────
AI_PROVIDER=https://api.groq.com/openai/v1

# ── Python Whisper 서버 URL ───────────────────────────────────
# 로컬 서버: http://localhost:8765
# 원격 서버: https://your-deployed-server
PYTHON_API_URL=http://localhost:8765

# ── DeepL API 키 (선택) ───────────────────────────────────────
# 발급: https://www.deepl.com/pro-api (무료 플랜 500,000자/월)
# 없으면 DeepL 번역 엔진이 비활성화됩니다
# DEEPL_API_KEY=
```

> `.env` 파일은 **절대 Git에 커밋하지 마세요.** `.gitignore`에 이미 포함되어 있습니다.

#### 환경 변수 설명

| 변수 | 필수 | 설명 | 발급처 |
|------|:----:|------|--------|
| `GROQ_API_KEY` | ✅ | Groq LLM/Whisper API 인증 키 | [console.groq.com](https://console.groq.com) |
| `AI_MODEL` | — | 번역에 사용할 Llama 모델명 (기본값 사용 권장) | — |
| `AI_PROVIDER` | — | OpenAI 호환 엔드포인트 URL (Groq 기본값) | — |
| `PYTHON_API_URL` | ✅ | Whisper 전사 서버 URL | 직접 실행 또는 원격 서버 |
| `DEEPL_API_KEY` | — | DeepL 번역 엔진 (없으면 Groq 번역만 사용) | [deepl.com/pro-api](https://www.deepl.com/pro-api) |

---

### Python Whisper 서버 설치 및 실행

AI 자막 생성은 [faster-whisper](https://github.com/SYSTRAN/faster-whisper) 기반 로컬 Python 서버에서 음성 전사를 수행합니다.

#### 1단계 — Python 패키지 설치 (최초 1회)

```bash
cd fflux.AiSubtitle/python

pip install -r requirements.txt
```

주요 패키지: `faster-whisper`, `fastapi`, `uvicorn`

> GPU(CUDA) 사용 시 PyTorch CUDA 버전이 필요합니다.
> [pytorch.org](https://pytorch.org/get-started/locally/) 에서 CUDA 버전에 맞는 설치 명령을 확인하세요.
> CUDA 12.6 버전 설치 권장 (13버전 이후 지원 안됨)

#### 2단계 — 서버 실행

**방법 A: fflux 앱 내에서 시작 (권장)**

1. 좌측 메뉴 **AI Subtitle** 진입
2. **Whisper 서버** 카드에서 모델 선택
3. GPU 사용 가능 시 **GPU 사용 (CUDA)** 체크
4. **[서버 시작]** 클릭 → 하단 로그 패널에서 진행 확인
5. "● 서버 연결됨" 상태가 되면 자막 생성 버튼 활성화

**방법 B: 직접 실행**

```bash
# CPU, base 모델 (기본)
python whisper_server.py

# GPU + 고정밀 모델
python whisper_server.py --model large-v3 --device cuda --port 8765
```

`.env`의 `PYTHON_API_URL`을 실행한 서버 주소와 일치시키세요.

#### Whisper 모델 비교

| 모델 | 크기 | 상대 속도 | 정확도 | 권장 용도 |
|------|------|--------:|--------|----------|
| `tiny` | ~75 MB | 32× | 낮음 | 빠른 초안 확인 |
| `base` | ~145 MB | 16× | 보통 | 일반 대화 |
| `small` | ~465 MB | 6× | 좋음 | 강연·인터뷰 |
| `medium` | ~1.5 GB | 2× | 매우 좋음 | 전문 콘텐츠 |
| `large-v3` | ~3 GB | 1× | 최고 | 고품질 필요 시 |

> GPU(CUDA) 환경에서는 `large-v3` 모델도 실용적인 속도로 동작합니다.

---

### AI 자막 생성 워크플로우

```
① Python 서버 시작
        ↓
② 서버 상태 "● 서버 연결됨" 확인 → 자막 생성 버튼 활성화
        ↓
③ 소스 동영상 파일 선택
        ↓
④ 번역 설정
   · 소스 언어: 자동 감지 또는 수동 선택
   · 대상 언어: 번역 결과 언어
   · 번역 엔진: Groq (Llama 4) 또는 DeepL
   · 번역 스타일: 일반 / 자막형 / 격식체 등
        ↓
⑤ [🎬 자막 생성 시작] 클릭
        ↓
⑥ 하단 로그 패널에서 진행 확인
   · 🎙 전사 시작… → 완료
   · 🌐 번역 시작… → 완료
   · 💾 저장 중…
        ↓
⑦ 동영상과 같은 폴더에 .srt 파일 저장 완료
```

#### 긴 영상 처리

영상이 길면 600초 단위 청크로 자동 분할하여 순차 처리합니다.
Groq API 무료 플랜은 분당 요청 수 제한(RPM)이 있으므로 긴 영상은 시간이 소요됩니다.

#### 번역 캐시

동일한 구문은 SQLite 캐시(`%APPDATA%\fflux\AiSubtitle\translation_cache.db`)에서 즉시 반환하여 API 호출을 최소화합니다.

---

## MISB KLV 메타데이터 (PRO)

> PRO 기능 — `fflux.Misb` private 서브모듈이 포함된 빌드에서만 활성화됩니다.

**MISB(Motion Imagery Standards Board) ST 0601** KLV 메타데이터를 파싱하여 항공 영상의 센서 위치·자세·VMTI 표적 정보를 실시간으로 오버레이합니다.

### 활성화 방법

1. 플레이어 우상단 **PRO 패널** → 📍(Location) 아이콘 클릭
2. KLV가 포함된 영상 파일 열기 → 자동으로 타임라인 인덱싱 시작
3. 인덱싱 완료 후 오버레이 및 메타데이터 패널 활성화

### 기능

| 기능 | 설명 |
|------|------|
| VMTI 바운딩 박스 | 프레임별 표적 위치에 바운딩 박스 + 분류 레이블 오버레이 |
| 메타데이터 패널 | 센서 위치(위도/경도/고도), 플랫폼 자세(Heading/Pitch/Roll), 센서 FOV 실시간 표시 |
| 프레임 중심 | 프레임 센터 좌표 및 4개 모서리 GeoPoint 표시 |
| 타임라인 동기화 | 시크 시 해당 위치의 KLV 메타데이터 즉시 업데이트 |

### 지원 표준

- **MISB ST 0601** — UAS Datalink Local Set
- **MISB ST 0903** — VMTI (Video Moving Target Indicator)

---

## 소스 빌드

### 빌드 환경

```
.NET 10 SDK
Visual Studio 2022 17.10+ 또는 JetBrains Rider 2024.1+
```

### Public 빌드 (서브모듈 없이)

`fflux.Misb` / `fflux.AiSubtitle` 서브모듈 없이 빌드하면 PRO 기능 없이 동작하는 바이너리가 생성됩니다.

```bash
git clone https://github.com/imbae/fflux.git
cd fflux
dotnet build fflux/fflux.UI.csproj
```

`Directory.Build.props`가 서브모듈 존재 여부를 자동 감지하며, 없으면 `#if MISB` / `#if AI_SUBTITLE` 블록이 컴파일에서 제외됩니다.

### PRO 빌드 (서브모듈 포함)

```bash
git clone --recurse-submodules https://github.com/imbae/fflux.git
cd fflux
dotnet build fflux.slnx
```

---

## 프로젝트 구조

```
fflux/
├── fflux.Core/            ← ffmpeg.autogen 핵심 엔진 (디코더, 파서, 모델)
├── fflux/                 ← WPF UI (fflux.UI.csproj)
│   └── Modules/
│       ├── Player/        ← 비디오 플레이어
│       ├── SubtitleEditor/← 자막 편집기
│       ├── FFmpegExplorer/← FFmpeg 커맨드 빌더
│       ├── AiSubtitle/    ← AI 자막 생성 페이지 (PRO UI)
│       └── MisbViewer/    ← MISB 오버레이 컨트롤 (PRO UI)
├── tests/
│   └── fflux.Core.Tests/
│
│   (별도 Private 저장소)
├── fflux.Misb/            ← MISB KLV 파싱 엔진 (PRO)
└── fflux.AiSubtitle/      ← AI 자막·번역 엔진 (PRO)
    └── python/
        ├── whisper_server.py    ← faster-whisper FastAPI 서버
        └── requirements.txt
```

---

## 라이선스

fflux는 **LGPL(GNU Lesser General Public License)** 라이선스로 배포됩니다.

- FFmpeg **LGPL 빌드**를 사용하여 LGPL 조건을 준수합니다
- GPL 코덱(x264, x265 등) 링킹 금지
- 소스 수정 배포 시 변경 내역 공개 의무

---

## 기여 및 문의

- 버그 제보 및 기능 제안: [Issues](https://github.com/imbae/fflux/issues)
- PRO 기능(AI 자막, MISB) 관련 문의는 별도 연락 바랍니다
