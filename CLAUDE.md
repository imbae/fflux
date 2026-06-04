# CLAUDE.md — fflux WPF Video Player Project

## 📌 프로젝트 개요

ffmpeg.autogen 기반의 WPF 비디오 플레이어입니다.
개발자용 고급 기능을 제공하며, 확장 모듈(MISB, AI 자막)은 별도 Private 프로젝트로 분리하여 관리합니다.

- **베이스 기술**: WPF (.NET 10.0), ffmpeg.autogen 8.1.0
- **라이선스**: LGPL (FFmpeg LGPL 빌드 사용 필수)
- **목표**: 오픈소스 공개 후 Private 확장 모듈 기반 수익화

---

## 🗂️ 프로젝트 구조

```
fflux/                                  ← Public 오픈소스 저장소
├── CLAUDE.md
├── README.md
├── LICENSE
├── fflux.slnx
│
├── fflux.Core/                         ← ffmpeg.autogen 핵심 엔진 (Public)
│   ├── Abstractions/                   ← 인터페이스 (IVideoDecoder, IStreamCopyRecorder 등)
│   ├── Decoders/                       ← VideoDecoder, AudioDecoder
│   ├── Muxers/                         ← StreamCopyRecorder (Stream Copy 녹화)
│   ├── Demuxers/                       ← MediaFileReader (컨테이너 파싱)
│   ├── Subtitles/                      ← SrtParser, AssParser, SubtitleDocument 모델
│   ├── Models/                         ← VideoFrame, AudioFrame, MediaInfo, SubtitleCue 등
│   └── Helpers/                        ← PixelFormatConverter, FFmpegLogBridge 등
│
├── fflux.UI/                           ← WPF UI 레이어 (Public)
│   ├── App.xaml
│   ├── Modules/
│   │   ├── Player/                     ← 플레이어 + 미디어 정보 사이드패널
│   │   │                                  + 실시간 비트레이트 차트 오버레이
│   │   ├── SubtitleEditor/             ← 자막 편집기 (SRT/ASS 읽기/편집/저장)
│   │   └── FFmpegExplorer/             ← FFmpeg 옵션 GUI 빌더 + 커맨드 생성기
│   │
│   └── Shared/
│       ├── Controls/                   ← 공용 CustomControl
│       ├── Converters/                 ← WPF ValueConverter
│       ├── Services/                   ← UI 서비스 (DialogService 등)
│       └── Helpers/
│
├── tests/
│   ├── fflux.Core.Tests/
│   └── fflux.UI.Tests/
│
└── docs/
    ├── CLAUDE.md                       ← 이 파일
    ├── phase-prompts.md                ← 단계별 프롬프트
    └── api/

--- (별도 Private 저장소) ---

fflux.Misb/                             ← Private: MISB KLV 파싱 모듈
fflux.AiSubtitle/                       ← Private: AI 자막/번역 모듈
```

---

## 🧠 아키텍처 원칙

### 기본 패턴
- **MVVM** 패턴 엄격히 준수 (View ↔ ViewModel ↔ Model)
- **Dependency Injection** — Microsoft.Extensions.DependencyInjection 사용
- **모듈화** — 각 기능은 독립 모듈로 분리, 인터페이스 기반 느슨한 결합
- **async/await** — UI 블로킹 금지, 모든 무거운 작업은 비동기 처리

### 네이밍 규칙
```
Interface:   IVideoDecoder, IStreamCopyRecorder, ISubtitleParser
ViewModel:   PlayerViewModel, SubtitleEditorViewModel, FFmpegExplorerViewModel
Service:     StreamCopyService, SubtitleFileService
Model:       VideoFrame, SubtitleCue, MediaInfo
Command:     RelayCommand, AsyncRelayCommand (CommunityToolkit.Mvvm)
View:        PlayerPage.xaml, SubtitleEditorPage.xaml
```

