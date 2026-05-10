using System;
using Wpf.Ui.Abstractions;

namespace fflux.UI.Shared.Services;

/// <summary>
/// DI 컨테이너 기반 NavigationView 페이지 제공자.
/// INavigationService와 연동하여 Type → 페이지 인스턴스를 해석합니다.
/// </summary>
public sealed class NavigationViewPageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationViewPageProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? GetPage(Type pageType)
        => _serviceProvider.GetService(pageType);
}
