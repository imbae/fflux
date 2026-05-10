using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using fflux.UI.Shared.Models;
using fflux.UI.Shared.Services;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace fflux.UI.Modules.Settings;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;

    // ── FFmpeg 경로 ──────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(FFmpegPathValidation))]
    private string _FFmpegBinaryPath = string.Empty;

    // ── 기본 출력 폴더 ───────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(OutputFolderValidation))]
    private string _defaultOutputFolder = string.Empty;

    // ── 테마 ────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme))]
    [NotifyPropertyChangedFor(nameof(IsLightTheme))]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    private AppTheme _selectedTheme = AppTheme.System;

    // ── 언어 ────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private AppLanguage _selectedLanguage = AppLanguage.Korean;

    // ── 파생 프로퍼티 ────────────────────────────────────────
    public bool HasUnsavedChanges
    {
        get
        {
            var s = _settingsService.Current;
            return FFmpegBinaryPath    != s.FFmpegBinaryPath    ||
                   DefaultOutputFolder != s.DefaultOutputFolder ||
                   SelectedTheme       != s.Theme               ||
                   SelectedLanguage    != s.Language;
        }
    }

    /// <summary>FFmpeg 경로 유효성 메시지. 경로가 있을 때만 검증.</summary>
    public string FFmpegPathValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FFmpegBinaryPath)) return string.Empty;
            return Directory.Exists(FFmpegBinaryPath)
                ? string.Empty
                : "⚠ 폴더를 찾을 수 없습니다.";
        }
    }

    /// <summary>출력 폴더 유효성 메시지</summary>
    public string OutputFolderValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DefaultOutputFolder)) return string.Empty;
            return Directory.Exists(DefaultOutputFolder)
                ? string.Empty
                : "⚠ 폴더를 찾을 수 없습니다.";
        }
    }

    // RadioButton 바인딩용 편의 프로퍼티
    public bool IsSystemTheme
    {
        get => SelectedTheme == AppTheme.System;
        set { if (value) SelectedTheme = AppTheme.System; }
    }
    public bool IsLightTheme
    {
        get => SelectedTheme == AppTheme.Light;
        set { if (value) SelectedTheme = AppTheme.Light; }
    }
    public bool IsDarkTheme
    {
        get => SelectedTheme == AppTheme.Dark;
        set { if (value) SelectedTheme = AppTheme.Dark; }
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ISnackbarService snackbarService)
    {
        _settingsService = settingsService;
        _dialogService   = dialogService;
        _snackbarService = snackbarService;

        LoadFromSettings(_settingsService.Current);
    }

    // ── 테마 변경 즉시 적용 ──────────────────────────────────
    partial void OnSelectedThemeChanged(AppTheme value)
    {
        var wpfTheme = value switch
        {
            AppTheme.Light  => ApplicationTheme.Light,
            AppTheme.Dark   => ApplicationTheme.Dark,
            _               => ApplicationTheme.Dark   // System → 현재는 Dark로 처리
        };
        ApplicationThemeManager.Apply(wpfTheme);
    }

    // ── 커맨드: FFmpeg 폴더 찾아보기 ─────────────────────────
    [RelayCommand]
    private void BrowseFFmpegPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "FFmpeg 바이너리 폴더를 선택하세요",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            FFmpegBinaryPath = dialog.FolderName;
    }

    // ── 커맨드: 출력 폴더 찾아보기 ──────────────────────────
    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "기본 출력 폴더를 선택하세요",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            DefaultOutputFolder = dialog.FolderName;
    }

    // ── 커맨드: 설정 저장 ─────────────────────────────────────
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var newSettings = new AppSettings
            {
                FFmpegBinaryPath    = FFmpegBinaryPath,
                DefaultOutputFolder = DefaultOutputFolder,
                Theme               = SelectedTheme,
                Language            = SelectedLanguage
            };

            await _settingsService.SaveAsync(newSettings);
            OnPropertyChanged(nameof(HasUnsavedChanges));

            _snackbarService.Show(
                "저장 완료",
                "설정이 저장되었습니다.",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("저장 실패", "설정 저장 중 오류가 발생했습니다.", ex);
        }
    }

    // ── 커맨드: 기본값으로 초기화 ────────────────────────────
    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            "기본값으로 초기화",
            "모든 설정을 기본값으로 되돌립니다. 계속하시겠습니까?",
            confirmText: "초기화",
            cancelText:  "취소");

        if (!confirmed) return;

        var defaults = new AppSettings();
        LoadFromSettings(defaults);
        await _settingsService.SaveAsync(defaults);
        OnPropertyChanged(nameof(HasUnsavedChanges));

        _snackbarService.Show(
            "초기화 완료",
            "설정이 기본값으로 초기화되었습니다.",
            ControlAppearance.Caution,
            new SymbolIcon(SymbolRegular.ArrowReset24),
            TimeSpan.FromSeconds(3));
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────
    private void LoadFromSettings(AppSettings s)
    {
        FFmpegBinaryPath    = s.FFmpegBinaryPath;
        DefaultOutputFolder = s.DefaultOutputFolder;
        SelectedTheme       = s.Theme;
        SelectedLanguage    = s.Language;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
