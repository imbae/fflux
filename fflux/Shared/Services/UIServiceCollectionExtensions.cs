using fflux.UI.Modules.FFmpegExplorer;
using fflux.UI.Modules.Player;
using fflux.UI.Modules.Settings;
using fflux.UI.Modules.VideoAnalyzer;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace fflux.UI.Shared.Services;

public static class UIServiceCollectionExtensions
{
    public static IServiceCollection AddUIServices(this IServiceCollection services)
    {
        // ── Settings ─────────────────────────────────────────
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        // ── Navigation ───────────────────────────────────────
        services.AddSingleton<INavigationViewPageProvider, NavigationViewPageProvider>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ── Dialog / Snackbar ────────────────────────────────
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ── Windows & Shell ──────────────────────────────────
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // ── Pages ─────────────────────────────────────────────
        services.AddSingleton<PlayerPage>();
        services.AddSingleton<FFmpegExplorerPage>();
        services.AddSingleton<VideoAnalyzerPage>();
        services.AddSingleton<SettingsPage>();

        // ── ViewModels (Page별) ──────────────────────────────
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
