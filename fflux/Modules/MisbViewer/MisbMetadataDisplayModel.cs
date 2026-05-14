using fflux.Misb.Models;

namespace fflux.UI.Modules.MisbViewer;

/// <summary>
/// MISB 메타데이터를 UI에 표시하기 위한 뷰 모델.
/// 모든 값은 사전 포맷된 문자열로 저장됩니다.
/// </summary>
public sealed partial class MisbMetadataDisplayModel : ObservableObject
{
    // ── 타임스탬프 ────────────────────────────────────────────────────

    [ObservableProperty] private string _precisionTimestamp = "—";
    [ObservableProperty] private string _playbackTimestamp  = "—";

    // ── 플랫폼 식별 ───────────────────────────────────────────────────

    [ObservableProperty] private string _missionId    = "—";
    [ObservableProperty] private string _tailNumber   = "—";
    [ObservableProperty] private string _versionNumber = "—";

    // ── 센서 위치 ─────────────────────────────────────────────────────

    [ObservableProperty] private string _sensorLatitude         = "—";
    [ObservableProperty] private string _sensorLongitude        = "—";
    [ObservableProperty] private string _sensorAltitude         = "—";
    [ObservableProperty] private string _sensorEllipsoidHeight  = "—";

    // ── 플랫폼 자세 ───────────────────────────────────────────────────

    [ObservableProperty] private string _heading = "—";
    [ObservableProperty] private string _pitch   = "—";
    [ObservableProperty] private string _roll    = "—";

    // ── 센서 FOV / 방위각 ─────────────────────────────────────────────

    [ObservableProperty] private string _horizontalFov     = "—";
    [ObservableProperty] private string _verticalFov       = "—";
    [ObservableProperty] private string _relativeAzimuth   = "—";
    [ObservableProperty] private string _relativeElevation = "—";
    [ObservableProperty] private string _relativeRoll      = "—";

    // ── 프레임 중심 ───────────────────────────────────────────────────

    [ObservableProperty] private string _frameCenterLat = "—";
    [ObservableProperty] private string _frameCenterLon = "—";
    [ObservableProperty] private string _frameCenterAlt = "—";

    // ── 프레임 모서리 ─────────────────────────────────────────────────

    [ObservableProperty] private bool   _hasCornerPoints = false;
    [ObservableProperty] private string _cornerTL = "—";
    [ObservableProperty] private string _cornerTR = "—";
    [ObservableProperty] private string _cornerBR = "—";
    [ObservableProperty] private string _cornerBL = "—";

    // ── VMTI ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _hasVmti            = false;
    [ObservableProperty] private string _vmtiSystem         = "—";
    [ObservableProperty] private string _vmtiTargetCountText = "—";
    [ObservableProperty] private string _vmtiTargetsText    = "—";

