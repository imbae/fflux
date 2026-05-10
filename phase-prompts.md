# phase-prompts.md — fflux 단계별 개발 프롬프트 가이드

---

## 🔰 공통 컨텍스트 (모든 프롬프트 앞에 붙이기)

```
나는 WPF와 ffmpeg.autogen 기반의 비디오 플레이어(fflux)를 개발 중이야.
- 언어: C# / WPF
- 패턴: MVVM (CommunityToolkit.Mvvm)
- DI: Microsoft.Extensions.DependencyInjection + Microsoft.Extensions.Hosting
- UI 라이브러리: WPF-UI (Wpf.Ui) v4.*
- FFmpeg 빌드: LGPL (ffmpeg.autogen v8.*)
- .NET 버전: 10.0

코드 작성 규칙:
1. Code-behind에 비즈니스 로직 금지 — ViewModel에 작성
2. 비동기 처리 필수 (async/await), UI 블로킹 금지
3. ffmpeg.autogen unsafe 코드는 반드시 래퍼 클래스로 격리
4. UI 업데이트는 ObservableProperty 또는 Dispatcher 사용
5. 에러 처리는 IDialogService로 사용자 친화적 메시지 표시
6. MISB, AI 관련 기능은 이 프로젝트에 포함하지 않음
```

---

## 📌 Phase 0 — 솔루션 기반 세팅

### 프롬프트 0-1: 솔루션 및 프로젝트 구조 생성
```
위 공통 컨텍스트를 참고해서,
fflux 솔루션의 초기 프로젝트 구조를 설계해줘.

프로젝트 구성:
- fflux.Core      — 클래스 라이브러리 (ffmpeg.autogen 엔진)
- fflux.UI        — WPF 애플리케이션 (WPF-UI 기반)
- fflux.Core.Tests — xUnit 테스트 프로젝트

결과물:
1. 각 .csproj 파일 내용 (NuGet 패키지 포함)
2. fflux.slnx (또는 .sln) 솔루션 파일
3. App.xaml / App.xaml.cs — Microsoft.Extensions.Hosting 기반 DI 세팅
4. fflux.Core의 최상위 폴더 구조 (Abstractions, Decoders, Models 등)
5. GlobalUsings.cs 세팅 (각 프로젝트)
```

### 프롬프트 0-2: DI 컨테이너 및 서비스 등록 구조
```
Microsoft.Extensions.Hosting 기반으로
WPF 앱의 DI 컨테이너와 서비스 등록 구조를 만들어줘.

요구사항:
- App.xaml.cs에서 IHost 생성 및 수명 관리
- IServiceCollection에 Core / UI 서비스 분리 등록
- ViewModelLocator 대신 DI로 ViewModel 주입
- Window / Page의 ViewModel을 DI로 resolve하는 패턴

결과물:
- App.xaml.cs (IHost 기반)
- ServiceCollectionExtensions (Core 서비스 등록)
- ServiceCollectionExtensions (UI 서비스 등록)
```

---

## 📌 Phase 1 — WPF-UI 메인 셸 UI 구성

### 프롬프트 1-1: 메인 윈도우 셸 레이아웃
```
WPF-UI(Wpf.Ui) v4를 사용해서 메인 윈도우 셸을 구성해줘.

레이아웃 요구사항:
- FluentWindow 기반 (타이틀바 커스텀)
- 좌측 NavigationView (사이드바 방식)
- 네비게이션 항목:
    - 🎬 Player          (기본 플레이어)
    - 🛠️ FFmpeg Explorer (커맨드 빌더)
    - 📊 Video Analyzer  (영상 분석)
    - ⚙️ Settings        (설정)
- 우측 콘텐츠 영역 (Frame / ContentPresenter)
- 하단 상태바 (StatusBar) — 현재 파일명, 재생 상태

결과물:
- MainWindow.xaml / MainWindow.xaml.cs
- MainWindowViewModel.cs
- NavigationService (페이지 전환 추상화)
- 각 페이지 Shell (PlayerPage, FFmpegExplorerPage 등) — 빈 껍데기
```

### 프롬프트 1-2: 공통 UI 컴포넌트 및 스타일 세팅
```
WPF-UI 테마 기반으로 공통 UI 리소스를 세팅해줘.

요구사항:
- Light / Dark 테마 전환 지원 (ApplicationThemeManager)
- 공통 색상 팔레트 (ResourceDictionary)
- 자주 쓰는 공용 컨트롤:
    - LoadingOverlay (진행률 + 취소 버튼)
    - EmptyStatePlaceholder (파일 없을 때 안내)
    - TimecodeTextBox (00:00:00.000 형식 입력)
- IDialogService 구현 (ContentDialog 기반)

결과물:
- App.xaml 리소스 딕셔너리 구성
- Themes/ 폴더 구조
- 공용 컨트롤 XAML + Code-behind (최소한)
- DialogService 구현체
```

