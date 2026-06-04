namespace fflux.UI.Modules.MisbViewer;

/// <summary>
/// VMTI 단일 표적의 바운딩 박스 + 레이블 오버레이 아이템.
/// Canvas 좌표계 (비디오 픽셀 단위).
/// </summary>
public sealed class VmtiOverlayItem
{
    /// <summary>바운딩 박스 좌상단 X [픽셀]</summary>
    public double X { get; init; }

    /// <summary>바운딩 박스 좌상단 Y [픽셀]</summary>
    public double Y { get; init; }

    /// <summary>바운딩 박스 너비 [픽셀]</summary>
    public double Width { get; init; }

    /// <summary>바운딩 박스 높이 [픽셀]</summary>
    public double Height { get; init; }

    /// <summary>표시할 레이블 (예: "T3: Vehicle (92%)")</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>바운딩 박스가 유효한지 (너비·높이 > 0)</summary>
    public bool HasBoundingBox => Width > 0 && Height > 0;
}
