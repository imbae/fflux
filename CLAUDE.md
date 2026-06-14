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
│   │   ├── FFmpegExplorer/             ← FFmpeg 옵션 GUI 빌더 + 커맨드 생성기
│   │   └── ScreenRecorder/             ← 화면 녹화 모듈 (Free + Pro)
│   │       ├── Core/                   ← 녹화 핵심 서비스 (Free)
│   │       ├── Drawing/                ← 그리기 오버레이 도구 (Pro)
│   │       └── Annotations/            ← 주석 타임라인 (Pro, fflux 전용)
│   │
│   └── Shared/
│       ├── Controls/                   ← 공용 CustomControl
│       ├── Converters/                 ← WPF ValueConverter
│       ├── Services/                   ← UI 서비스 (DialogService, IFeatureGate 등)
│       └── Helpers/
│
├── tests/
│   ├── fflux.Core.Tests/
│   └── fflux.UI.Tests/
│
└── docs/
    ├── CLAUDE.md                       ← 이 파일
    ├── PROGRESS.md                     ← 기능별 세부 구현 진행 상황
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
- **Feature Gate** — Pro 기능은 `IFeatureGate`로 접근 제어, 미인증 시 업그레이드 유도 다이얼로그 표시

### 네이밍 규칙
```
Interface:   IVideoDecoder, IStreamCopyRecorder, ISubtitleParser, IScreenCaptureService
ViewModel:   PlayerViewModel, SubtitleEditorViewModel, FFmpegExplorerViewModel, ScreenRecorderViewModel
Service:     StreamCopyService, SubtitleFileService, ScreenCaptureService, AudioCaptureService
Model:       VideoFrame, SubtitleCue, MediaInfo, RecordingSettings, AnnotationTrack
Command:     RelayCommand, AsyncRelayCommand (CommunityToolkit.Mvvm)
View:        PlayerPage.xaml, SubtitleEditorPage.xaml, ScreenRecorderPage.xaml
```

### 금지 사항
- ❌ Code-behind에 비즈니스 로직 작성 금지
- ❌ `Thread.Sleep()` 사용 금지 → `Task.Delay()` 사용
- ❌ `MessageBox.Show()` 직접 호출 금지 → `IDialogService` 사용
- ❌ FFmpeg GPL 빌드 사용 금지 → **반드시 LGPL 빌드** 사용
- ❌ ffmpeg.autogen unsafe 코드 직접 노출 금지 → 래퍼 클래스로 격리
- ❌ `ffmpeg.exe` 외부 프로세스 호출 금지 → ffmpeg.autogen API 직접 사용
- ❌ x264 / x265 등 GPL 코덱 링킹 금지 → Stream Copy 또는 LGPL 내장 인코더만 사용
- ❌ Pro 기능을 `IFeatureGate` 없이 직접 노출 금지

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
<!-- ScreenRecorder -->
<PackageReference Include="NAudio" Version="2.*" />               <!-- WASAPI 오디오 캡처 -->
<PackageReference Include="System.Text.Json" />                   <!-- 주석 JSON 직렬화 -->
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
| **Phase 5** | Public | 플레이어 컨트롤 (탐색, 재생속도, 볼륨) | ✅ 완료 |
| **Phase 6** | Public | 미디어 정보 사이드패널 (Player 내 On/Off — 코덱·해상도·스트림 정보 표시) | ✅ 완료 |
| **Phase 7** | Public | 구간 녹화 — Stream Copy (재인코딩 없이 선택 구간 추출, LGPL 안전) | ✅ 완료 |
| **Phase 8** | Public | 자막 편집기 (SRT/ASS 파일 읽기·타임스탬프·텍스트 편집·저장, 재생 연동) | ✅ 완료 |
| **Phase 9** | Public | FFmpeg 옵션 GUI 빌더 + 커맨드 생성기 | ✅ 완료 |
| **Phase 10** | Public | 화면 녹화 — 핵심 기능 (캡처·오디오·컨트롤·핫키·플로팅바) | 🔲 예정 |
| **Phase 11** | Public | 화면 녹화 — Pro 기능 (그리기 오버레이 + 주석 타임라인) | 🔲 예정 |
| **Phase 12** | Public | 배포 준비 (자동 업데이트, README, 라이선스) | 🔲 예정 |
| **Phase M** | Private | MISB KLV 파싱 (별도 저장소) | ✅ 완료 |
| **Phase A** | Private | AI 자막 생성 → 자막 편집기 연동 (별도 저장소) | ✅ 완료 |