### 프롬프트 1-3: 설정 페이지 UI
```
앱 설정 페이지를 만들어줘.

설정 항목:
- FFmpeg 바이너리 경로 설정 (FolderPicker)
- 테마 선택 (Light / Dark / System)
- 기본 출력 폴더 설정
- 언어 설정 (한국어 / English) — 추후 적용 예정 placeholder

설정 저장 방식:
- appsettings.local.json (파일 기반 영속)
- ISettingsService 인터페이스 추상화

결과물:
- SettingsPage.xaml / SettingsViewModel.cs
- ISettingsService + JsonSettingsService 구현
- AppSettings 모델 클래스
```

---

## 📌 Phase 2 — fflux.Core: ffmpeg.autogen 핵심 엔진

### 프롬프트 2-1: FFmpeg 초기화 및 바이너리 로딩
```
ffmpeg.autogen 라이브러리의 FFmpeg 바이너리 로딩 및
초기화 래퍼를 구현해줘.

요구사항:
- ffmpeg.autogen의 FFmpegLoader 또는 직접 NativeLibrary.Load 방식
- LGPL 빌드 필수 (avcodec, avformat, avutil, swscale, swresample)
- 바이너리 경로를 ISettingsService에서 읽어옴
- 로딩 실패 시 명확한 예외 메시지
- av_log 콜백 → .NET ILogger로 연결

결과물:
- FFmpegLoader 클래스
- FFmpegLogBridge (av_log → ILogger)
- IFFmpegInitializer 인터페이스 + 구현
```

### 프롬프트 2-2: 미디어 파일 열기 및 스트림 정보 추출
```
ffmpeg.autogen으로 미디어 파일을 열고
스트림 정보를 추출하는 서비스를 구현해줘.

구현 내용:
- AVFormatContext로 파일 열기 (avformat_open_input)
- 스트림 정보 추출 (avformat_find_stream_info)
- 비디오/오디오/자막 스트림 목록 파싱
- StreamInfoModel 매핑:
    - 컨테이너: 포맷명, 비트레이트, 재생시간
    - 비디오: 코덱, 해상도, fps, 픽셀포맷, bitrate
    - 오디오: 코덱, 샘플레이트, 채널수, bitrate
    - 자막: 코덱, 언어 태그
- unsafe 코드는 MediaFileReader 내부로 격리

결과물:
- IMediaFileReader 인터페이스
- MediaFileReader 구현 (unsafe 래퍼)
- StreamInfoModel / VideoStreamInfo / AudioStreamInfo / SubtitleStreamInfo
- 단위 테스트 (xUnit)
```

### 프롬프트 2-3: 비디오 디코더 구현
```
ffmpeg.autogen으로 비디오 프레임을 디코딩하는
디코더 클래스를 구현해줘.

구현 내용:
- AVCodecContext로 비디오 코덱 열기
- av_read_frame → avcodec_send_packet → avcodec_receive_frame 파이프라인
- 디코딩된 AVFrame → VideoFrame 모델로 변환
- 픽셀 포맷 변환 (sws_scale → BGRA32, WPF 렌더링용)
- 시크 지원 (av_seek_frame)
- IAsyncEnumerable<VideoFrame> 스트리밍 방식

결과물:
- IVideoDecoder 인터페이스
- VideoDecoder 구현
- VideoFrame 모델 (byte[], width, height, pts, duration)
- PixelFormatConverter 헬퍼 (SwsContext 래퍼)
- 단위 테스트
```

### 프롬프트 2-4: 오디오 디코더 + PCM 변환
```
오디오 스트림을 디코딩하고 PCM으로 변환하는
서비스를 구현해줘.

구현 내용:
- AVCodecContext로 오디오 코덱 열기
- avcodec_receive_frame → AudioFrame 변환
- SwrContext로 PCM 변환 (float32, stereo, 48kHz — 재생용)
- AudioFrame 모델 (float[], sampleRate, channels, pts)
- IAsyncEnumerable<AudioFrame> 스트리밍 방식

결과물:
- IAudioDecoder 인터페이스
- AudioDecoder 구현
- AudioFrame 모델
- SampleFormatConverter 헬퍼 (SwrContext 래퍼)
```

