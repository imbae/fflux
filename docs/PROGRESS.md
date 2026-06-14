# PROGRESS.md — fflux 화면 녹화 모듈 구현 진행 상황

> Phase 10 (Free 핵심) / Phase 11 (Pro 확장) 기능별 세부 구현 내용

---

## 📊 전체 진행 현황

| 기능 | 등급 | Phase | 상태 |
|---|---|---|---|
| 화면 캡처 서비스 (DXGI) | Free | 10 | ✅ 완료 |
| 녹화 설정 UI | Free | 10 | ✅ 완료 |
| 오디오 캡처 서비스 (WASAPI) | Free | 10 | ✅ 완료 |
| 녹화 세션 관리 | Free | 10 | ✅ 완료 |
| 글로벌 핫키 | Free | 10 | ✅ 완료 |
| 플로팅 컨트롤바 | Free | 10 | ✅ 완료 |
| 녹화 후 즉시 재생 연계 | Free | 10 | ✅ 완료 |
| 기본 그리기 도구 | Free | 10 | ✅ 완료 |
| 자동 사라지는 잉크 | **Pro** | 11 | 🔲 예정 |
| 스포트라이트/줌 펜 | **Pro** | 11 | 🔲 예정 |
| 레이저 포인터 모드 | **Pro** | 11 | 🔲 예정 |
| 클릭/키 입력 시각화 | **Pro** | 11 | 🔲 예정 |
| 그리기 타임라인 동기화 | **Pro** | 11 | 🔲 예정 |
| 주석 번인 내보내기 | **Pro** | 11 | 🔲 예정 |

---

## 🟦 Phase 10 — 핵심 녹화 기능 (Free)

---

### 1. 화면 캡처 서비스 (DXGI Desktop Duplication)

**상태**: ✅ 완료

**구현 목표**
DXGI Desktop Duplication API를 통해 GPU에서 직접 화면 프레임을 획득하고,
ffmpeg.autogen `AVFrame`으로 변환하여 인코딩 파이프라인에 전달한다.

**인터페이스**
```csharp
public interface IScreenCaptureService : IAsyncDisposable
{
    // 캡처 대상 설정
    void SetCaptureTarget(CaptureTarget target); // FullScreen / Region / Window
    void SetCaptureRegion(Rect region);
    void SetCaptureWindow(IntPtr hWnd);

    // 캡처 루프
    IAsyncEnumerable<CaptureFrame> CaptureAsync(CancellationToken ct);
}

public record CaptureTarget(CaptureMode Mode, int MonitorIndex = 0);
public enum CaptureMode { FullScreen, Region, Window }
public record CaptureFrame(byte[] Data, int Width, int Height, long TimestampUs);
```

**핵심 구현 사항**
- `SharpDX.DXGI` 또는 `Windows.Graphics.Capture` API 활용
- `IDXGIOutputDuplication.AcquireNextFrame()` → GPU 텍스처 → CPU 스테이징 텍스처 복사 → byte[]
- `DXGI_ERROR_ACCESS_LOST` 감지 시 Duplication 객체 자동 재생성 (모니터 전환/절전 복구)
- DRM 보호 콘텐츠 → 검은 화면으로 캡처됨 (OS 정책, 별도 처리 불필요)
- 캡처 FPS 제어: `DispatcherTimer` 또는 `PeriodicTimer` 기반 프레임 레이트 조절
- 영역 캡처 시 `SetCaptureRegion` 으로 지정된 `Rect` 크롭

**성능 고려**
- GPU → CPU 복사 최소화 (스테이징 버퍼 재사용)
- 프레임 드롭 허용 (녹화 타임스탬프 기반 동기화, 실시간성 우선)

**테스트 전략**
- `Mock<IScreenCaptureService>` 로 더미 프레임 생성 → 세션 로직 단위 테스트
- 실제 DXGI 테스트는 `[Fact(Skip = "Requires display")]` 조건부 처리

---

### 2. 오디오 캡처 서비스 (WASAPI)

**상태**: ✅ 완료

**구현 목표**
NAudio의 WASAPI를 통해 시스템 사운드(루프백)와 마이크 입력을 동시에 캡처하고,
PCM 오디오 데이터를 녹화 세션으로 전달한다.

**인터페이스**
```csharp
public interface IAudioCaptureService : IAsyncDisposable
{
    bool SystemAudioEnabled { get; set; }
    bool MicrophoneEnabled { get; set; }

    event EventHandler<AudioDataEventArgs> DataAvailable;

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}

public record AudioDataEventArgs(byte[] Data, int BytesRecorded, AudioSource Source);
public enum AudioSource { System, Microphone, Mixed }
```

