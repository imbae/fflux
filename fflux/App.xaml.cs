using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
#if AI_SUBTITLE
using fflux.AiSubtitle.DependencyInjection;
using fflux.AiSubtitle.Infrastructure.Database;
using fflux.AiSubtitle.Services.Subtitle;
using fflux.UI.Modules.Player;
#endif
using fflux.Core;
using fflux.Core.Abstractions;
#if MISB
using fflux.Misb;
#endif
using fflux.Core.Exceptions;
using fflux.UI.Shared.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace fflux.UI;

public partial class App : Application
{
    // ── Windows 타이머 해상도 설정 ───────────────────────────────────
    // 기본값 15.6ms → 1ms로 낮춰 Task.Delay / Thread.Sleep의 정밀도를 높입니다.
    // 비디오 재생의 PTS 기반 프레임 타이밍에 필수적입니다.
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);

    private readonly IHost _host;

    public static IServiceProvider Services => ((App)Current)._host.Services;

    // ── Pro 모듈 가용성 플래그 ────────────────────────────────────────
    // modules/ 서브폴더에 해당 DLL이 존재할 때만 true로 설정됩니다.
    // UI 레이어 전체에서 읽기 전용으로 참조합니다.
    public static bool IsMisbEnabled { get; private set; }
    public static bool IsAiSubtitleEnabled { get; private set; }

    public App()
    {
        // ── Step 1: modules/ 폴더 어셈블리 리졸버 등록 (가장 먼저) ──────
        // 표준 CLR 탐색 경로에 없는 어셈블리를 modules/ 서브폴더에서 대신 로드합니다.
        AppDomain.CurrentDomain.AssemblyResolve += OnModuleAssemblyResolve;

        // ── Step 2: Private 모듈 DLL 존재 여부 감지 ───────────────────
        string modulesDir = Path.Combine(AppContext.BaseDirectory, "modules");
        IsMisbEnabled = File.Exists(Path.Combine(modulesDir, "fflux.Misb.dll"));
        IsAiSubtitleEnabled = File.Exists(Path.Combine(modulesDir, "fflux.AiSubtitle.dll"));

        // ── 전역 ScrollViewer 마우스 휠 핸들러 등록 ─────────────────
        // WPF-UI NavigationView 내 Page의 ScrollViewer는 포커스가 없어도
        // PreviewMouseWheel(터널링) 이벤트를 통해 스크롤이 동작하도록 합니다.
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel));

        // .env 파일 로드 (GROQ_API_KEY 등 — 존재하는 경우만)
        TryLoadDotEnv();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                services.AddCoreServices();
                services.AddUIServices();
#if MISB
                // [NoInlining] 래퍼를 통해 호출해야 합니다.
                // IsMisbEnabled=false일 때 JIT가 이 경로를 컴파일하면서
                // fflux.Misb.dll 로딩을 강제하는 것을 방지합니다.
                if (IsMisbEnabled) RegisterMisbServices(services);
#endif
#if AI_SUBTITLE
                if (IsAiSubtitleEnabled) RegisterAiSubtitleServices(services, ctx.Configuration);
#endif
            })
            .Build();
    }

    // ── modules/ 어셈블리 리졸버 ─────────────────────────────────────
    /// <summary>
    /// CLR이 어셈블리를 기본 경로에서 찾지 못했을 때 호출됩니다.
    /// modules/ 서브폴더에서 해당 DLL을 탐색하여 로드합니다.
    /// </summary>
    private static Assembly? OnModuleAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name!;
        string path = Path.Combine(AppContext.BaseDirectory, "modules", $"{name}.dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    // ── [NoInlining] DI 등록 래퍼 ────────────────────────────────────
    // NoInlining: JIT가 호출 지점에 인라인 전개하는 것을 막아,
    // DLL이 없을 때 이 메서드가 컴파일(=어셈블리 로드 시도)되지 않게 합니다.
    // 반드시 IsEnabled 체크 후에만 호출하세요.

#if MISB
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterMisbServices(IServiceCollection services)
        => services.AddMisbServices();
#endif

#if AI_SUBTITLE
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterAiSubtitleServices(
        IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddAiSubtitle(configuration);

        // AiSubtitleOptions.FfmpegBinaryPath 를 ISettingsService에서 런타임 주입.
        services.AddOptions<AiSubtitleOptions>()
            .PostConfigure<ISettingsService>((opts, ss) =>
            {
                opts.FfmpegBinaryPath = ss.Current.FFmpegBinaryPath;
            });

        // PlayerViewModel → IMediaPositionProvider 어댑터 등록.
        // PlayerViewModel이 IMediaPositionProvider를 직접 구현하면 모듈 DLL 없을 때
        // 타입 로드 시점에 fflux.AiSubtitle.dll 로드를 강제하여 앱이 충돌합니다.
        // 이 어댑터는 IsAiSubtitleEnabled=true일 때만 등록되므로 안전합니다.
        services.AddSingleton<IMediaPositionProvider, PlayerMediaPositionAdapter>();
    }
#endif

    // ── 마우스 휠 핸들러 ─────────────────────────────────────
    /// <summary>
    /// ScrollViewer가 포커스를 갖지 않아도 마우스 휠로 스크롤되도록 합니다.
    /// 중첩된 스크롤 가능한 자식이 있으면 자식에게 처리를 위임합니다.
    /// </summary>
    private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv) return;

        // 이벤트 소스에서 현재 ScrollViewer 사이에 스크롤 가능한 자식 ScrollViewer가
        // 있으면 해당 자식이 먼저 처리하도록 넘깁니다.
        if (HasScrollableChildScrollViewer(e.OriginalSource as DependencyObject, sv))
            return;

        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// <paramref name="source"/>에서 <paramref name="boundary"/>까지의 비주얼 트리 경로에
    /// 스크롤 가능한 ScrollViewer가 존재하는지 확인합니다.
    /// </summary>
    private static bool HasScrollableChildScrollViewer(
        DependencyObject? source, ScrollViewer boundary)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, boundary))
        {
            if (current is ScrollViewer inner &&
                inner.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 타이머 해상도를 1ms로 설정 (기본 15.6ms → PTS 기반 프레임 타이밍 정밀도 향상)
        timeBeginPeriod(1);

        await _host.StartAsync();

        // 1. 설정 로드 (테마·FFmpeg 초기화보다 먼저)
        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();

        // 2. 저장된 언어 적용
        LocalizationManager.Instance.SetLanguage(settingsService.Current.Language);

        // 3. 다크 테마 적용 (고정)
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);

        // 4. 다크 퍼플 액센트 적용
        ApplicationAccentColorManager.Apply(
            systemAccent: Color.FromRgb(0x5B, 0x2D, 0x92),
            applicationTheme: ApplicationTheme.Dark
        );

        // 5. FFmpeg 바이너리 초기화 (경로가 설정된 경우만)
        await TryInitializeFFmpegAsync(settingsService.Current.FFmpegBinaryPath);

#if AI_SUBTITLE
        // 5. AiSubtitle SQLite 캐시 DB 초기화 (번역 캐시 테이블 생성)
        if (IsAiSubtitleEnabled) await TryInitializeAiSubtitleDbAsync();
#endif

        // 6. 메인 창 표시
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        timeEndPeriod(1);
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }

