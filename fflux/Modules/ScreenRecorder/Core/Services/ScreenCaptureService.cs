using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows;
using fflux.UI.Modules.ScreenRecorder.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace fflux.UI.Modules.ScreenRecorder.Core.Services;

/// <summary>
/// DXGI Desktop Duplication API를 사용해 화면을 캡처하는 서비스.
/// GPU에서 직접 프레임을 획득하므로 GDI 방식 대비 성능이 높습니다.
/// 출력 픽셀 포맷: BGRA32.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    // HRESULT 코드 (DXGI_ERROR_*)
    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private const int DXGI_ERROR_ACCESS_LOST  = unchecked((int)0x887A0026);

    private readonly ILogger<ScreenCaptureService> _logger;

    private CaptureMode _mode      = CaptureMode.FullScreen;
    private int         _monitor   = 0;
    private Rect        _region    = Rect.Empty;
    private IntPtr      _window    = IntPtr.Zero;
    private int         _targetFps = 30;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
        => _logger = logger;

    // ── 설정 ────────────────────────────────────────────────────

    public void SetCaptureTarget(CaptureTarget target)
    {
        _mode    = target.Mode;
        _monitor = target.MonitorIndex;
    }

    public void SetCaptureRegion(Rect region) => _region = region;
    public void SetCaptureWindow(IntPtr hWnd) => _window = hWnd;
    public void SetTargetFps(int fps)          => _targetFps = Math.Clamp(fps, 1, 120);

    public IReadOnlyList<MonitorInfo> GetAvailableMonitors()
        => MonitorEnumerator.GetMonitors();

    // ── 캡처 스트림 ─────────────────────────────────────────────

    public async IAsyncEnumerable<CaptureFrame> CaptureAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<CaptureFrame>(
            new BoundedChannelOptions(4)
            {
                FullMode     = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

        // 캡처 루프는 전용 스레드에서 실행 (DXGI 스레드 어피니티 유지)
        var captureTask = Task.Run(
            () => RunCaptureDxgi(channel.Writer, _mode, _monitor, _region, _targetFps, ct),
            CancellationToken.None);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(ct))
                yield return frame;
        }
        finally
        {
            channel.Writer.TryComplete();
            try { await captureTask.ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "캡처 작업 종료 중 오류");
            }
        }
    }

    // ── DXGI 캡처 루프 (전용 스레드) ───────────────────────────

    private void RunCaptureDxgi(
        ChannelWriter<CaptureFrame> writer,
        CaptureMode mode, int monitorIdx, Rect region, int fps,
        CancellationToken ct)
    {
        ID3D11Device?           device  = null;
        ID3D11DeviceContext?    context = null;
        IDXGIOutputDuplication? dup     = null;
        ID3D11Texture2D?        staging = null;
        int w = 0, h = 0;

        try
        {
            // D3D11 디바이스 생성 (Vortice 3.x — 6인수 오버로드)
            FeatureLevel[] featureLevels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1];
            D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                featureLevels,
                out device,
                out context).CheckError();

            (dup, w, h) = CreateDuplication(device!, monitorIdx);
            staging     = CreateStagingTexture(device!, w, h);

            long tickPerUs  = Math.Max(1L, Stopwatch.Frequency / 1_000_000L);
            long frameGapUs = fps > 0 ? 1_000_000L / fps : 0;
            long nextUs     = Stopwatch.GetTimestamp() / tickPerUs;

            while (!ct.IsCancellationRequested)
            {
                // 프레임 획득 (17ms 타임아웃 ≈ 60fps 주기 대기)
                var hr = dup!.AcquireNextFrame(17u, out _, out var resource);

                if (hr.Code == DXGI_ERROR_WAIT_TIMEOUT)
                    continue;

                if (hr.Code == DXGI_ERROR_ACCESS_LOST)
                {
                    // 모니터 전환·절전 복구 시 Duplication 객체 재생성
                    _logger.LogWarning("DXGI_ERROR_ACCESS_LOST — Duplication 재생성");
                    dup.Dispose();
                    (dup, w, h) = CreateDuplication(device!, monitorIdx);
                    staging?.Dispose();
                    staging = CreateStagingTexture(device!, w, h);
                    continue;
                }

                hr.CheckError();

                // GPU 텍스처 → 스테이징 텍스처 복사
                try
                {
                    using var tex = resource!.QueryInterface<ID3D11Texture2D>();
                    context!.CopyResource(staging!, tex);
                }
                finally
                {
                    resource?.Dispose();
                    dup.ReleaseFrame();
                }

                // FPS 스로틀 — 타겟 인터벌 미경과 시 해당 프레임 스킵
                long nowUs = Stopwatch.GetTimestamp() / tickPerUs;
                if (frameGapUs > 0 && nowUs < nextUs)
                    continue;

                // CPU 메모리 읽기 및 프레임 구성
                var data  = ReadStagingTexture(context!, staging!, w, h);
                var frame = new CaptureFrame(data, w, h, nowUs);

                if (mode == CaptureMode.Region && region != Rect.Empty)
                    frame = CropFrame(frame, region);

                writer.TryWrite(frame);

                // catch-up 방지: nextUs가 너무 뒤처지면 "지금 + 1프레임" 으로 리셋
                // 이 없으면 정적 화면 후 활성화 시점에 수백 프레임이 한꺼번에 통과됨
                if (frameGapUs > 0)
                {
                    nextUs += frameGapUs;
                    if (nextUs < nowUs)
                        nextUs = nowUs + frameGapUs;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DXGI 캡처 루프에서 치명적 오류 발생");
            writer.TryComplete(ex);
        }
        finally
        {
            staging?.Dispose();
            dup?.Dispose();
            context?.Dispose();
            device?.Dispose();
            writer.TryComplete();
        }
    }

    // ── D3D11 / DXGI 헬퍼 ──────────────────────────────────────

    private static (IDXGIOutputDuplication dup, int w, int h)
        CreateDuplication(ID3D11Device device, int monitorIdx)
    {
        // IDXGIDevice → GetAdapter → EnumOutputs → IDXGIOutput1 → DuplicateOutput
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out var adapter).CheckError();
        using (adapter)
        {
            adapter!.EnumOutputs((uint)monitorIdx, out var output).CheckError();
            using (output)
            {
                using var output1 = output!.QueryInterface<IDXGIOutput1>();
                var dup  = output1.DuplicateOutput(device);
                var desc = output.Description;
                int w    = desc.DesktopCoordinates.Right  - desc.DesktopCoordinates.Left;
                int h    = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
                return (dup, w, h);
            }
        }
    }

    private static ID3D11Texture2D CreateStagingTexture(ID3D11Device device, int w, int h)
    {
        // CPU에서 읽을 수 있는 스테이징 텍스처 생성 (GPU→CPU 복사용 버퍼)
        var desc = new Texture2DDescription
        {
            Width             = (uint)w,
            Height            = (uint)h,
            MipLevels         = 1,
            ArraySize         = 1,
            Format            = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage             = ResourceUsage.Staging,
            BindFlags         = BindFlags.None,
            CPUAccessFlags    = CpuAccessFlags.Read,
            MiscFlags         = ResourceOptionFlags.None,
        };
        return device.CreateTexture2D(desc);
    }

    // unsafe 블록 분리 — async 메서드 내 unsafe CS4004 방지
    private static unsafe byte[] ReadStagingTexture(
        ID3D11DeviceContext context, ID3D11Texture2D texture, int w, int h)
    {
        context.Map(texture, 0u, MapMode.Read, Vortice.Direct3D11.MapFlags.None,
            out var mapped).CheckError();
        try
        {
            int stride = w * 4; // BGRA32
            var data   = new byte[h * stride];

            fixed (byte* dst = data)
            {
                var src = (byte*)mapped.DataPointer;
                for (int y = 0; y < h; y++)
                {
                    Buffer.MemoryCopy(
                        src + (long)y * mapped.RowPitch,
                        dst + (long)y * stride,
                        stride, stride);
                }
            }

            return data;
        }
        finally
        {
            context.Unmap(texture, 0u);
        }
    }

    private static unsafe CaptureFrame CropFrame(CaptureFrame frame, Rect region)
    {
        int x = Math.Max(0, (int)region.X);
        int y = Math.Max(0, (int)region.Y);
        int w = Math.Min((int)region.Width,  frame.Width  - x);
        int h = Math.Min((int)region.Height, frame.Height - y);

        if (w <= 0 || h <= 0) return frame;

        int srcStride = frame.Width * 4;
        int dstStride = w * 4;
        var data      = new byte[h * dstStride];

        fixed (byte* src = frame.Data, dst = data)
        {
            for (int row = 0; row < h; row++)
            {
                Buffer.MemoryCopy(
                    src + (long)(y + row) * srcStride + (long)x * 4,
                    dst + (long)row * dstStride,
                    dstStride, dstStride);
            }
        }

        return new CaptureFrame(data, w, h, frame.TimestampUs);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