**핵심 구현 사항**
- 시스템 사운드: `WasapiLoopbackCapture` (NAudio) — 재생 중인 오디오 캡처
- 마이크: `WasapiCapture` (NAudio) — 기본 입력 장치
- 동시 캡처 시 두 PCM 스트림 혼합 (샘플 단위 합산 후 클리핑 방지)
- 샘플레이트/채널 수 통일 (기본: 44100Hz, Stereo)
- 오디오 장치 목록 조회 → 설정 UI에서 선택 가능

**테스트 전략**
- NAudio `Mock` 또는 `WaveFileWriter` 기반 녹음 결과 파일 검증

---

### 3. 녹화 세션 관리

**상태**: ✅ 완료

**구현 목표**
화면 캡처 + 오디오 캡처 + ffmpeg 인코딩을 조율하는 세션 생명주기를 관리한다.

**인터페이스**
```csharp
public interface IRecordingSessionService
{
    RecordingState State { get; }
    TimeSpan Elapsed { get; }
    string? OutputFilePath { get; }

    event EventHandler<RecordingStateChangedEventArgs> StateChanged;

    Task StartAsync(RecordingSettings settings, CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task<string> StopAsync(); // 완료된 파일 경로 반환
}

public enum RecordingState { Idle, Recording, Paused, Stopping }

public record RecordingSettings(
    CaptureTarget CaptureTarget,
    Rect? CaptureRegion,
    IntPtr? WindowHandle,
    int Fps,
    string OutputDirectory,
    bool RecordSystemAudio,
    bool RecordMicrophone
);
```

**핵심 구현 사항**
- `IScreenCaptureService` + `IAudioCaptureService` 동시 시작/정지 조율
- 캡처 프레임 → ffmpeg.autogen `AVFrame` 변환 → 기존 Muxer 파이프라인 연결
- 출력 포맷: MKV (컨테이너) — Stream Copy 정책 유지, LGPL 안전
- 일시정지: 캡처 루프 중단 + 타임스탬프 Gap 보정 (재개 시 연속성 유지)
- 경과 시간: `Stopwatch` 기반, 일시정지 구간 제외
- 파일명 자동 생성: `fflux_record_yyyyMMdd_HHmmss.mkv`

---

### 4. 글로벌 핫키

**상태**: ✅ 완료

**구현 목표**
앱이 포커스를 잃은 상태에서도 녹화를 제어할 수 있는 전역 단축키를 등록한다.

**인터페이스**
```csharp
public interface IGlobalHotkeyService : IDisposable
{
    bool Register(HotkeyDefinition hotkey);
    void Unregister(int id);
    void UnregisterAll();

    event EventHandler<HotkeyTriggeredEventArgs> HotkeyTriggered;
}

public record HotkeyDefinition(int Id, ModifierKeys Modifiers, Key Key, string Action);
```

**핵심 구현 사항**
- Win32 `RegisterHotKey` / `UnregisterHotKey` P/Invoke
- WPF `HwndSource` 메시지 훅으로 `WM_HOTKEY` 수신
- 기본 단축키 (사용자 설정 가능):
  - `F9` → 녹화 시작/정지
  - `F10` → 일시정지/재개
  - `F11` → 스크린샷 (단일 프레임 저장)
- 충돌 단축키 감지 시 `IDialogService`로 경고

---

### 5. 플로팅 컨트롤바

**상태**: ✅ 완료

**구현 목표**
녹화 중 화면 위에 항상 표시되는 미니 컨트롤바를 제공한다.
다른 앱 위에 표시되며, 드래그로 위치 이동 가능.

**뷰 구성** (`RecorderOverlayWindow.xaml`)
```
┌──────────────────────────────────┐
│  🔴 00:03:42  ⏸ 일시정지  ⏹ 정지  │  ← 항상 위 (Topmost)
└──────────────────────────────────┘
```

**핵심 구현 사항**
- `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`
- 배경: 반투명 다크 패널 (WPF-UI 스타일)
- 경과 시간: `DispatcherTimer` 1초 주기 업데이트
- 마우스 드래그로 창 위치 이동 (`MouseLeftButtonDown` → `DragMove()`)
- 녹화 상태(Recording/Paused)에 따라 버튼 아이콘 동적 전환
- 최소화/숨김 버튼으로 컨트롤바 토글

---

### 6. 녹화 설정 UI

**상태**: ✅ 완료

**구현 목표**
NavigationView 서브메뉴 "Screen Recorder"에서 접근 가능한 설정 및 제어 페이지.

