using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace fflux.UI.Shared.Services;

/// <summary>
/// WPF-UI ContentDialog 기반 IDialogService 구현체.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IContentDialogService _contentDialogService;
    private readonly ILogger<DialogService> _logger;

    public DialogService(
        IContentDialogService contentDialogService,
        ILogger<DialogService> logger)
    {
        _contentDialogService = contentDialogService;
        _logger = logger;
    }

    public async Task<bool> ShowConfirmAsync(
        string title,
        string message,
        string confirmText = "확인",
        string cancelText = "취소",
        CancellationToken cancellationToken = default)
    {
        var result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = message,
                PrimaryButtonText = confirmText,
                CloseButtonText = cancelText,
                DefaultButton = ContentDialogButton.Primary
            },
            cancellationToken);

        return result == ContentDialogResult.Primary;
    }

    public async Task ShowInfoAsync(
        string title,
        string message,
        string closeText = "확인",
        CancellationToken cancellationToken = default)
    {
        await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = message,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close
            },
            cancellationToken);
    }

    public async Task ShowErrorAsync(
        string title,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        if (exception is not null)
            _logger.LogError(exception, "[Dialog] {Title}: {Message}", title, message);

        var content = exception is not null
            ? $"{message}\n\n상세: {exception.Message}"
            : message;

        await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = content,
                CloseButtonText = "확인",
                DefaultButton = ContentDialogButton.Close
            },
            cancellationToken);
    }

    public async Task ShowWarningAsync(
        string title,
        string message,
        string closeText = "확인",
        CancellationToken cancellationToken = default)
    {
        await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = $"⚠️  {title}",
                Content = message,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Close
            },
            cancellationToken);
    }
}