### 금지 사항
- ❌ Code-behind에 비즈니스 로직 작성 금지
- ❌ `Thread.Sleep()` 사용 금지 → `Task.Delay()` 사용
- ❌ `MessageBox.Show()` 직접 호출 금지 → `IDialogService` 사용
- ❌ FFmpeg GPL 빌드 사용 금지 → **반드시 LGPL 빌드** 사용
- ❌ ffmpeg.autogen unsafe 코드 직접 노출 금지 → 래퍼 클래스로 격리
- ❌ `ffmpeg.exe` 외부 프로세스 호출 금지 → ffmpeg.autogen API 직접 사용
- ❌ x264 / x265 등 GPL 코덱 링킹 금지 → Stream Copy 또는 LGPL 내장 인코더만 사용

---

## 📦 NuGet 패키지

### fflux.Core
```xml
<PackageReference Include="FFmpeg.AutoGen" Version="8.1.0" />
<PackageReference Include="CommunityToolkit.HighPerformance" Version="8.*" />
<PackageReference Include="CommunityToolkit.Diagnostics" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

### fflux.UI
```xml
<PackageReference Include="WPF-UI" Version="4.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
<PackageReference Include="Microsoft.Extensions.Hosting" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" />  <!-- 실시간 비트레이트 차트 -->
```

### Private 모듈 (별도 관리)
```xml
<!-- fflux.Misb -->
<PackageReference Include="Microsoft.Web.WebView2" />             <!-- 지도 오버레이 -->

<!-- fflux.AiSubtitle -->
<PackageReference Include="OpenAI" />                             <!-- Whisper, GPT API -->
<PackageReference Include="NAudio" />                             <!-- 오디오 추출 -->
<PackageReference Include="Microsoft.Data.Sqlite" />             <!-- 번역 캐싱 -->
```

---

## 🔐 환경 변수 / 설정

```json
// appsettings.local.json (절대 커밋 금지)
{
  "FFmpeg": {
    "BinaryPath": "F:/ffmpeg/bin",
    "Build": "LGPL"
  }
}
```

`.gitignore` 필수 포함:
```
appsettings.local.json
*.user
.vs/
bin/
obj/
```

---

## 🚀 개발 단계 (Phase)

| Phase | 범위 | 기능 | 상태 |
|-------|------|------|------|
| **Phase 0** | Public | 솔루션 구조 세팅, DI 기반 설정 | ✅ 완료 |
| **Phase 1** | Public | WPF-UI 기반 메인 UI 셸 구성 | ✅ 완료 |
| **Phase 2** | Public | fflux.Core — FFmpeg 핵심 엔진 (초기화, 미디어 리더, 비디오/오디오 디코더) | ✅ 완료 |
| **Phase 3** | Public | 기본 비디오 플레이어 (디코딩 → WriteableBitmap 렌더링) | ✅ 완료 |
| **Phase 4** | Public | 오디오 출력 (AudioDecoder + NAudio/WASAPI 렌더링) | ✅ 완료 |
| **Phase 5** | Public | 플레이어 컨트롤 (탐색, 재생속도, 볼륨) ✅ 완료 |
| **Phase 6** | Public | 미디어 정보 사이드패널 (Player 내 On/Off — 코덱·해상도·스트림 정보 표시) | ✅ 완료 |
| **Phase 7** | Public | 구간 녹화 — Stream Copy (재인코딩 없이 선택 구간 추출, LGPL 안전) | ✅ 완료 |
| **Phase 8** | Public | 자막 편집기 (SRT/ASS 파일 읽기·타임스탬프·텍스트 편집·저장, 재생 연동) | ✅ 완료 |
| **Phase 9** | Public | FFmpeg 옵션 GUI 빌더 + 커맨드 생성기 | ✅ 완료 |
| **Phase 10** | Public | 배포 준비 (자동 업데이트, README, 라이선스) | 🔲 예정 |
| **Phase M** | Private | MISB KLV 파싱 (별도 저장소) | ✅ 완료 |
| **Phase A** | Private | AI 자막 생성 → 자막 편집기 연동 (별도 저장소) | ✅ 완료 |

## 🏗️ 핵심 설계 결정

### ffmpeg.autogen 8.1.0 주요 API 특이사항
- `sws_scale` → `byte*[]` / `int[]` 관리 배열 (stackalloc 불가)
- `byte_ptrArray8` / `int_array8` 인덱서 → `uint` 인자 필요
- `SWS_BILINEAR` 상수 미노출 → `const int SWS_BILINEAR = 2` 직접 정의
- `unsafe class` 안의 `async` 메서드 → CS4004 에러. nint 핸들 패턴으로 분리 필요
- `av_frame_free` / `av_packet_free` → 이중 포인터 `**` 전달

---

## 🧪 테스트 전략

- **단위 테스트**: xUnit + Moq + FluentAssertions
- **샘플 파일**: `tests/TestAssets/` 에 소형 테스트용 영상 포함
- **Core 테스트**: ffmpeg.autogen 래퍼 클래스 단위 테스트 (가드 조건, 상태 전이)
- **UI 테스트**: ViewModel 로직 단위 테스트 (View 제외)
- **FFmpeg 조건부 테스트**: `IsFFmpegAvailable()` 헬퍼로 FFmpeg DLL 미설치 환경에서 스킵

---

## 💰 수익화 전략

```
무료 (Public 오픈소스 — fflux)
├── 기본 비디오 플레이어 (디코딩, 렌더링, 오디오, 컨트롤)
├── 실시간 비트레이트 차트
├── 미디어 정보 사이드패널 (코덱·스트림·해상도 등)
├── 구간 녹화 — Stream Copy (무손실, 빠름)
├── 자막 편집기 (SRT/ASS 읽기·편집·저장)
└── FFmpeg 커맨드 빌더