**뷰 구성** (`ScreenRecorderPage.xaml`)
```
[캡처 대상]   ○ 전체 화면  ○ 영역 선택  ○ 특정 윈도우
[모니터 선택] ──────────────────── (콤보박스)
[오디오]      ☑ 시스템 사운드    ☑ 마이크
[FPS]         ────── 30 ──────── (슬라이더: 15/30/60)
[출력 경로]   C:\Users\...\Videos    [폴더 선택]
[핫키 안내]   시작/정지: F9  |  일시정지: F10

              [● 녹화 시작]
```

**핵심 구현 사항**
- `ScreenRecorderViewModel` — `RecordingSettings` 바인딩
- 영역 선택: 반투명 오버레이 창으로 드래그 영역 지정 (`RegionSelectorWindow`)
- 특정 윈도우 선택: 실행 중인 윈도우 목록 콤보박스 (Win32 `EnumWindows`)
- 설정 값 `appsettings.local.json` 에 자동 저장/복원

---

### 7. 녹화 후 즉시 재생 연계

**상태**: ✅ 완료

**구현 목표**
녹화 완료 시 `IDialogService`로 알림 후, fflux 플레이어에서 바로 파일 열기.

**핵심 구현 사항**
- `IRecordingSessionService.StopAsync()` 완료 이벤트 수신
- `IDialogService.ShowConfirmAsync("녹화 완료. 지금 재생하시겠습니까?")` 표시
- 확인 시 → `PlayerViewModel.OpenFileAsync(outputFilePath)` 호출 (내비게이션 포함)

---

### 8. 기본 그리기 도구 (Free)

**상태**: ✅ 완료

**구현 목표**
녹화 중 화면 위 투명 레이어에서 펜/도형/텍스트를 그리고,
그리기 레이어를 캡처 프레임과 합성하여 영상에 포함한다.

**뷰 구성** (`DrawingOverlayWindow.xaml`)
- `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`, `Topmost="True"`
- `IsHitTestVisible` 토글 → 그리기 모드 ON: 클릭 수신 / OFF: 클릭 통과

**도구 목록 (Free)**
| 도구 | 구현 방식 |
|---|---|
| 펜 | `InkCanvas` — `StylusPointCollection` 기반 자유 곡선 |
| 사각형 / 원 / 선 | `Canvas`에 `Rectangle` / `Ellipse` / `Line` 동적 추가 |
| 화살표 | `Polyline` + `Path` (화살촉) |
| 텍스트 | `TextBox` 임시 입력 → `TextBlock`으로 고정 |
| Undo/Redo | `Stack<IDrawingCommand>` 커맨드 패턴 |

**핵심 구현 사항**
- 그리기 레이어 `RenderTargetBitmap`으로 래스터화 → 캡처 프레임과 픽셀 합성
- 색상, 굵기 선택 툴바 (`DrawingToolbarView.xaml`) — 단축키 지원
- 모든 그리기 요소 클리어 버튼

---

## 🟧 Phase 11 — Pro 확장 기능

---

### 9. 자동 사라지는 잉크 (Disappearing Ink)

**상태**: 🔲 예정

**구현 목표**
그린 후 일정 시간이 지나면 자연스럽게 페이드아웃되어 사라지는 잉크.
화면이 그리기 잔재로 지저분해지지 않아 강의/튜토리얼 영상에 적합.

**핵심 구현 사항**

```csharp
public class DisappearingInkTool : IDrawingTool
{
    // 설정 (사용자 조절 가능)
    public TimeSpan FadeDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan FadeDuration { get; set; } = TimeSpan.FromSeconds(1);

    // 내부 상태
    private readonly List<TrackedStroke> _strokes = new();
    private readonly DispatcherTimer _fadeTimer;

    // FadeTimer Tick (60fps)
    // → 각 스트로크 경과 시간 계산
    // → FadeDelay 초과분: alpha = 1 - (elapsed - FadeDelay) / FadeDuration
    // → DrawingAttributes.Color 알파값 적용
    // → alpha <= 0: InkCanvas.Strokes.Remove(stroke)
}
```

**UI 설정**
- Fade 딜레이: 슬라이더 (0.5초 ~ 10초)
- Fade 지속시간: 슬라이더 (0.2초 ~ 3초)
- 자동 사라지기 ON/OFF 토글

---

### 10. 스포트라이트/줌 펜

**상태**: 🔲 예정

**구현 목표**
특정 영역을 지정하면 해당 영역을 확대하여 포커스 줌 효과로 강조.
코드 리뷰, UI 설명 등에서 "이 부분을 보세요" 효과.

