using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using fflux.Core;
using fflux.Core.Abstractions;
using fflux.Core.Exceptions;
using fflux.UI.Shared.Models;
using fflux.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace fflux.UI;

public partial class App : Application
{
    private readonly IHost _host;

    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        // ── 전역 ScrollViewer 마우스 휠 핸들러 등록 ─────────────────
        // WPF-UI NavigationView 내 Page의 ScrollViewer는 포커스가 없어도
        // PreviewMouseWheel(터널링) 이벤트를 통해 스크롤이 동작하도록 합니다.
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel));

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddCoreServices();
                services.AddUIServices();
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

        // 5. 메인 창 표시
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
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
