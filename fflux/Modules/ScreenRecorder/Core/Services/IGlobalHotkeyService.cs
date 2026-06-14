namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>메인 윈도우가 표시된 후 한 번 호출합니다.</summary>
    void Initialize();

    /// <summary>
    /// 전역 핫키를 등록합니다.
    /// </summary>
    /// <param name="id">핫키 식별자 (앱 내 고유값)</param>
    /// <param name="modifiers">수식키 (0=없음, MOD_ALT=1, MOD_CTRL=2, MOD_SHIFT=4)</param>
    /// <param name="virtualKey">가상 키 코드 (F9=0x78, F10=0x79, F11=0x7A)</param>
    /// <param name="callback">핫키 트리거 시 UI 스레드에서 호출될 콜백</param>
    bool Register(int id, uint modifiers, uint virtualKey, Action callback);

    void Unregister(int id);
    void UnregisterAll();
}