---

## 📹 화면 녹화 모듈 아키텍처 (Phase 10~11)

### UI 진입점
- fflux.UI NavigationView에 **"Screen Recorder"** 서브메뉴 항목 추가
- `ScreenRecorderPage.xaml` — 설정 및 컨트롤 메인 뷰
- `RecorderOverlayWindow.xaml` — 녹화 중 플로팅 컨트롤바 (별도 Window, 항상 위)
- `DrawingOverlayWindow.xaml` — 투명 그리기 레이어 (Pro, 별도 Window)

### 모듈 구조
```
fflux.UI/Modules/ScreenRecorder/
│
├── ScreenRecorderPage.xaml             ← 메인 설정/컨트롤 뷰
├── ScreenRecorderViewModel.cs
│
├── Core/                               ← [Free] 핵심 녹화 기능
│   ├── Views/
│   │   └── RecorderOverlayWindow.xaml  ← 플로팅 컨트롤바
│   ├── Services/
│   │   ├── IScreenCaptureService.cs    ← DXGI Desktop Duplication API
│   │   ├── ScreenCaptureService.cs
│   │   ├── IAudioCaptureService.cs     ← WASAPI loopback + 마이크
│   │   ├── AudioCaptureService.cs
│   │   ├── IRecordingSessionService.cs ← 세션 생명주기 관리
│   │   ├── RecordingSessionService.cs
│   │   ├── IGlobalHotkeyService.cs     ← 글로벌 핫키 등록/해제
│   │   └── GlobalHotkeyService.cs
│   └── Models/
│       ├── RecordingSettings.cs        ← FPS, 해상도, 출력경로, 오디오 설정
│       └── RecordingSession.cs         ← 세션 상태 (진행시간, 파일경로 등)
│
├── Drawing/                            ← [Pro] 그리기 오버레이
│   ├── Views/
│   │   ├── DrawingOverlayWindow.xaml   ← 투명 최상위 Window (클릭 통과 토글)
│   │   └── DrawingToolbarView.xaml     ← 펜/도형/색상/굵기 선택 툴바
│   ├── DrawingOverlayViewModel.cs
│   ├── Tools/
│   │   ├── IDrawingTool.cs
│   │   ├── PenTool.cs                  ← InkCanvas 기반 자유 펜
│   │   ├── ShapeTool.cs                ← 사각형, 원, 화살표, 선
│   │   ├── TextTool.cs                 ← 텍스트 삽입
│   │   ├── LaserPointerTool.cs         ← [Pro] 레이저 포인터 모드
│   │   ├── SpotlightTool.cs            ← [Pro] 스포트라이트/줌 펜
│   │   └── DisappearingInkTool.cs      ← [Pro] 자동 사라지는 잉크
│   └── Models/
│       ├── DrawingStroke.cs            ← 펜 획 데이터
│       └── DrawingShape.cs             ← 도형 데이터
│
└── Annotations/                        ← [Pro] 주석 타임라인 (fflux 전용)
    ├── AnnotationTrackViewModel.cs
    ├── Services/
    │   ├── IAnnotationService.cs       ← 주석 저장/불러오기
    │   ├── AnnotationService.cs        ← JSON 사이드카 파일 관리
    │   └── AnnotationExportService.cs  ← 번인 내보내기 (ffmpeg.autogen)
    └── Models/
        ├── AnnotationTrack.cs          ← 전체 주석 트랙 컨테이너
        ├── AnnotationEntry.cs          ← 시간축 단일 주석 항목
        └── AnnotationSidecar.cs        ← JSON 직렬화 루트 모델
```

### 사이드카 파일 형식
```
video.mp4
video.fflux-annotations.json     ← 벡터 주석 데이터 (fflux 전용)
```
- fflux에서 영상 열기 시 동일 경로의 `.fflux-annotations.json` 자동 감지·로드
- 재생 중 해당 시점 주석 자동 재현 및 편집 가능
- "주석 포함 내보내기" → ffmpeg.autogen으로 프레임에 번인 후 표준 MP4 생성

