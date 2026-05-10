namespace fflux.Core.Models;

/// <summary>특정 시점의 비트레이트 데이터 포인트입니다.</summary>
public sealed record BitratePoint(
    /// <summary>미디어 시작 기준 시간 (초)</summary>
    double TimeSeconds,
    /// <summary>해당 1초 구간의 비트레이트 (kbps)</summary>
    double BitrateKbps);
