using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using fflux.UI.Shared.Models;
using Microsoft.Extensions.Logging;

namespace fflux.UI.Shared.Services;

/// <summary>
/// JSON 파일 기반 ISettingsService 구현체.
/// 저장 경로: %LOCALAPPDATA%\fflux\appsettings.local.json
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    // ── 상수 ────────────────────────────────────────────────
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fflux");

    private static readonly string SettingsFilePath =
        Path.Combine(SettingsDirectory, "appsettings.local.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── 상태 ────────────────────────────────────────────────
    private readonly ILogger<JsonSettingsService> _logger;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Saved;

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
    }

    // ── Load ─────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        if (!File.Exists(SettingsFilePath))
        {
            _logger.LogInformation("설정 파일 없음 — 기본값 사용: {Path}", SettingsFilePath);
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsFilePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                      ?? new AppSettings();
            _logger.LogInformation("설정 로드 완료: {Path}", SettingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 파일 로드 실패 — 기본값 사용");
            Current = new AppSettings();
        }
    }

    // ── Save ─────────────────────────────────────────────────
    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(SettingsFilePath, json);

            Current = settings;
            Saved?.Invoke(this, settings);

            _logger.LogInformation("설정 저장 완료: {Path}", SettingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "설정 파일 저장 실패");
            throw;
        }
    }
}
