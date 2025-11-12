using ContentEditor.Core;
using DirectXTexNet;
using ReeLib;
using ReeLib.DDS;

namespace ContentEditor.BackgroundTasks;

public class TextureConversionTask : IBackgroundTask
{
    private DDSFile dds;
    private readonly Action<DDSFile> callback;
    private readonly TextureOperation[] operations;

    public string Status { get; private set; }

    public TextureConversionTask(DDSFile dds, Action<DDSFile> callback, params TextureOperation[] operations)
    {
        this.dds = dds;
        this.callback = callback;
        this.operations = operations;
        Status = "Starting";
    }

    public override string ToString() => $"Texture Conversion: {dds.FileHandler.FilePath}";

    public unsafe void Execute(CancellationToken token = default)
    {
        var memstream = dds.FileHandler.Stream.ToMemoryStream(false, false);
        var buffer = memstream.GetBuffer();

        ScratchImage? image = null;
        ScratchImage? tmpImage = null;
        try {
            fixed (byte* bufPtr = buffer) {
                image = DirectXTexNet.TexHelper.Instance.LoadFromDDSMemory((IntPtr)bufPtr, (nint)memstream.Length, DDS_FLAGS.FORCE_DX10_EXT | DDS_FLAGS.ALLOW_LARGE_FILES);
            }

            var meta = image.GetMetadata();
            var sourceFormat = meta.Format;
            var outputformat = (DxgiFormat)sourceFormat;
            int targetMipLevel = -1;

            foreach (var op in operations) {
                switch (op) {
                    case ChangeFormat fmt:
                        outputformat = fmt.Format;
                        break;
                    case GenerateMipMaps:
                        targetMipLevel = 0;
                        break;
                }
            }

            if (((DxgiFormat)meta.Format).IsBlockCompressedFormat()) {
                Status = "Decompressing";
                tmpImage = image.Decompress(outputformat.IsBlockCompressedFormat() ? DXGI_FORMAT.UNKNOWN : (DXGI_FORMAT)outputformat);
                SwapImage(ref image, ref tmpImage);
            }

            if (token.IsCancellationRequested) return;

            if (targetMipLevel != -1) {
                Status = "Generating MipMaps";
                tmpImage = image.GenerateMipMaps(TEX_FILTER_FLAGS.DEFAULT, targetMipLevel);
                SwapImage(ref image, ref tmpImage);
            }

            if (token.IsCancellationRequested) return;

            meta = image.GetMetadata();

            if (outputformat != (DxgiFormat)meta.Format) {
                var convFormat = (DXGI_FORMAT)outputformat;

                if (outputformat.IsBlockCompressedFormat()) {
                    if (BackgroundResources.Instance.Value!.TryGetD3DDevice(out var d3dDevice)) {
                        // GPU happy path
                        Status = "Compressing (GPU)";
                        tmpImage = image.Compress(d3dDevice, convFormat, TEX_COMPRESS_FLAGS.DEFAULT, 1.0f);
                    } else {
                        // CPU fallback
                        Status = "Compressing (CPU)";
                        tmpImage = image.Compress(convFormat, TEX_COMPRESS_FLAGS.DEFAULT, 1.0f);
                    }
                } else {
                    Status = "Converting format";
                    tmpImage = image.Convert(convFormat, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
                }

                SwapImage(ref image, ref tmpImage);
            }

            if (token.IsCancellationRequested) {
                return;
            }

            Status = "Finalizing";
            var ddsStream = image.SaveToDDSMemory(DDS_FLAGS.FORCE_DX10_EXT);
            dds.FileHandler = new FileHandler(ddsStream, this.dds.FileHandler.FilePath);
            dds.Read();
            dds.FileHandler.Seek(0);

            callback.Invoke(dds);
            Status = "Done";
        } finally {
            image?.Dispose();
            tmpImage?.Dispose();
        }
    }

    private static void SwapImage(ref ScratchImage src, ref ScratchImage tmp)
    {
        src.Dispose();
        src = tmp;
        tmp = null!;
    }

    public abstract class TextureOperation
    {
    }

    public class GenerateMipMaps : TextureOperation { }
    public class ChangeFormat(ReeLib.DDS.DxgiFormat format) : TextureOperation
    {
        public DxgiFormat Format { get; } = format;
    }
}