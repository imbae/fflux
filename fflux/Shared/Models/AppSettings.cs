namespace fflux.UI.Shared.Models;

/// <summary>앱 설정 루트 모델 (appsettings.local.json으로 영속)</summary>
public sealed class AppSettings
{
    /// <summary>FFmpeg LGPL 바이너리 폴더 경로 (ffmpeg.exe가 있는 폴더)</summary>
    public string FFmpegBinaryPath { get; set; } = string.Empty;

    /// <summary>기본 출력 폴더 경로</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    /// <summary>앱 테마</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>앱 언어 (추후 지원 예정)</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Korean;
}

/// <summary>앱 테마 선택 열거형</summary>
public enum AppTheme
{
    /// <summary>시스템 설정 따름</summary>
    System,
    /// <summary>라이트 테마</summary>
    Light,
    /// <summary>다크 테마</summary>
    Dark
}

/// <summary>앱 언어 선택 열거형 (추후 지원)</summary>
public enum AppLanguage
{
    Korean,
    English
}