**핵심 구현 사항**
- **스포트라이트**: 지정 영역 외 화면을 반투명 어두운 오버레이로 덮음
  - `Canvas` 위에 전체 크기 반투명 사각형 + 지정 영역 `Clip` 으로 구멍 뚫기 (`CombinedGeometry`)
- **줌 펜**: 지정 Rect의 화면 캡처를 `ScaleTransform` 으로 확대 → 별도 팝업 창 또는 오버레이 패널에 표시
  - 팝업 위치: 지정 영역 옆 (화면 경계 고려하여 자동 조정)
  - 줌 배율: 2x / 3x / 4x (설정 가능)
- 마우스를 놓으면 팝업 자동 닫기 또는 고정 모드 (클릭으로 토글)

---

### 11. 레이저 포인터 모드

**상태**: 🔲 예정

**구현 목표**
마우스 움직임을 따라 빛나는 레이저 포인터 점을 표시.
클릭 시 확산 애니메이션으로 강조 효과 제공.

**핵심 구현 사항**
- `DrawingOverlayWindow` 위에 `Ellipse` (반투명 글로우 원) 오버레이
- `MouseMove` → `Canvas.SetLeft/Top` 으로 포인터 위치 실시간 이동
- **포인터 글로우 효과**: `Ellipse` + `DropShadowEffect` (Blur=15, Color=Red/Green/Blue 선택)
- **클릭 확산 애니메이션**: 클릭 시 `EllipseGeometry.RadiusX/Y` `DoubleAnimation` → 0에서 60px로 확산 후 Opacity 0으로 페이드
- 레이저 포인터 ON 시 커서 숨김 (`Cursor = Cursors.None`)
- 색상 선택: 빨강(기본) / 초록 / 파랑 / 흰색

```csharp
// 클릭 확산 애니메이션 예시
private void PlayClickRipple(Point position)
{
    var ripple = new Ellipse { Width = 0, Height = 0, Opacity = 0.8,
        Fill = new SolidColorBrush(_laserColor) };
    OverlayCanvas.Children.Add(ripple);

    var anim = new DoubleAnimation(0, 60, TimeSpan.FromMilliseconds(400));
    anim.Completed += (_, _) => OverlayCanvas.Children.Remove(ripple);
    ripple.BeginAnimation(WidthProperty, anim);
    ripple.BeginAnimation(HeightProperty, anim);
    ripple.BeginAnimation(OpacityProperty,
        new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(400)));
}
```

---

### 12. 클릭/키 입력 시각화

**상태**: 🔲 예정

**구현 목표**
마우스 클릭과 키보드 입력을 화면에 오버레이로 표시.
코딩 강의, 튜토리얼 영상에서 "어떤 키를 눌렀는지" 시청자가 볼 수 있게 함.

**핵심 구현 사항**

**마우스 클릭 시각화**
- 전역 마우스 훅 (`SetWindowsHookEx WH_MOUSE_LL`)
- 좌클릭: 파란 원 / 우클릭: 주황 원 → 클릭 위치에 확산 애니메이션
- 클릭 좌표를 캡처 영역 기준으로 변환 (전체화면/영역 캡처 모드 고려)

**키보드 입력 시각화**
- 전역 키보드 훅 (`SetWindowsHookEx WH_KEYBOARD_LL`)
- 화면 우하단(또는 사용자 지정 위치)에 최근 입력 키 표시 (최대 5개)
- 수식키(Ctrl/Alt/Shift) 조합 표시: `Ctrl + C` 형태
- 표시 후 2초 뒤 페이드아웃 (Disappearing Ink와 동일 메커니즘)

```
┌─────────────────────────┐
│  Ctrl + S               │  ← 화면 우하단 오버레이
│  Enter                  │
└─────────────────────────┘
```

**UI 설정**
- 마우스 클릭 시각화 ON/OFF
- 키보드 시각화 ON/OFF
- 키 표시 위치 (우하단/좌하단/우상단/좌상단)
- 키 표시 폰트 크기, 배경 색상

---

### 13. 그리기 타임라인 동기화 (fflux 전용)

**상태**: 🔲 예정

**구현 목표**
녹화 중 그린 모든 주석(펜 획, 도형, 텍스트)을 타임스탬프와 함께 벡터 데이터로 저장.
fflux 플레이어에서 재생 시 해당 시점 주석이 화면 위에 자동 재현되고 편집 가능.

