using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StageFlow.Engine.Graphics;

/// <summary>Fixed-function D3D11 BGRA-to-NV12 conversion before hardware encoding.</summary>
public sealed class GpuEncoderConverter : IDisposable
{
    private readonly ID3D11VideoDevice _device;
    private readonly ID3D11VideoContext1 _context;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly VideoProcessorStream[] _streams = new VideoProcessorStream[1];
    private ID3D11VideoProcessorInputView? _input;
    private nint _inputTexture;
    private readonly Dictionary<nint, ID3D11VideoProcessorOutputView> _outputs = [];
    public GpuEncoderConverter(GpuDevice gpu, int width, int height, int fpsNumerator, int fpsDenominator)
    {
        _device = gpu.Device.QueryInterface<ID3D11VideoDevice>();
        _context = gpu.Context.QueryInterface<ID3D11VideoContext1>();
        var description = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive, InputWidth = (uint)width, InputHeight = (uint)height,
            OutputWidth = (uint)width, OutputHeight = (uint)height,
            InputFrameRate = new((uint)fpsNumerator, (uint)fpsDenominator), OutputFrameRate = new((uint)fpsNumerator, (uint)fpsDenominator),
            Usage = VideoUsage.PlaybackNormal
        };
        _device.CreateVideoProcessorEnumerator(description, out _enumerator).CheckError();
        _device.CreateVideoProcessor(_enumerator, 0, out _processor).CheckError();
        var rect = new RawRect(0, 0, width, height);
        _context.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        _context.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        _context.VideoProcessorSetStreamSourceRect(_processor, 0, true, rect);
        _context.VideoProcessorSetStreamDestRect(_processor, 0, true, rect);
        _context.VideoProcessorSetOutputTargetRect(_processor, true, rect);
        // DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709 = 0; YCBCR_STUDIO_G22_LEFT_P709 = 8.
        _context.VideoProcessorSetStreamColorSpace1(_processor, 0, (ColorSpaceType)0);
        _context.VideoProcessorSetOutputColorSpace1(_processor, (ColorSpaceType)8);
    }
    public void Convert(ID3D11Texture2D input, ID3D11Texture2D output)
    {
        if (_inputTexture != input.NativePointer)
        {
            _input?.Dispose();
            _device.CreateVideoProcessorInputView(input, _enumerator, new VideoProcessorInputViewDescription
            { ViewDimension = VideoProcessorInputViewDimension.Texture2D, Texture2D = new() { ArraySlice = 0, MipSlice = 0 } }, out _input).CheckError();
            _inputTexture = input.NativePointer;
        }
        if (!_outputs.TryGetValue(output.NativePointer, out var view))
        {
            _device.CreateVideoProcessorOutputView(output, _enumerator, new VideoProcessorOutputViewDescription
            { ViewDimension = VideoProcessorOutputViewDimension.Texture2D, Texture2D = new() { MipSlice = 0 } }, out view).CheckError();
            _outputs.Add(output.NativePointer, view);
        }
        _streams[0] = new VideoProcessorStream { Enable = true, InputSurface = _input };
        _context.VideoProcessorBlt(_processor, view, 0, 1, _streams).CheckError();
    }
    public void Dispose()
    {
        foreach (var output in _outputs.Values) output.Dispose(); _outputs.Clear(); _input?.Dispose();
        _processor.Dispose(); _enumerator.Dispose(); _context.Dispose(); _device.Dispose();
    }
}
