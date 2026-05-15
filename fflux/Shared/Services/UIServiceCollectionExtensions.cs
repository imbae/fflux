using fflux.AiSubtitle.Services.Subtitle;
using fflux.UI.Modules.AiSubtitle;
using fflux.UI.Modules.BatchQueue;
using fflux.UI.Modules.BitrateAnalyzer;
using fflux.UI.Modules.FFmpegExplorer;
using fflux.UI.Modules.Player;
using fflux.UI.Modules.SceneDetector;
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
        services.AddSingleton<BatchQueuePage>();
        services.AddSingleton<SceneDetectorPage>();
        services.AddSingleton<BitrateAnalyzerPage>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<AiSubtitlePage>();

        // ── ViewModels (Page별) ──────────────────────────────
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<FFmpegExplorerViewModel>();
        services.AddSingleton<VideoAnalyzerViewModel>();
        services.AddSingleton<BatchQueueViewModel>();
        services.AddSingleton<SceneDetectorViewModel>();
        services.AddSingleton<BitrateAnalyzerViewModel>();
        services.AddSingleton<AiSubtitleViewModel>();

        // ── AiSubtitle 실시간 전사·번역 연동 ────────────────────
        // RealTimeTranslationService의 의존성(IAudioTranscriptionService,
        // ITranslationServiceFactory)은 AddAiSubtitle()에서 등록됩니다.
        services.AddSingleton<IRealTimeTranslationService, RealTimeTranslationService>();

        return services;
    }
}