Pro (Private 확장 모듈 — 라이선스 판매)
├── MISB KLV 파싱 + 지도 오버레이  (fflux.Misb)
└── AI 자막 생성 + 자막 편집기 연동  (fflux.AiSubtitle)
```

---

## 📋 커밋 컨벤션

```
feat:     새 기능 추가
fix:      버그 수정
refactor: 코드 리팩토링
docs:     문서 수정
test:     테스트 추가/수정
chore:    빌드, 설정 변경

예시:
  feat(core): AVFormatContext Stream Copy 녹화 구현
  feat(player): 실시간 비트레이트 롤링 차트 연동
  feat(subtitle): SRT 파서 및 편집기 DataGrid 구현
  feat(player): 미디어 정보 슬라이딩 사이드패널 구현
```

---

## 🤖 Claude에게 작업 요청 시 참고사항

- 항상 **WPF MVVM 패턴** 기준으로 코드 작성 (CommunityToolkit.Mvvm)
- UI는 **WPF-UI (Wpf.Ui)** 컨트롤 우선 사용 (FluentWindow, NavigationView 등)
- ffmpeg.autogen unsafe 코드는 **래퍼 클래스로 격리**, 인터페이스로 노출
- `unsafe class`를 사용하면 async 메서드에서 CS4004 발생 → **nint 핸들 패턴** 사용
- UI 업데이트는 `ObservableProperty` 또는 `Application.Current.Dispatcher.InvokeAsync()` 사용
- 에러 처리는 `IDialogService`로 사용자 친화적 메시지 표시
- **녹화는 항상 Stream Copy** — x264/x265 등 GPL 코덱 링킹 금지
- **외부 ffmpeg.exe 호출 금지** — ffmpeg.autogen API 직접 사용
- Private 모듈(MISB, AI)은 이 프로젝트에 포함하지 않음 — 인터페이스 확장점만 남겨둘 것
- 각 Phase 작업 전 `docs/phase-prompts.md` 의 해당 프롬프트 참고
