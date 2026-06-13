#if AI_SUBTITLE
using fflux.AiSubtitle.Services.Subtitle;
using fflux.AiSubtitle.Services.Translation;
using fflux.UI.Modules.AiSubtitle;

namespace fflux.UI.Modules.Player;

/// <summary>
/// PlayerViewModel — 실시간 AI 번역 연동 (fflux.AiSubtitle 서브모듈 전용).
/// fflux.AiSubtitle 이 없으면 이 파일 전체가 컴파일에서 제외됩니다.
/// </summary>
// IMediaPositionProvider를 이 partial 선언에서 추가합니다.
// C# partial class는 인터페이스 목록을 파일별로 분리 추가할 수 있습니다.
public sealed partial class PlayerViewModel
{
    // ── AI 번역 전용 필드 ────────────────────────────────────────────
    // 실시간 번역 서비스 (지연 취득 — IMediaPositionProvider 순환 참조 회피)
    private IRealTimeTranslationService? _realTimeTranslationSvc;

    // ── 실시간 번역 커맨드 ────────────────────────────────────────────

    /// <summary>실시간 번역 활성화/비활성화 토글.</summary>
    [RelayCommand]
    private void ToggleRealTimeTranslation()
    {
        var svc = _realTimeTranslationSvc
            ??= _services.GetService<IRealTimeTranslationService>();

        if (svc == null)
        {
            _logger.LogWarning("IRealTimeTranslationService가 DI에 등록되지 않았습니다.");
            return;
        }

        if (IsRealTimeTranslationEnabled)
        {
            svc.Stop();
            svc.TranslationReady -= OnRealTimeTranslationReady;
            IsRealTimeTranslationEnabled = false;
            RealTimeTranslationText = string.Empty;
        }
        else
        {
            svc.TranslationReady += OnRealTimeTranslationReady;

            // AI Subtitle 페이지의 번역 설정과 연동
            var aiVm = _services.GetService<AiSubtitleViewModel>();
            svc.Start(
                targetLanguage: aiVm?.TargetLanguage ?? "ko",
                sourceLanguage: aiVm?.SourceLanguage ?? "",
                style: aiVm?.TranslationStyle ?? TranslationStyle.Default);
            IsRealTimeTranslationEnabled = true;
        }
    }

    private void OnRealTimeTranslationReady(object? sender, RealTimeTranslationResult result)
    {
        Application.Current.Dispatcher.InvokeAsync(
            () => RealTimeTranslationText = result.TranslatedText);
    }

    /// <summary>Dispose() 에서 호출 — AI 번역 리소스 정리.</summary>
    private void DisposeAiSubtitleResources()
    {
        if (_realTimeTranslationSvc != null)
        {
            _realTimeTranslationSvc.TranslationReady -= OnRealTimeTranslationReady;
            _realTimeTranslationSvc.Stop();
        }
    }
}
#endif