    // ══════════════════════════════════════════════════════════════════
    // 업데이트
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// MisbMetadata 값으로 모든 표시 프로퍼티를 갱신합니다.
    /// UI 스레드에서 호출해야 합니다.
    /// </summary>
    public void UpdateFrom(MisbMetadata metadata)
    {
        // ── 타임스탬프
        PrecisionTimestamp = metadata.PrecisionTimestamp.HasValue
            ? metadata.PrecisionTimestamp.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") + " UTC"
            : "—";
        PlaybackTimestamp = FormatTs(metadata.Timestamp);

        // ── 플랫폼 식별
        MissionId     = metadata.MissionId          ?? "—";
        TailNumber    = metadata.PlatformTailNumber ?? "—";
        VersionNumber = metadata.VersionNumber.HasValue
            ? $"v{metadata.VersionNumber.Value}"
            : "—";

        // ── 센서 위치
        SensorLatitude        = FormatDeg(metadata.SensorPosition.Latitude);
        SensorLongitude       = FormatDeg(metadata.SensorPosition.Longitude);
        SensorAltitude        = FormatAlt(metadata.SensorPosition.Altitude);
        SensorEllipsoidHeight = FormatAlt(metadata.SensorEllipsoidHeight);

        // ── 플랫폼 자세
        Heading = FormatDeg(metadata.Attitude.Heading);
        Pitch   = FormatDeg(metadata.Attitude.Pitch);
        Roll    = FormatDeg(metadata.Attitude.Roll);

        // ── 센서 FOV / 방위각
        HorizontalFov     = FormatDeg(metadata.Sensor.HorizontalFov);
        VerticalFov       = FormatDeg(metadata.Sensor.VerticalFov);
        RelativeAzimuth   = FormatDeg(metadata.Sensor.RelativeAzimuth);
        RelativeElevation = FormatDeg(metadata.Sensor.RelativeElevation);
        RelativeRoll      = FormatDeg(metadata.Sensor.RelativeRollAngle);

        // ── 프레임 중심
        FrameCenterLat = FormatDeg(metadata.FrameCenter.Latitude);
        FrameCenterLon = FormatDeg(metadata.FrameCenter.Longitude);
        FrameCenterAlt = FormatAlt(metadata.FrameCenter.Altitude);

        // ── 프레임 모서리
        HasCornerPoints = metadata.CornerPoints.IsValid;
        if (HasCornerPoints)
        {
            CornerTL = FormatGeoPoint(metadata.CornerPoints.Corner1);
            CornerTR = FormatGeoPoint(metadata.CornerPoints.Corner2);
            CornerBR = FormatGeoPoint(metadata.CornerPoints.Corner3);
            CornerBL = FormatGeoPoint(metadata.CornerPoints.Corner4);
        }
        else
        {
            CornerTL = CornerTR = CornerBR = CornerBL = "—";
        }

        // ── VMTI
        if (metadata.VmtiData is { } vmti)
        {
            HasVmti = true;
            VmtiSystem = vmti.SystemName ?? "—";
            VmtiTargetCountText = vmti.TotalTargetCount == 0 && vmti.ReportedTargetCount == 0
                ? $"{vmti.Targets.Count}개"
                : $"{vmti.ReportedTargetCount} / {vmti.TotalTargetCount}개";

            VmtiTargetsText = BuildVmtiTargetsText(vmti);
        }
        else
        {
            HasVmti = false;
            VmtiSystem = VmtiTargetCountText = VmtiTargetsText = "—";
        }
    }

    // ── 포맷 헬퍼 ─────────────────────────────────────────────────────

    private static string FormatDeg(double value)
        => double.IsNaN(value) ? "—" : $"{value:+0.0000000°;-0.0000000°;0°}";

    private static string FormatAlt(double value)
        => double.IsNaN(value) ? "—" : $"{value:N1} m";

    private static string FormatGeoPoint(GeoPoint p)
        => double.IsNaN(p.Latitude) || double.IsNaN(p.Longitude)
            ? "—"
            : $"{p.Latitude:F6}°, {p.Longitude:F6}°";

    private static string FormatTs(TimeSpan ts)
        => ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";

    private static string BuildVmtiTargetsText(VmtiMetadata vmti)
    {
        if (vmti.Targets.Count == 0) return "표적 없음";

        var sb = new System.Text.StringBuilder();
        foreach (var target in vmti.Targets)
        {
            if (sb.Length > 0) sb.AppendLine();

            // 첫 번째 객체 분류 정보 조회
            string classification = "—";
            if (target.Objects.Count > 0)
            {
                var obj      = target.Objects[0];
                var ontology = vmti.Ontologies.FirstOrDefault(o => o.OntologyId == obj.OntologyId);
                var label    = ontology?.Label ?? "Unknown";
                classification = double.IsNaN(obj.Confidence)
                    ? label
                    : $"{label} {obj.Confidence:F0}%";
            }

            // 위치 오프셋 (있으면 표시)
            string location = !double.IsNaN(target.LocationOffsetLat) && !double.IsNaN(target.LocationOffsetLon)
                ? $"  Δ{target.LocationOffsetLat:+0.0000;-0.0000}°, {target.LocationOffsetLon:+0.0000;-0.0000}°"
                : string.Empty;

            sb.Append($"T{target.TargetId}: {classification}");
            if (!string.IsNullOrEmpty(location))
                sb.AppendLine(location);
        }
        return sb.ToString().TrimEnd();
    }
}
