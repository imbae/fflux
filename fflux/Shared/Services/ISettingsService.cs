using System;
using System.Threading.Tasks;
using fflux.UI.Shared.Models;

namespace fflux.UI.Shared.Services;

/// <summary>
/// 앱 설정 로드/저장을 추상화하는 서비스 인터페이스.
/// </summary>
public interface ISettingsService
{
    /// <summary>현재 로드된 설정</summary>
    AppSettings Current { get; }

    /// <summary>설정이 저장된 후 발생하는 이벤트</summary>
    event EventHandler<AppSettings>? Saved;

    /// <summary>저장소에서 설정을 비동기로 로드합니다.</summary>
    Task LoadAsync();

    /// <summary>지정한 설정을 저장소에 비동기로 저장하고 Current를 갱신합니다.</summary>
    Task SaveAsync(AppSettings settings);
}
