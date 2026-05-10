using System;
using System.Windows;
using System.Windows.Media;
using fflux.Core;
using fflux.UI.Shared.Models;
using fflux.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace fflux.UI;

public partial class App : Application
{
    private readonly IHost _host;

    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddCoreServices();
                services.AddUIServices();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // 1. 설정 로드 (테마 적용보다 먼저)
        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();

        // 2. 저장된 테마 적용
        ApplyTheme(settingsService.Current.Theme);

        // 3. 다크 퍼플 액센트 적용
        ApplicationAccentColorManager.Apply(
            systemAccent: Color.FromRgb(0x5B, 0x2D, 0x92),
            applicationTheme: ApplicationTheme.Dark
        );

        // 4. 메인 창 표시
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
