#if AI_SUBTITLE
using fflux.AiSubtitle.Services.Subtitle;

namespace fflux.UI.Modules.Player;

/// <summary>
/// PlayerViewModel의 재생 위치를 IMediaPositionProvider로 노출하는 어댑터.
/// fflux.AiSubtitle.dll이 존재할 때만 등록됩니다.
/// PlayerViewModel이 IMediaPositionProvider를 직접 구현하면 타입 로드 시
/// fflux.AiSubtitle.dll이 즉시 로드되어 모듈 없을 때 충돌하므로 이 어댑터로 분리합니다.
/// </summary>
internal sealed class PlayerMediaPositionAdapter : IMediaPositionProvider
{
    private readonly PlayerViewModel _playerVm;

    public PlayerMediaPositionAdapter(PlayerViewModel playerVm)
    {
        _playerVm = playerVm;
        _playerVm.PositionChanged += OnPlayerPositionChanged;
    }

    public TimeSpan CurrentPosition => _playerVm.CurrentPosition;

    public event EventHandler<TimeSpan>? PositionChanged;

    private void OnPlayerPositionChanged(object? sender, TimeSpan position)
        => PositionChanged?.Invoke(this, position);
}
#endif
