# CLAUDE.md — WPF Video Player Extension Project

## 📌 프로젝트 개요

WPF 기반 비디오 플레이어를 확장하여 개발자용 고급 기능과 AI 기반 자막/번역 기능을 추가하는 프로젝트입니다.

- **베이스 기술**: WPF (.NET), ffmpeg.autogen
- **라이선스**: LGPL (FFmpeg LGPL 빌드 사용 권장)
- **목표**: 오픈소스 공개 후 Freemium 또는 라이선스 기반 수익화

---

## 🗂️ 프로젝트 구조

```
VideoPlayerPro/
├── CLAUDE.md                        ← 이 파일
├── README.md
├── LICENSE
├── VideoPlayerPro.sln
│
├── src/
│   ├── VideoPlayerPro.Core/         ← 핵심 플레이어 엔진 (ffmpeg.autogen)
│   ├── VideoPlayerPro.UI/           ← WPF UI 레이어 (MVVM)
│   │
│   ├── Modules/
│   │   ├── FFmpegExplorer/          ← Phase 1: FFmpeg 옵션 GUI
│   │   ├── MisbParser/              ← Phase 2: MISB 메타데이터 파싱
│   │   ├── AiSubtitle/              ← Phase 3: AI 자막/번역
│   │   └── VideoAnalyzer/           ← Phase 4: 영상 분석 도구
│   │
│   └── Shared/
│       ├── Models/
│       ├── Services/
│       └── Helpers/
│
├── tests/
│   ├── Core.Tests/
│   └── Module.Tests/
│
└── docs/
    ├── phase-prompts/               ← 단계별 프롬프트 모음
    └── api/
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
- Interface:     IFFmpegService, IMisbParser
- ViewModel:     FFmpegExplorerViewModel
- Service:       WhisperTranscriptionService
- Model:         KlvMetadataModel
- Command:       RelayCommand, AsyncRelayCommand (CommunityToolkit.Mvvm)
```

### 금지 사항
- ❌ Code-behind에 비즈니스 로직 작성 금지
- ❌ `Thread.Sleep()` 사용 금지 → `Task.Delay()` 사용
- ❌ `MessageBox.Show()` 직접 호출 금지 → DialogService 사용
- ❌ FFmpeg GPL 빌드 사용 금지 → **반드시 LGPL 빌드** 사용

---

## 📦 주요 NuGet 패키지

```xml
<!-- 핵심 -->
<PackageReference Include="FFmpeg.AutoGen" Version="6.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />

<!-- Phase 2: MISB -->
<PackageReference Include="BinaryKits.Utility" />  <!-- KLV 파싱 보조 -->

<!-- Phase 3: AI 자막 -->
<PackageReference Include="OpenAI" />              <!-- Whisper, GPT API -->
<PackageReference Include="NAudio" />              <!-- 오디오 추출 -->

<!-- Phase 4: 영상 분석 -->
<PackageReference Include="OpenCvSharp4.Windows" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" />  <!-- 차트 -->
```

---

## 🔐 환경 변수 / 설정

```json
// appsettings.json (절대 커밋 금지)
{
  "ApiKeys": {
    "OpenAI": "sk-...",
    "DeepL": "..."
  },
  "FFmpeg": {
    "BinaryPath": "C:/ffmpeg/bin",
    "Build": "LGPL"
  }
}
```

`.gitignore`에 반드시 포함:
```
appsettings.local.json
*.user
.vs/
bin/
obj/
```

---

## 🚀 개발 단계 (Phase)

| Phase | 기능 | 상태 |
|-------|------|------|
| Phase 1 | FFmpeg 옵션 GUI 빌더 + 커맨드 생성기 | 🔲 예정 |
| Phase 2 | MISB KLV 메타데이터 파싱 + 시각화 | 🔲 예정 |
| Phase 3 | AI 자막 생성 (Whisper) + 번역 | 🔲 예정 |
| Phase 4 | 영상 분석 도구 (장면 감지, 텔레메트리 오버레이) | 🔲 예정 |

---

## 🧪 테스트 전략

- **단위 테스트**: xUnit + Moq
- **샘플 파일**: `tests/TestAssets/` 에 소형 테스트용 영상 포함
- **MISB 테스트**: 공개 MISB 샘플 KLV 파일 활용
- **AI 테스트**: API 호출은 Mock으로 대체하여 비용 절감

---

## 💰 수익화 전략

```
무료 (오픈소스)
├── 기본 플레이어
├── FFmpeg 커맨드 빌더
└── 기본 자막 파일 생성

Pro (라이선스 판매 / Gumroad)
├── MISB 파싱 + 지도 오버레이
├── 실시간 AI 번역 자막
├── 배치 인코딩 큐
└── 영상 분석 고급 기능
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

예시: feat(misb): KLV 스트림 실시간 파싱 구현
```

---

## 🤖 Claude에게 작업 요청 시 참고사항

- 항상 **WPF MVVM 패턴** 기준으로 코드 작성
- ffmpeg.autogen API는 **unsafe 블록** 또는 래퍼 클래스로 안전하게 감쌀 것
- UI 업데이트는 항상 `Application.Current.Dispatcher.InvokeAsync()` 또는 `ObservableProperty` 사용
- 에러 처리는 사용자에게 친화적인 메시지로 변환하여 표시
- 각 Phase 작업 전 해당 `docs/phase-prompts/` 의 프롬프트 참고