**사이드카 파일 형식**
```json
// video.fflux-annotations.json
{
  "version": "1.0",
  "videoFile": "video.mkv",
  "createdAt": "2025-06-14T12:00:00Z",
  "entries": [
    {
      "id": "a1b2c3",
      "timestampUs": 5000000,
      "durationUs": 3000000,
      "type": "Stroke",
      "color": "#FFFF0000",
      "thickness": 3.0,
      "points": [[100, 200], [105, 210], [112, 225]],
      "fadeOut": true
    },
    {
      "id": "d4e5f6",
      "timestampUs": 10000000,
      "durationUs": 0,
      "type": "Shape",
      "shapeType": "Arrow",
      "color": "#FF00FF00",
      "thickness": 2.0,
      "bounds": { "x": 300, "y": 150, "width": 100, "height": 50 }
    },
    {
      "id": "g7h8i9",
      "timestampUs": 15000000,
      "durationUs": 5000000,
      "type": "Text",
      "color": "#FFFFFFFF",
      "fontSize": 18.0,
      "text": "이 부분 중요!",
      "position": { "x": 400, "y": 300 }
    }
  ]
}
```

**핵심 구현 사항**

_녹화 시_
- 그리기 이벤트 발생 시 현재 녹화 타임스탬프 (`RecordingSession.Elapsed`) 기록
- 모든 그리기 요소 → `AnnotationEntry` 모델로 직렬화
- 녹화 종료 시 `IAnnotationService.SaveAsync(outputPath)` 호출 → JSON 사이드카 저장

_fflux 재생 시_
- 영상 파일 열기 시 동일 경로 `.fflux-annotations.json` 자동 탐색·로드
- `PlayerViewModel` 재생 위치 변경 이벤트 → `IAnnotationService.GetEntriesAt(timestampUs)` 조회
- 해당 시점 주석을 `DrawingOverlayWindow`에 렌더링
- 주석 클릭 시 편집 모드 진입 (위치/색상/텍스트 수정, 타임스탬프 조정)

**인터페이스**
```csharp
public interface IAnnotationService
{
    AnnotationTrack? CurrentTrack { get; }

    Task LoadAsync(string videoFilePath);
    Task SaveAsync(string videoFilePath);

    void AddEntry(AnnotationEntry entry);
    void RemoveEntry(string id);
    IEnumerable<AnnotationEntry> GetEntriesAt(long timestampUs);
}
```

---

### 14. 주석 포함 번인 내보내기

**상태**: 🔲 예정

**구현 목표**
`.fflux-annotations.json` 의 주석 데이터를 영상 프레임에 직접 합성하여
일반 MP4로 내보낸다. 결과물은 fflux 없이도 재생 가능.

**핵심 구현 사항**
- `AnnotationExportService`: 영상 프레임 단위 디코딩 → 각 프레임 타임스탬프의 주석 조회 → WPF `DrawingVisual`로 렌더링 → 픽셀 합성 → ffmpeg.autogen 인코딩
- 인코딩: LGPL 안전 코덱만 허용 (기존 Stream Copy 정책 확인)
  - 화면 녹화 결과물은 Stream Copy 불가 (픽셀 합성 필요) → `libx264` 금지
  - 대안: `libopenh264` (LGPL) 또는 `mpeg4` 내장 인코더 사용
- 진행률: `IProgress<double>` → `ScreenRecorderViewModel` 프로그레스바 바인딩
- 내보내기 옵션: 출력 해상도, 비트레이트 선택

**내보내기 흐름**
```
fflux-annotations.json 로드
       ↓
영상 프레임 디코딩 루프 (ffmpeg.autogen)
       ↓
프레임 타임스탬프 → GetEntriesAt() → 주석 목록
       ↓
DrawingVisual 렌더링 → RenderTargetBitmap → byte[]
       ↓
프레임 픽셀 합성 (WriteableBitmap 또는 SkiaSharp)
       ↓
ffmpeg.autogen 인코딩 → MP4 출력
```

---

## 📝 변경 이력

| 날짜 | 내용 |
|---|---|
| 2025-06-14 | 화면 녹화 모듈 기능 정의 및 PROGRESS.md 최초 작성 |
| 2026-06-14 | Phase 10 화면 캡처 서비스(DXGI) + 녹화 설정 UI 구현 완료 |
| 2026-06-14 | Phase 10 오디오 캡처(WASAPI) + 녹화 세션 관리 + 인코딩(mpeg4) + 핫키 + 플로팅바 + 재생 연계 완료 |
| 2026-06-14 | Phase 10 기본 그리기 도구 완료 (펜/도형/화살표/텍스트/Undo-Redo + 클릭통과 + 프레임 합성) |