---

## 📌 Phase 3 — 기본 비디오 플레이어 구현

### 프롬프트 3-1: 비디오 렌더러 (WriteableBitmap)
```
디코딩된 VideoFrame을 WPF Image 컨트롤에
렌더링하는 비디오 렌더러를 구현해줘.

구현 내용:
- WriteableBitmap 기반 렌더링 (BGRA32)
- Dispatcher.InvokeAsync로 UI 스레드에서 픽셀 업데이트
- 렌더링 루프: CompositionTarget.Rendering 또는 DispatcherTimer
- 프레임 드롭 처리 (PTS 기반 동기화)
- VideoFrame 버퍼링 (Channel<VideoFrame> 또는 BlockingCollection)

결과물:
- IVideoRenderer 인터페이스
- WriteableBitmapRenderer 구현
- VideoRenderControl.xaml (Image + 오버레이 Grid)
```

### 프롬프트 3-2: 오디오 출력 (NAudio)
```
디코딩된 PCM AudioFrame을 NAudio로 출력하는
오디오 재생 서비스를 구현해줘.

구현 내용:
- NAudio WaveOutEvent 또는 DirectSoundOut 사용
- IWaveProvider 구현 (AudioFrame 큐 → PCM 스트림)
- 볼륨 제어 (0.0 ~ 1.0)
- 재생/일시정지/정지 상태 관리
- 오디오-비디오 동기화 (AV Sync): PTS 기반

결과물:
- IAudioOutput 인터페이스
- NAudioOutput 구현
- AudioFrameProvider (IWaveProvider 구현체)
```

### 프롬프트 3-3: 미디어 플레이어 컨트롤러 (통합)
```
VideoDecoder + AudioDecoder + VideoRenderer + AudioOutput을
통합하는 MediaPlayer 컨트롤러를 만들어줘.

기능:
- Open(filePath) — 파일 열기
- Play / Pause / Stop
- Seek(TimeSpan) — 특정 위치로 탐색
- PlaybackSpeed (0.5x ~ 2.0x)
- 상태 관리: PlaybackState enum (Stopped/Playing/Paused/Buffering)
- 이벤트: PositionChanged, PlaybackEnded, ErrorOccurred

결과물:
- IMediaPlayer 인터페이스
- MediaPlayer 구현 (내부적으로 디코딩 Task 관리)
- PlaybackState enum
- PlayerViewModel.cs (UI 바인딩)
- PlayerPage.xaml (컨트롤 UI 포함)
```

---

## 📌 Phase 4 — 플레이어 UI 완성

### 프롬프트 4-1: 플레이어 컨트롤 바
```
영상 플레이어 하단 컨트롤 바를 만들어줘.

포함 요소:
- 재생/일시정지 버튼 (토글)
- 정지 버튼
- 시크바 (Slider — 드래그 시 썸네일 미리보기)
- 현재 시간 / 전체 시간 표시 (00:00:00 / 00:00:00)
- 볼륨 슬라이더 + 음소거 버튼
- 재생 속도 선택 (ComboBox: 0.5x, 1x, 1.5x, 2x)
- 전체화면 토글 버튼
- 파일 열기 버튼

WPF-UI 스타일로 세련되게 만들어줘.
시크바 hover 시 시간 툴팁 표시도 포함해줘.
```

### 프롬프트 4-2: 자막 파일 로드 및 표시
```
외부 자막 파일(SRT, VTT)을 로드하여 영상 위에
오버레이로 표시하는 기능을 만들어줘.

구현 내용:
- SRT / VTT 파서 (재생 위치와 동기화)
- 자막 오버레이 (TextBlock, 반투명 배경)
- 자막 표시/숨김 토글
- 자막 파일 드래그앤드롭 로드
- 자막 스타일 설정 (폰트 크기, 색상)

결과물:
- ISubtitleParser 인터페이스
- SrtParser / VttParser 구현
- SubtitleEntry 모델
- SubtitleOverlayControl.xaml
```

---

## 📌 Phase 5 — FFmpeg 옵션 GUI 빌더