### Free / Pro 기능 분리
| 기능 | 등급 |
|---|---|
| 화면 캡처 (전체/영역/윈도우) | Free |
| 오디오 녹음 (시스템+마이크) | Free |
| 녹화 컨트롤 (시작/정지/일시정지) | Free |
| 글로벌 핫키 | Free |
| 플로팅 컨트롤바 | Free |
| 녹화 후 fflux 즉시 재생 연계 | Free |
| 기본 그리기 (펜/도형/텍스트) | Free |
| 그리기 Undo/Redo | Free |
| **자동 사라지는 잉크** | **Pro** |
| **스포트라이트/줌 펜** | **Pro** |
| **레이저 포인터 모드** | **Pro** |
| **클릭/키 입력 시각화** | **Pro** |
| **그리기 타임라인 동기화** | **Pro** |
| **주석 포함 번인 내보내기** | **Pro** |

---

## 🏗️ 핵심 설계 결정

### ffmpeg.autogen 8.1.0 주요 API 특이사항
- `sws_scale` → `byte*[]` / `int[]` 관리 배열 (stackalloc 불가)
- `byte_ptrArray8` / `int_array8` 인덱서 → `uint` 인자 필요
- `SWS_BILINEAR` 상수 미노출 → `const int SWS_BILINEAR = 2` 직접 정의
- `unsafe class` 안의 `async` 메서드 → CS4004 에러. nint 핸들 패턴으로 분리 필요
- `av_frame_free` / `av_packet_free` → 이중 포인터 `**` 전달

### DXGI Desktop Duplication 캡처 주의사항
- `IDXGIOutputDuplication` — GPU 메모리에서 직접 프레임 획득 (GDI 대비 고성능)
- 보호된 콘텐츠(DRM) 화면은 검은 화면으로 캡처됨 (OS 정책)
- 모니터 전환/절전 시 `DXGI_ERROR_ACCESS_LOST` → Duplication 객체 재생성 필요
- 캡처 프레임 → ffmpeg.autogen `AVFrame` 변환 후 기존 인코딩 파이프라인 재사용

---

## 🧪 테스트 전략

- **단위 테스트**: xUnit + Moq + FluentAssertions
- **샘플 파일**: `tests/TestAssets/` 에 소형 테스트용 영상 포함
- **Core 테스트**: ffmpeg.autogen 래퍼 클래스 단위 테스트 (가드 조건, 상태 전이)
- **UI 테스트**: ViewModel 로직 단위 테스트 (View 제외)
- **FFmpeg 조건부 테스트**: `IsFFmpegAvailable()` 헬퍼로 FFmpeg DLL 미설치 환경에서 스킵
- **ScreenRecorder 테스트**: Mock `IScreenCaptureService` 로 실제 캡처 없이 세션 로직 테스트

---

## 💰 수익화 전략

```
무료 (Public 오픈소스 — fflux)
├── 기본 비디오 플레이어 (디코딩, 렌더링, 오디오, 컨트롤)
├── 실시간 비트레이트 차트
├── 미디어 정보 사이드패널 (코덱·스트림·해상도 등)
├── 구간 녹화 — Stream Copy (무손실, 빠름)
├── 자막 편집기 (SRT/ASS 읽기·편집·저장)
├── FFmpeg 커맨드 빌더
├── 화면 녹화 핵심 (캡처·오디오·컨트롤·핫키)
└── 기본 그리기 도구 (펜/도형/텍스트/Undo-Redo)

Pro (Private 확장 모듈 — 라이선스 판매)
├── MISB KLV 파싱 + 지도 오버레이          (fflux.Misb)
├── AI 자막 생성 + 자막 편집기 연동         (fflux.AiSubtitle)
└── 화면 녹화 Pro 도구
    ├── 자동 사라지는 잉크
    ├── 스포트라이트/줌 펜
    ├── 레이저 포인터 모드
    ├── 클릭/키 입력 시각화
    ├── 그리기 타임라인 동기화 (fflux 전용 사이드카)
    └── 주석 포함 번인 내보내기
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
  feat(recorder): DXGI 화면 캡처 서비스 구현
  feat(recorder): 그리기 오버레이 DisappearingInk 구현
  feat(recorder): 주석 타임라인 사이드카 JSON 저장 구현
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
- **Pro 기능은 반드시 `IFeatureGate.IsEnabled("feature-key")` 체크 후 노출**
- 화면 녹화 캡처는 **DXGI Desktop Duplication API** 사용 (GDI 금지)
- 각 Phase 작업 전 `docs/phase-prompts.md` 의 해당 프롬프트 참고
- 세부 구현 진행 상황은 `docs/PROGRESS.md` 참고
