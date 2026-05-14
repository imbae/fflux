using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using fflux.AiSubtitle.DependencyInjection;
using fflux.AiSubtitle.Infrastructure.Database;
using fflux.Core;
using fflux.Core.Abstractions;
using fflux.Misb;
using fflux.Core.Exceptions;
using fflux.UI.Shared.Models;
using fflux.UI.Shared.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    public App()
    {
        // ── LiveChartsCore 초기화 ─────────────────────────────────────
        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers()
            .AddDarkTheme());

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
                services.AddMisbServices();
                services.AddAiSubtitle(ctx.Configuration);
            })
            .Build();
    }

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

        // 2. 저장된 테마 적용
        ApplyTheme(settingsService.Current.Theme);

        // 3. 다크 퍼플 액센트 적용
        ApplicationAccentColorManager.Apply(
            systemAccent: Color.FromRgb(0x5B, 0x2D, 0x92),
            applicationTheme: ApplicationTheme.Dark
        );

        // 4. FFmpeg 바이너리 초기화 (경로가 설정된 경우만)
        await TryInitializeFFmpegAsync(settingsService.Current.FFmpegBinaryPath);

        // 5. AiSubtitle SQLite 캐시 DB 초기화 (번역 캐시 테이블 생성)
        await TryInitializeAiSubtitleDbAsync();

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

    // ── AiSubtitle DB 초기화 ─────────────────────────────────
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

            string key   = trimmed[..eq].Trim();
            string value = trimmed[(eq + 1)..].Trim();

            // 이미 설정된 시스템 환경변수는 덮어쓰지 않음
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    // ── 테마 적용 헬퍼 ──────────────────────────────────────
    private static void ApplyTheme(AppTheme theme)
    {
        var wpfTheme = theme switch
        {
            AppTheme.Light  => ApplicationTheme.Light,
            AppTheme.Dark   => ApplicationTheme.Dark,
            _               => ApplicationTheme.Dark  // System → Dark 처리
        };
        ApplicationThemeManager.Apply(wpfTheme, WindowBackdropType.Mica);
    }
}
