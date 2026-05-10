# CLAUDE.md — fflux WPF Video Player Project

## 📌 프로젝트 개요

ffmpeg.autogen 기반의 WPF 비디오 플레이어입니다.
개발자용 고급 기능을 제공하며, 확장 모듈(MISB, AI 자막)은 별도 Private 프로젝트로 분리하여 관리합니다.

- **베이스 기술**: WPF (.NET 10.0), ffmpeg.autogen
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
│   ├── Abstractions/                   ← 인터페이스 정의
│   ├── Decoders/                       ← 디코딩 관련
│   ├── Encoders/                       ← 인코딩 관련
│   ├── Demuxers/                       ← 컨테이너 파싱
│   ├── Filters/                        ← FFmpeg 필터 그래프
│   ├── Models/                         ← 공유 모델 (StreamInfo 등)
│   └── Helpers/                        ← FFmpeg 유틸리티
│
├── fflux.UI/                           ← WPF UI 레이어 (Public)
│   ├── App.xaml
│   ├── Modules/
│   │   ├── Player/                     ← 기본 플레이어 UI
│   │   ├── FFmpegExplorer/             ← FFmpeg 옵션 GUI 빌더
│   │   └── VideoAnalyzer/              ← 영상 분석 도구
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
Interface:   IVideoPlayer, IVideoDecoder, IFFmpegCommandService
ViewModel:   PlayerViewModel, FFmpegExplorerViewModel
Service:     FFmpegCommandService, StreamAnalysisService
Model:       StreamInfoModel, VideoMetadataModel
Command:     RelayCommand, AsyncRelayCommand (CommunityToolkit.Mvvm)
View:        PlayerView.xaml, FFmpegExplorerView.xaml
```

### 금지 사항
- ❌ Code-behind에 비즈니스 로직 작성 금지
- ❌ `Thread.Sleep()` 사용 금지 → `Task.Delay()` 사용
- ❌ `MessageBox.Show()` 직접 호출 금지 → `IDialogService` 사용
- ❌ FFmpeg GPL 빌드 사용 금지 → **반드시 LGPL 빌드** 사용
- ❌ ffmpeg.autogen unsafe 코드 직접 노출 금지 → 래퍼 클래스로 격리

---

## 📦 NuGet 패키지

### fflux.Core
```xml
<PackageReference Include="FFmpeg.AutoGen" Version="8.*" />
<PackageReference Include="CommunityToolkit.HighPerformance" Version="8.*" />
<PackageReference Include="CommunityToolkit.Diagnostics" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
```

### fflux.UI
```xml
<PackageReference Include="WPF-UI" Version="4.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
<PackageReference Include="Microsoft.Extensions.Hosting" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" />  <!-- 차트 (VideoAnalyzer) -->
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
| **Phase 0** | Public | 솔루션 구조 세팅, DI 기반 설정 | 🔲 예정 |
| **Phase 1** | Public | WPF-UI 기반 메인 UI 셸 구성 | 🔲 예정 |
| **Phase 2** | Public | fflux.Core — ffmpeg.autogen 핵심 엔진 구현 | 🔲 예정 |
| **Phase 3** | Public | 기본 비디오 플레이어 구현 (디코딩 → 렌더링) | 🔲 예정 |
| **Phase 4** | Public | 오디오 출력 구현 | 🔲 예정 |
| **Phase 5** | Public | 플레이어 컨트롤 (탐색, 속도, 볼륨) | 🔲 예정 |
| **Phase 6** | Public | FFmpeg 옵션 GUI 빌더 + 커맨드 생성기 | 🔲 예정 |
| **Phase 7** | Public | 스트림 정보 분석기 + 배치 인코딩 큐 | 🔲 예정 |
| **Phase 8** | Public | 영상 분석 도구 (장면 감지, 품질 분석) | 🔲 예정 |
| **Phase 9** | Public | 배포 준비 (업데이트, README, 라이선스) | 🔲 예정 |
| **Phase M** | Private | MISB KLV 파싱 + 지도 오버레이 (별도 저장소) | 🔲 별도 |
| **Phase A** | Private | AI 자막 생성 + 실시간 번역 (별도 저장소) | 🔲 별도 |

---

## 🧪 테스트 전략

- **단위 테스트**: xUnit + Moq
- **샘플 파일**: `tests/TestAssets/` 에 소형 테스트용 영상 포함
- **Core 테스트**: ffmpeg.autogen 래퍼 클래스 단위 테스트
- **UI 테스트**: ViewModel 로직 단위 테스트 (View 제외)

---

## 💰 수익화 전략

```
무료 (Public 오픈소스 — fflux)
├── 기본 비디오 플레이어
├── FFmpeg 커맨드 빌더
├── 스트림 정보 분석기
├── 장면 감지 / 영상 품질 분석
└── 배치 인코딩 큐

Pro (Private 확장 모듈 — 라이선스 판매)
├── MISB KLV 파싱 + 지도 오버레이  (fflux.Misb)
└── AI 자막 생성 + 실시간 번역     (fflux.AiSubtitle)
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
  feat(ui): 메인 셸 NavigationView 레이아웃 구성
  feat(core): AVFormatContext 기반 스트림 정보 추출 구현
  feat(player): D3D11 렌더러 WriteableBitmap 연동
```

---

## 🤖 Claude에게 작업 요청 시 참고사항

- 항상 **WPF MVVM 패턴** 기준으로 코드 작성 (CommunityToolkit.Mvvm)
- UI는 **WPF-UI (Wpf.Ui)** 컨트롤 우선 사용 (FluentWindow, NavigationView 등)
- ffmpeg.autogen unsafe 코드는 **래퍼 클래스로 격리**, 인터페이스로 노출
- UI 업데이트는 `ObservableProperty` 또는 `Application.Current.Dispatcher.InvokeAsync()` 사용
- 에러 처리는 `IDialogService`로 사용자 친화적 메시지 표시
- Private 모듈(MISB, AI)은 이 프로젝트에 포함하지 않음 — 인터페이스 확장점만 남겨둘 것
- 각 Phase 작업 전 `docs/phase-prompts.md` 의 해당 프롬프트 참고
