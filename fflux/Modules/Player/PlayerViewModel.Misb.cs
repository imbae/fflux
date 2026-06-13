#if MISB
using fflux.Misb.Abstractions;
using fflux.Misb.Helpers;
using fflux.Misb.Models;
using fflux.Misb.Timeline;
using fflux.UI.Modules.MisbViewer;

namespace fflux.UI.Modules.Player;

/// <summary>
/// PlayerViewModel — MISB KLV 메타데이터 연동 (fflux.Misb 서브모듈 전용).
/// fflux.Misb 가 없으면 이 파일 전체가 컴파일에서 제외됩니다.
/// </summary>
public sealed partial class PlayerViewModel
{
    // ── MISB 전용 필드 ──────────────────────────────────────────────
    private IMetadataTimelineService? _misbTimeline;
    private IMisbPlaybackSyncService? _misbSyncService;
    private CancellationTokenSource? _misbLoadCts;

    // ── MISB 커맨드 ──────────────────────────────────────────────────

    /// <summary>MISB 오버레이 활성화/비활성화 토글.</summary>
    [RelayCommand]
    private async Task ToggleMisbAsync()
    {
        if (IsMisbEnabled)
        {
            IsMisbEnabled = false;
            UnsubscribeMisbSync();
            ClearMisbOverlay();
            MisbStatusText = string.Empty;
        }
        else
        {
            IsMisbEnabled = true;
            SubscribeMisbSync();
            if (IsFileOpen && _lastOpenedFilePath != null)
                await LoadMisbForFileAsync(_lastOpenedFilePath);
        }
    }

    /// <summary>우측 MISB 메타데이터 패널 표시/숨김 토글.</summary>
    [RelayCommand]
    private void ToggleMisbPanel()
        => IsMisbPanelVisible = !IsMisbPanelVisible;

    // ── MISB 헬퍼 ────────────────────────────────────────────────────

    private IMetadataTimelineService GetMisbTimeline()
        => _misbTimeline ??= _services.GetRequiredService<IMetadataTimelineService>();

    private IMisbPlaybackSyncService GetMisbSync()
        => _misbSyncService ??= _services.GetRequiredService<IMisbPlaybackSyncService>();

    private void SubscribeMisbSync()
        => GetMisbSync().MetadataUpdated += OnMisbMetadataUpdated;

    private void UnsubscribeMisbSync()
    {
        if (_misbSyncService != null)
            _misbSyncService.MetadataUpdated -= OnMisbMetadataUpdated;
    }

    /// <summary>MISB 파일 인덱싱 백그라운드 태스크.</summary>
    private async Task LoadMisbForFileAsync(string filePath)
    {
        _misbLoadCts?.Cancel();
        _misbLoadCts?.Dispose();
        _misbLoadCts = new CancellationTokenSource();
        var ct = _misbLoadCts.Token;

        IsMisbLoaded = false;
        MisbStatusText = "MISB 로드 중…";

        try
        {
            var timeline = GetMisbTimeline();
            var progress = new Progress<double>(p =>
                Application.Current.Dispatcher.InvokeAsync(() =>
                    MisbStatusText = $"MISB 로드 {p:P0}"));

            await timeline.LoadAsync(filePath, progress, ct);

            IsMisbLoaded = timeline.IsLoaded && timeline.IndexedCount > 0;
            MisbStatusText = IsMisbLoaded
                ? $"MISB: {timeline.IndexedCount:N0}개 레코드"
                : "MISB 데이터 없음";

            _logger.LogInformation("MISB 타임라인 로드 완료: {Count}개 레코드", timeline.IndexedCount);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MISB 로드 실패: {File}", filePath);
            IsMisbLoaded = false;
            MisbStatusText = "MISB 로드 실패";
        }
    }

    /// <summary>MetadataUpdated 이벤트 핸들러 — 오버레이 아이템과 메타데이터 패널을 갱신합니다.</summary>
    private void OnMisbMetadataUpdated(object? sender, MetadataSnapshot snapshot)
    {
        var items = BuildOverlayItems(snapshot.Metadata).ToList();
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            VmtiOverlayItems.Clear();
            foreach (var item in items)
                VmtiOverlayItems.Add(item);
            MisbDisplay.UpdateFrom(snapshot.Metadata);
        });
    }

    /// <summary>MisbMetadata → VmtiOverlayItem 목록 변환.</summary>
    private IEnumerable<VmtiOverlayItem> BuildOverlayItems(MisbMetadata metadata)
    {
        if (metadata.VmtiData is not { } vmti || vmti.Targets.Count == 0)
            yield break;

        uint frameWidth = vmti.FrameWidth > 0 ? vmti.FrameWidth : (uint)VideoWidth;
        if (frameWidth == 0) yield break;

        foreach (var target in vmti.Targets)
        {
            if (target.BoundingBoxTopLeft == 0 && target.BoundingBoxBottomRight == 0)
                continue;

            var (x0, y0) = PixelCoordinateHelper.GetCoordinate(target.BoundingBoxTopLeft, frameWidth);
            var (x1, y1) = PixelCoordinateHelper.GetCoordinate(target.BoundingBoxBottomRight, frameWidth);

            yield return new VmtiOverlayItem
            {
                X = Math.Min(x0, x1),
                Y = Math.Min(y0, y1),
                Width = Math.Abs(x1 - x0),
                Height = Math.Abs(y1 - y0),
                Label = BuildTargetLabel(target, vmti.Ontologies),
            };
        }
    }

    private static string BuildTargetLabel(VmtiTarget target, IReadOnlyList<VmtiOntology> ontologies)
    {
        if (target.Objects.Count == 0) return $"T{target.TargetId}";
        var obj = target.Objects[0];
        var ontology = ontologies.FirstOrDefault(o => o.OntologyId == obj.OntologyId);
        var name = ontology?.Label ?? "Unknown";
        var conf = double.IsNaN(obj.Confidence) ? "" : $" {obj.Confidence:F0}%";
        return $"T{target.TargetId}: {name}{conf}";
    }

    /// <summary>VMTI 오버레이 아이템 및 상태 초기화.</summary>
    private void ClearMisbOverlay()
    {
        if (Application.Current.Dispatcher.CheckAccess())
            VmtiOverlayItems.Clear();
        else
            Application.Current.Dispatcher.InvokeAsync(() => VmtiOverlayItems.Clear());
    }

    /// <summary>Dispose() 에서 호출 — MISB 리소스 정리.</summary>
    private void DisposeMisbResources()
    {
        UnsubscribeMisbSync();
        _misbLoadCts?.Cancel();
        _misbLoadCts?.Dispose();
        _misbLoadCts = null;
    }
}
#endif