### 프롬프트 5-1: FFmpeg 커맨드 빌더 UI
```
FFmpeg 옵션을 GUI로 설정하고 커맨드라인을 자동 생성하는
WPF 모듈을 만들어줘.

기능 요구사항:
- 입력/출력 파일 선택 (FileDialog)
- 옵션 카테고리별 TabControl:
    - 비디오: 코덱, 비트레이트, fps, 해상도, CRF
    - 오디오: 코덱, 비트레이트, 샘플레이트, 채널
    - 필터: -vf / -af 텍스트 직접 입력
    - 고급: 자유 텍스트 옵션
- 하단 생성된 FFmpeg 커맨드 실시간 미리보기 (읽기 전용)
- 클립보드 복사 버튼
- 실행 버튼 (비동기, 진행률 + 로그 표시)
- 실행 취소 버튼 (CancellationToken)

결과물:
- IFFmpegCommandService 인터페이스 + 구현
- FFmpegCommandBuilderViewModel
- FFmpegExplorerPage.xaml
```

### 프롬프트 5-2: 스트림 정보 분석기 UI
```
현재 열린 파일의 스트림 정보를 표시하는 패널을 만들어줘.
(Phase 2-2의 IMediaFileReader 사용)

표시 항목:
- 컨테이너: 포맷, 비트레이트, 재생시간, 파일 크기
- 비디오 스트림: 코덱, 해상도, fps, 픽셀 포맷, 비트레이트
- 오디오 스트림: 코덱, 샘플레이트, 채널, 비트레이트
- 자막 스트림 목록

UI:
- TreeView 또는 GroupBox 카드 방식
- JSON으로 내보내기 버튼
- 파일 드래그앤드롭으로 빠른 분석 지원

결과물:
- StreamInfoViewModel
- StreamInfoPanel.xaml (재사용 가능한 UserControl)
```

### 프롬프트 5-3: 배치 인코딩 큐
```
여러 파일을 순서대로 인코딩하는 배치 큐 매니저를 만들어줘.

기능:
- 파일 목록 드래그앤드롭 추가
- 각 파일별 FFmpeg 옵션 설정 (프리셋 선택 또는 커스텀)
- 큐 순서 변경 (드래그 또는 위/아래 버튼)
- 전체 진행률 + 현재 파일 진행률 표시
- 완료/실패/대기 상태 아이콘
- 실패 시 에러 로그 확인 (팝업)
- 완료 후 알림 (토스트)

결과물:
- BatchEncodeQueue (ObservableCollection 기반)
- BatchEncodeViewModel
- BatchEncodeItem 모델
- BatchEncodePage.xaml
```

---

## 📌 Phase 6 — 영상 분석 도구 (VideoAnalyzer)

### 프롬프트 6-1: 장면 전환 감지 + 챕터 생성
```
영상의 장면 전환을 자동으로 감지하고
챕터 마커를 생성하는 기능을 만들어줘.

구현 방법:
- Phase 2의 VideoDecoder로 프레임 단위 디코딩
- 연속 프레임 간 히스토그램 차이 계산
- 임계값 초과 시 장면 전환으로 판단
- BackgroundService 패턴으로 백그라운드 처리
- 감지된 장면 목록: 썸네일 + 타임코드
- 챕터 이름 편집 후 FFmpeg 메타데이터 형식으로 내보내기

결과물:
- ISceneDetectionService 인터페이스 + 구현
- SceneInfo 모델
- SceneDetectionViewModel
- SceneDetectionPage.xaml
```

### 프롬프트 6-2: 비트레이트 / 품질 분석 그래프
```
영상의 품질 지표를 분석하고 그래프로 시각화하는
도구를 만들어줘. (LiveChartsCore 사용)

분석 항목:
- 시간대별 비트레이트 변화 (라인 차트)
- 프레임별 크기 분포 (히스토그램)
- 드롭 프레임 위치 표시
- (선택) PSNR / SSIM — ffmpeg -lavfi 필터 활용

UI:
- LiveChartsCore 차트 컴포넌트
- 품질 저하 구간 빨간색 하이라이트
- 분석 결과 CSV 내보내기

결과물:
- IVideoQualityAnalyzer 인터페이스 + 구현
- QualityAnalysisViewModel
- QualityAnalysisPage.xaml
```

---

## 📌 Phase 7 — 배포 준비

### 프롬프트 7-1: 자동 업데이트 시스템
```
GitHub Releases 기반의 자동 업데이트 시스템을 구현해줘.

요구사항:
- GitHub Releases API에서 최신 버전 확인 (Octokit.net)
- 현재 버전과 SemVer 비교
- 업데이트 있으면 변경로그 + 다운로드 다이얼로그
- 백그라운드 다운로드 + 진행률 표시
- SHA256 검증 후 설치 및 앱 재시작

결과물:
- IUpdateService 인터페이스 + GitHubUpdateService 구현
- UpdateViewModel
- UpdateCheckDialog.xaml
```