#if AI_SUBTITLE
    // ── AiSubtitle DB 초기화 ─────────────────────────────────
    // [NoInlining]: IsAiSubtitleEnabled=true일 때만 호출. DLL 없을 때 JIT 컴파일 방지.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task TryInitializeAiSubtitleDbAsync()
    {
        try
        {
            var dbInit = _host.Services.GetRequiredService<DatabaseInitializer>();
            await dbInit.InitializeAsync();
        }
        catch (Exception ex)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogWarning(ex, "AiSubtitle 번역 캐시 DB 초기화 실패 — 캐시 없이 동작합니다.");
        }
    }
#endif

    // ── FFmpeg 초기화 ────────────────────────────────────────
    private async Task TryInitializeFFmpegAsync(string binaryPath)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            var logger = _host.Services
                .GetRequiredService<ILogger<App>>();
            logger.LogWarning(
                "FFmpeg 경로가 설정되지 않았습니다. " +
                "설정 페이지에서 FFmpeg LGPL 바이너리 경로를 지정해 주세요.");
            return;
        }

        try
        {
            var initializer = _host.Services
                .GetRequiredService<IFFmpegInitializer>();
            await initializer.InitializeAsync(binaryPath);
        }
        catch (FFmpegInitializationException ex)
        {
            // 초기화 실패는 앱 시작을 막지 않습니다.
            // 사용자가 Settings 페이지에서 경로를 수정하면 재시도할 수 있습니다.
            var logger = _host.Services
                .GetRequiredService<ILogger<App>>();
            logger.LogError(ex,
                "FFmpeg 초기화 실패 — 설정에서 경로를 확인하세요.");
        }
    }

    // ── .env 로드 ──────────────────────────────────────────
    /// <summary>
    /// .env 파일을 찾아 환경변수로 로드합니다. 없어도 앱 시작을 막지 않습니다.
    ///
    /// 탐색 순서 (먼저 발견된 파일 하나만 사용):
    ///   1. 실행 파일 디렉터리 (배포 환경 / bin\Debug\…\.env)
    ///   2. 프로젝트 루트 상위 탐색 — 솔루션 디렉터리의 .env
    ///   3. 솔루션 루트 하위 fflux.AiSubtitle\.env (개발 환경용 서브모듈 경로)
    /// </summary>
    private static void TryLoadDotEnv()
    {
        string? envPath = FindDotEnvPath();
        if (envPath is null)
        {
            // .env 파일을 찾지 못해도 앱 시작을 막지 않음
            // (시스템 환경변수 또는 appsettings에서 GROQ_API_KEY를 설정한 경우 동작)
            Debug.WriteLine("[AiSubtitle] .env 파일을 찾을 수 없습니다. 시스템 환경변수를 사용합니다.");
            return;
        }

        LoadDotEnvFile(envPath);
        Debug.WriteLine($"[AiSubtitle] .env 로드 완료: {envPath}");
    }

    private static string? FindDotEnvPath()
    {
        // 1. 실행 파일 디렉터리 (bin\Debug\net10.0-windows\)
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(baseDir, ".env");
        if (File.Exists(candidate)) return candidate;

        // 2+3. 실행 파일에서 위로 최대 6단계 올라가며 탐색
        //       개발 환경: bin\Debug\net10.0-windows → bin\Debug → bin → fflux → fflux(솔루션) → source
        string? dir = baseDir;
        for (int i = 0; i < 6; i++)
        {
            dir = Path.GetDirectoryName(dir?.TrimEnd(Path.DirectorySeparatorChar));
            if (dir is null) break;

            // 해당 디렉터리의 .env
            candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate)) return candidate;

            // fflux.AiSubtitle 서브모듈의 .env (솔루션 루트에서 탐색)
            candidate = Path.Combine(dir, "fflux.AiSubtitle", ".env");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static void LoadDotEnvFile(string envPath)
    {
        foreach (string line in File.ReadAllLines(envPath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            int eq = trimmed.IndexOf('=');
            if (eq < 1) continue;

            string key = trimmed[..eq].Trim();
            string value = trimmed[(eq + 1)..].Trim();

            // 이미 설정된 시스템 환경변수는 덮어쓰지 않음
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

}
