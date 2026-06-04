#if AI_SUBTITLE
using fflux.AiSubtitle.Services.Subtitle;
#endif
using fflux.UI.Modules.AiSubtitle;
using fflux.UI.Modules.FFmpegExplorer;
using fflux.UI.Modules.Player;
using fflux.UI.Modules.Settings;
using fflux.UI.Modules.SubtitleEditor;
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
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<SubtitleEditorPage>();

        // ── ViewModels (Page별) ──────────────────────────────
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<FFmpegExplorerViewModel>();
        services.AddSingleton<SubtitleEditorViewModel>();

        // ── AI Subtitle — modules/fflux.AiSubtitle.dll 존재 시에만 등록 ──
        // IsAiSubtitleEnabled=false 일 때는 AiSubtitlePage 네비 아이템이 숨겨지므로
        // 페이지·뷰모델이 해석(resolve)될 일이 없습니다.
        // IsAiSubtitleEnabled=true 이면 RegisterAiSubtitleServices()가 먼저 호출되어
        // ISubtitleGenerationService 등 의존성이 이미 등록된 상태입니다.
#if AI_SUBTITLE
        if (App.IsAiSubtitleEnabled)
        {
            services.AddSingleton<AiSubtitlePage>();
            services.AddSingleton<AiSubtitleViewModel>();
            // ── 실시간 전사·번역 연동 ────────────────────────────────
            services.AddSingleton<IRealTimeTranslationService, RealTimeTranslationService>();
        }
        else
        {
            // DLL 없음: 스텁 인스턴스 등록 (빈 ViewModel — 페이지는 숨겨짐)
            services.AddSingleton<AiSubtitlePage>();
            services.AddSingleton<AiSubtitleViewModel>(sp =>
                AiSubtitleViewModel.CreateDisabled(
                    sp.GetRequiredService<ILogger<AiSubtitleViewModel>>()));
        }
#else
        // AI_SUBTITLE 심볼 없음: 컴파일 타임 스텁 구현 사용
        services.AddSingleton<AiSubtitlePage>();
        services.AddSingleton<AiSubtitleViewModel>();
#endif

        return services;
    }
}
