using ContentEditor.Core;
using ReeLib;
using ReeLib.DDS;
using Hexa.NET.DirectXTex;

namespace ContentEditor.BackgroundTasks;

/// <summary>
/// Converts a DDS according to the given set of operations (format, mipmaps).
/// The DDS is modified in place, meaning the success callback receives the same DDS file instance as was given as input, and the original FileHandler Stream is disposed.
/// </summary>
public class TextureConversionTask : IBackgroundTask
{
    private DDSFile dds;
    private readonly Action<DDSFile> callback;
    private readonly TextureOperation[] operations;

    public string Status { get; private set; }
    public bool IsCancelled { get; set; }

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
        var memstream = dds.FileHandler.Stream.ToMemoryStream(true, false);
        var buffer = memstream.GetBuffer();
        var image = DirectXTex.CreateScratchImage();
        var tmpImage = DirectXTex.CreateScratchImage();
        var meta = new Hexa.NET.DirectXTex.TexMetadata();
        try {
            fixed (byte* bufPtr = buffer) {
                DirectXTex.LoadFromDDSMemory(bufPtr, (nuint)memstream.Length, Hexa.NET.DirectXTex.DDSFlags.ForceDx10Ext| Hexa.NET.DirectXTex.DDSFlags.AllowLargeFiles, ref meta, ref image).ThrowIf();
            }

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
                    case RemoveMipMaps:
                        targetMipLevel = 1;
                        break;
                }
            }

            if (((DxgiFormat)meta.Format).IsBlockCompressedFormat()) {
                Status = "Decompressing";
                DirectXTex.Decompress(image.GetImages(), (int)(outputformat.IsBlockCompressedFormat() ? 0 : (DxgiFormat)outputformat), ref tmpImage).ThrowIf();
                SwapImage(ref image, ref tmpImage);
            }

            if (token.IsCancellationRequested) return;

            if (targetMipLevel != -1 && targetMipLevel != (int)meta.MipLevels) {
                if (targetMipLevel == 0 || targetMipLevel > (int)meta.MipLevels) {
                    Status = "Generating MipMaps";
                    DirectXTex.GenerateMipMaps(image.GetImages(), TexFilterFlags.Cubic|TexFilterFlags.SeparateAlpha, (nuint)targetMipLevel, ref tmpImage, false).ThrowIf();
                    SwapImage(ref image, ref tmpImage);
                } else if (targetMipLevel < (uint)meta.MipLevels) {
                    Status = "Removing MipMaps";
                    // Initialize2D + CopyRectangle snippet based on CreateCopyWithEmptyMipMaps() from DirectXTexNet
                    tmpImage.Initialize2D((int)DxgiFormat.R8G8B8A8_UNORM, meta.Width, meta.Height, meta.ArraySize, 1, CPFlags.None).ThrowIf();
                    for (int i = 0; i < (int)meta.ArraySize; ++i) {
                        var rect = new Hexa.NET.DirectXTex.Rect(0, 0, meta.Width, meta.Height);
                        var img = image.GetImage(0, (uint)i, 0);
                        DirectXTex.CopyRectangle(img, ref rect, tmpImage.GetImage(0, (uint)i, 0), TexFilterFlags.Default, 0, 0);
                    }
                    if (targetMipLevel > 1) {
                        Status = "Generating MipMaps";
                        SwapImage(ref image, ref tmpImage);
                        DirectXTex.GenerateMipMaps(image.GetImages(), TexFilterFlags.Cubic|TexFilterFlags.SeparateAlpha, (nuint)targetMipLevel, ref tmpImage, false).ThrowIf();
                    }

                    SwapImage(ref image, ref tmpImage);
                }
            }

            if (token.IsCancellationRequested) return;

            meta = image.GetMetadata();

            if (outputformat != (DxgiFormat)meta.Format) {
                var convFormat = (DxgiFormat)outputformat;

                if (outputformat.IsBlockCompressedFormat()) {
                    if (outputformat is DxgiFormat.BC6H_UF16 or DxgiFormat.BC6H_SF16 or DxgiFormat.BC7_UNORM or DxgiFormat.BC7_UNORM_SRGB && BackgroundResources.Instance.Value!.TryGetD3DDevice(out var d3dDevice)) {
                        // GPU happy path
                        Status = "Compressing (GPU)";
                        DirectXTex.Compress4((ID3D11Device*)d3dDevice, image.GetImages(), image.GetImageCount(), ref meta, (int)convFormat, TexCompressFlags.Default, 0.5f, ref tmpImage).ThrowIf();
                    } else {
                        // CPU fallback
                        Status = "Compressing (CPU)";
                        DirectXTex.Compress2(image.GetImages(), image.GetImageCount(), ref meta, (int)convFormat, TexCompressFlags.Default, 0.5f, ref tmpImage).ThrowIf();
                    }
                } else {
                    Status = "Converting format";
                    DirectXTex.Convert2(image.GetImages(), image.GetImageCount(), ref meta, (int)convFormat, (int)TexCompressFlags.Default, 0.5f, ref tmpImage).ThrowIf();
                }

                SwapImage(ref image, ref tmpImage);
            }

            if (token.IsCancellationRequested) {
                return;
            }

            meta = image.GetMetadata();
            Status = "Finalizing";
            dds.FileHandler.Dispose();
            Stream ddsStream;

            var blob = DirectXTex.CreateBlob();
            try {
                DirectXTex.SaveToDDSMemory2(image.GetImages(), image.GetImageCount(), ref meta, DDSFlags.ForceDx10Ext, ref blob).ThrowIf();
                ddsStream = new DirectxTexBlobStream(blob);
            } catch {
                blob.Release();
                throw;
            }

            dds.FileHandler = new FileHandler(ddsStream, this.dds.FileHandler.FilePath);
            dds.Read();
            dds.FileHandler.Seek(0);

            callback.Invoke(dds);
            Status = "Done";
        } finally {
            image.Release();
            tmpImage.Release();
        }
    }

    private static void SwapImage(ref ScratchImage src, ref ScratchImage tmp)
    {
        (src, tmp) = (tmp, src);
    }

    public abstract class TextureOperation
    {
    }

    public unsafe class DirectxTexBlobStream : UnmanagedMemoryStream
    {
        private readonly Blob blob;

        public DirectxTexBlobStream(Hexa.NET.DirectXTex.Blob blob) : base((byte*)blob.GetBufferPointer(), (uint)blob.GetBufferSize())
        {
            this.blob = blob;
        }

        protected DirectxTexBlobStream()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            blob.Release();
        }
    }

    public class GenerateMipMaps : TextureOperation { }
    public class RemoveMipMaps : TextureOperation { }
    public class ChangeFormat(ReeLib.DDS.DxgiFormat format) : TextureOperation
    {
        public DxgiFormat Format { get; } = format;
    }
}