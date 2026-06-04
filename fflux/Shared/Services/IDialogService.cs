using System;
using System.Threading;
using System.Threading.Tasks;

namespace fflux.UI.Shared.Services;

/// <summary>
/// 사용자 대화 상자 표시를 추상화하는 서비스 인터페이스.
/// WPF-UI ContentDialog 기반으로 구현됩니다.
/// </summary>
public interface IDialogService
{
    /// <summary>확인/취소 선택 대화 상자를 표시합니다.</summary>
    /// <returns>사용자가 확인을 선택하면 <see langword="true"/></returns>
    Task<bool> ShowConfirmAsync(
        string title,
        string message,
        string confirmText = "확인",
        string cancelText = "취소",
        CancellationToken cancellationToken = default);

    /// <summary>정보 대화 상자를 표시합니다.</summary>
    Task ShowInfoAsync(
        string title,
        string message,
        string closeText = "확인",
        CancellationToken cancellationToken = default);

    /// <summary>오류 대화 상자를 표시합니다.</summary>
    Task ShowErrorAsync(
        string title,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default);

    /// <summary>경고 대화 상자를 표시합니다.</summary>
    Task ShowWarningAsync(
        string title,
        string message,
        string closeText = "확인",
        CancellationToken cancellationToken = default);
}