### 프롬프트 7-2: GitHub README 작성
```
fflux 프로젝트의 GitHub README.md를 영어로 작성해줘.

포함 내용:
- 프로젝트 소개 (배지: License/LGPL, .NET 10, Platform/Windows)
- 주요 기능 목록 (스크린샷 자리표시자 포함)
- 설치 방법 (GitHub Releases 다운로드)
- 빠른 시작 가이드 (FFmpeg LGPL 바이너리 준비 포함)
- FFmpeg LGPL 라이선스 고지
- 기여 방법 (Contributing)
- 로드맵 (Public Phase 완료 기준)
- 라이선스 섹션

개발자 커뮤니티에 어필할 수 있는 기술적 톤으로 작성해줘.
```

---

## 📌 Phase M — MISB 파싱 모듈 (Private 저장소)

> ⚠️ **이 Phase는 별도 Private 저장소(fflux.Misb)에서 진행합니다.**
> fflux Public 저장소에는 포함하지 않습니다.

### 프롬프트 M-1: KLV 파서 구현
```
MISB ST 0601 표준 기반의 KLV 파서를 구현해줘.
(공통 컨텍스트 + fflux.Core의 IMediaFileReader 사용 가정)

파싱 대상 태그:
- Tag 2: UNIX Timestamp
- Tag 13: Sensor Latitude / Tag 14: Sensor Longitude
- Tag 15: Sensor Altitude
- Tag 17/18/19: Platform Heading / Pitch / Roll
- Tag 23: Sensor Relative Azimuth
- Tag 48: Security Local MD Set

결과물:
- KlvParser (byte[] → KlvPacket 목록)
- MisbSt0601Decoder (KlvPacket → MisbMetadata)
- ffmpeg.autogen으로 MPEG-TS에서 KLV 추출
- xUnit 단위 테스트
```

### 프롬프트 M-2: 메타데이터 실시간 시각화 + 지도 오버레이
```
MISB 메타데이터 시각화 패널과 WebView2 기반 지도 오버레이를 구현해줘.

기능:
- 재생 위치 동기화 실시간 GPS/자세 표시
- Leaflet.js + OpenStreetMap 지도 (WebView2)
- 비행 경로 라인 누적 표시 + FOV 폴리곤
- CSV/JSON/KML 내보내기
- JS Interop (WPF ViewModel ↔ Leaflet.js)
```

---

## 📌 Phase A — AI 자막/번역 모듈 (Private 저장소)

> ⚠️ **이 Phase는 별도 Private 저장소(fflux.AiSubtitle)에서 진행합니다.**
> fflux Public 저장소에는 포함하지 않습니다.

### 프롬프트 A-1: 오디오 추출 + Whisper 연동
```
영상에서 오디오를 추출하고 OpenAI Whisper API로
자막을 생성하는 서비스를 만들어줘.

구현 내용:
1. AudioExtractionService — ffmpeg.autogen, 16kHz mono PCM, 청크 분할
2. WhisperTranscriptionService — Whisper API, 타임스탬프 파싱, 재시도 로직
3. SubtitleExportService — SRT / VTT / ASS 포맷 생성

인터페이스 기반으로 Mock 테스트 가능하게 설계해줘.
```

### 프롬프트 A-2: 실시간 번역 자막 오버레이 + 편집기
```
생성된 자막을 번역하고 편집하는 UI를 만들어줘.

기능:
- 원문 + 번역 2줄 오버레이 (GPT-4o / DeepL 선택)
- 번역 결과 SQLite 캐싱
- 자막 편집기: DataGrid 기반 (시작/종료 시간, 원문, 번역 편집)
- 타임라인 드래그 구간 조정
- SRT/VTT 저장
```

---

## 💡 프롬프트 사용 팁

1. **공통 컨텍스트**를 항상 첫 줄에 붙여 사용하세요
2. 한 번에 하나의 프롬프트만 사용하고, 결과 검토 후 다음 단계로 진행하세요
3. 코드가 길면 "계속해줘" 또는 "다음 파일 작성해줘"로 이어서 요청하세요
4. 에러 발생 시 에러 메시지 전체를 복사해서 "이 에러 고쳐줘"로 요청하세요
5. 각 Phase 완료 후 Git 커밋하고 다음 Phase로 이동하세요
6. Phase M / Phase A는 별도 Private 저장소에서 별도 CLAUDE.md를 유지하세요
