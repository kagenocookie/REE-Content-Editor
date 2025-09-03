using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.DDS;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ContentEditor.App.Graphics;

public class Texture : IDisposable
{
    private uint _handle;
    private GL _gl;

    public string? Path { get; set; }

    public uint Handle => _handle;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public string? Format { get; set; }

    public enum TextureChannel
    {
        RGBA,
        RGB,
        Red,
        Green,
        Blue,
        Alpha
    }

    public unsafe Texture()
    {
        var gl = EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        _gl = gl;
        _handle = _gl.GenTexture();
        Bind();
    }

    public unsafe Texture(GL? gl = null)
    {
        if (gl == null) {
            gl = EditorWindow.CurrentWindow?.GLContext ?? throw new Exception("Could not get OpenGL Context!");
        }
        _gl = gl;
        _handle = _gl.GenTexture();
        Bind();
    }

    public unsafe Texture LoadFromRawData(Span<byte> data, uint width, uint height)
    {
        fixed (void* d = &data[0]) {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, d);
            SetDefaultParameters();
        }
        return this;
    }

    public unsafe Texture LoadFromFile(FileHandle file)
    {
        return LoadFromFile(file.Stream, file.Filepath);
    }

    public unsafe Texture LoadFromFile(string filepath)
    {
        using var stream = System.IO.File.OpenRead(filepath);
        return LoadFromFile(stream, filepath);
    }

    private unsafe Texture LoadFromFile(Stream stream, string filepath)
    {
        Path = filepath;
        if (filepath.EndsWith(".dds")) {
            return LoadFromDDS(stream);
        }

        var fmt = PathUtils.ParseFileFormat(filepath);
        if (fmt.format == KnownFileFormats.Texture) {
            return LoadFromTex(stream, filepath);
        }

        return LoadFromStream(stream);
    }

    /// <summary>
    /// Load a standard image file format from a stream. This method does not support .dds or .tex files.
    /// </summary>
    public unsafe Texture LoadFromStream(Stream stream)
    {
        using (var img = Image.Load<Rgba32>(stream)) {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);

            img.ProcessPixelRows(accessor => {
                for (int y = 0; y < accessor.Height; y++) {
                    fixed (void* data = accessor.GetRowSpan(y)) {
                        _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, y, (uint)accessor.Width, 1, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    }
                }
            });
            Width = img.Width;
            Height = img.Height;
        }

        SetDefaultParameters();
        return this;
    }

    public Texture LoadFromDDS(Stream stream)
    {
        var dds = new DDSFile(new FileHandler(stream));
        if (!dds.Read()) {
            throw new Exception("Failed to read DDS header");
        }

        var it = dds.CreateMipMapIterator();
        if (it.isCompressed) {
            LoadCompressedDDSMipMaps(ref it, dds.Header.DX10.Format);
        } else {
            LoadUncompressedDDSMipMaps(ref it, dds.Header.DX10.Format);
        }
        it.Dispose();
        Width = (int)dds.Header.width;
        Height = (int)dds.Header.height;
        Format = dds.Header.DX10.Format.ToString();
        return this;
    }

    public unsafe Texture LoadFromTex(Stream stream, string filename)
    {
        var tex = new TexFile(new FileHandler(stream, filename));
        if (!tex.Read()) {
            throw new Exception("Failed to read TEX header");
        }

        return LoadFromTex(tex);
    }

    public unsafe Texture LoadFromTex(TexFile tex)
    {
        Path = tex.FileHandler.FilePath;
        if (!tex.Header.IsPowerOfTwo && tex.Header.format.IsBlockCompressedFormat()) {
            var it = tex.CreateNonPo2Iterator();
            LoadCompressedTexMipMaps(ref it, tex.Header.format);
            it.Dispose();
        } else {
            var it = tex.CreateMipMapIterator();
            if (it.isCompressed) {
                LoadCompressedDDSMipMaps(ref it, tex.Header.format);
            } else {
                LoadUncompressedDDSMipMaps(ref it, tex.Header.format);
            }
            it.Dispose();
        }
        Width = tex.Header.width;
        Height = tex.Header.height;
        Format = tex.Header.format.ToString();
        return this;
    }

    private unsafe void LoadCompressedDDSMipMaps(ref DDSFile.DdsMipMapIterator iterator, DxgiFormat compressedFormat)
    {
        // https://www.oldunreal.com/editing/s3tc/ARB_texture_compression.pdf
        var data = new DDSFile.MipMapLevelData();
        var intFormat = DxgiToGLInternalFormat(compressedFormat);
        // Logger.Debug("Loading DDS with format " + compressedFormat + " => " + intFormat);
        var mips = 0;
        while (iterator.Next(ref data)) {
            fixed (byte* bytes = data.data) {
                _gl.CompressedTexImage2D(TextureTarget.Texture2D, mips++, intFormat, data.width, data.height, 0, (uint)data.data.Length, bytes);
            }
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mips - 1);
    }

    private unsafe void LoadUncompressedDDSMipMaps(ref DDSFile.DdsMipMapIterator iterator, DxgiFormat format)
    {
        var fmt = GetFormatInfo(format);
        var data = new DDSFile.MipMapLevelData();
        // Logger.Debug("Loading DDS with format " + format + " => " + fmt.internalFormat);
        var mips = 0;
        var internalFormat = fmt.internalFormat;
        while (iterator.Next(ref data)) {
            fixed (byte* bytes = data.data) {
                _gl.TexImage2D(TextureTarget.Texture2D, mips++, internalFormat, data.width, data.height, 0, fmt.pixelFormat, fmt.pixelType, bytes);
            }
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mips - 1);
    }

    private unsafe void LoadCompressedTexMipMaps(ref TexFile.TexMipMapIterator iterator, DxgiFormat compressedFormat)
    {
        var data = new DDSFile.MipMapLevelData();
        var intFormat = DxgiToGLInternalFormat(compressedFormat);
        // Logger.Debug("Loading DDS with format " + compressedFormat + " => " + intFormat);
        var mips = 0;
        while (iterator.Next(ref data)) {
            fixed (byte* bytes = data.data) {
                _gl.CompressedTexImage2D(TextureTarget.Texture2D, mips++, intFormat, data.width, data.height, 0, (uint)data.data.Length, bytes);
            }
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mips - 1);
    }

    private unsafe Image<Rgba32> GetCurrentTexture()
    {
        var data = new byte[Width * Height * 4];

        Bind();

        fixed (byte* bytes = data) {
            _gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, bytes);
        }

        var image = Image.LoadPixelData<Rgba32>(data, Width, Height);
        return image;
    }
    public void SaveAs(string filepath)
    {
        using var image = GetCurrentTexture();
        if (image != null) {
            image.Save(filepath, new SixLabors.ImageSharp.Formats.Tga.TgaEncoder {
                BitsPerPixel = SixLabors.ImageSharp.Formats.Tga.TgaBitsPerPixel.Pixel32,
                Compression = SixLabors.ImageSharp.Formats.Tga.TgaCompression.None
            });
            Logger.Info($"Texture saved as TGA to: {filepath}");
        }
    }

    private void SetDefaultParameters()
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 8);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
    }

    public void SetChannel(TextureChannel channel)
    {
        switch (channel) {
            case TextureChannel.RGBA:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Green);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Blue);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Alpha);
                break;

            case TextureChannel.RGB:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Green);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Blue);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                break;

            case TextureChannel.Red:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                break;

            case TextureChannel.Green:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Green);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Green);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Green);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                break;

            case TextureChannel.Blue:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Blue);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Blue);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Blue);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                break;

            case TextureChannel.Alpha:
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Alpha);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Alpha);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Alpha);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.One);
                break;
        }
    }

    public void Bind(TextureUnit textureSlot = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(textureSlot);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_handle);
    }

    public override string ToString() => Path ?? _handle.ToString();

    public static implicit operator nint(Texture tex) => (nint)tex.Handle;

#region ENUMS
    private static InternalFormat DxgiToGLInternalFormat(DxgiFormat dxgi) => dxgi switch {
        DxgiFormat.BC7_UNORM => InternalFormat.CompressedRgbaBptcUnorm,
        DxgiFormat.BC7_UNORM_SRGB => InternalFormat.CompressedRgbaBptcUnorm,
        // DxgiFormat.BC7_UNORM_SRGB => InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext,
        DxgiFormat.BC4_UNORM => InternalFormat.CompressedRedRgtc1,
        DxgiFormat.BC3_UNORM or DxgiFormat.BC3_UNORM_SRGB => InternalFormat.CompressedRgbaS3TCDxt5Ext,

        DxgiFormat.R16G16B16A16_FLOAT => InternalFormat.Rgba32f,

        // untested
        DxgiFormat.BC1_UNORM_SRGB => InternalFormat.CompressedRgbaS3TCDxt1Ext,
        DxgiFormat.BC1_UNORM => InternalFormat.CompressedRgbaS3TCDxt1Ext,
        DxgiFormat.BC2_UNORM or DxgiFormat.BC2_UNORM_SRGB => InternalFormat.CompressedRgbaS3TCDxt3Ext,
        _ => InternalFormat.CompressedRgba,
    };

    private static InternalFormat GetGLCompressionEnum(DDSFourCC fourcc) => fourcc switch {
        DDSFourCC.DXT1 => InternalFormat.CompressedRgbaS3TCDxt1Ext,
        DDSFourCC.DXT4 => InternalFormat.CompressedRgbaS3TCDxt3Ext,
        DDSFourCC.DXT5 or DDSFourCC.DX10 => InternalFormat.CompressedRgbaS3TCDxt5Ext,
        _ => InternalFormat.CompressedRgbS3TCDxt1Ext,
    };

    private record struct FormatInfo(InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType);

    private static FormatInfo GetFormatInfo(DxgiFormat format) => format switch {
        DxgiFormat.R32G32B32A32_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Int),
        DxgiFormat.R32G32B32A32_FLOAT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Float),
        DxgiFormat.R32G32B32A32_UINT  => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
        DxgiFormat.R32G32B32A32_SINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Int),
        DxgiFormat.R32G32B32_TYPELESS => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.Int),
        DxgiFormat.R32G32B32_FLOAT => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.Float),
        DxgiFormat.R32G32B32_UINT  => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.UnsignedInt),
        DxgiFormat.R32G32B32_SINT => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.Int),
        DxgiFormat.R32G32_TYPELESS => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Int),
        DxgiFormat.R32G32_FLOAT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Float),
        DxgiFormat.R32G32_UINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Int),
        DxgiFormat.R32G32_SINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedInt),
        DxgiFormat.R32_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Int),
        DxgiFormat.R32_FLOAT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Float),
        DxgiFormat.R32_UINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedInt),
        DxgiFormat.R32_SINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Int),
		DxgiFormat.D32_FLOAT => new FormatInfo(InternalFormat.DepthStencil, PixelFormat.DepthStencil, PixelType.Float),
        DxgiFormat.R16G16B16A16_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Short),
        DxgiFormat.R16G16B16A16_FLOAT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.HalfFloat),
        DxgiFormat.R16G16B16A16_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedShort),
        DxgiFormat.R16G16B16A16_UINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedShort),
        DxgiFormat.R16G16B16A16_SNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Short),
        DxgiFormat.R16G16B16A16_SINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Short),
        DxgiFormat.R16G16_TYPELESS => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Short),
        DxgiFormat.R16G16_FLOAT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.HalfFloat),
		DxgiFormat.R16G16_UNORM => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Byte),
        DxgiFormat.R16G16_UINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedShort),
		DxgiFormat.R16G16_SNORM => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Byte),
        DxgiFormat.R16G16_SINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Short),
        DxgiFormat.R16_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Short),
        DxgiFormat.R16_FLOAT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.HalfFloat),
		DxgiFormat.R16_UNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedShort),
        DxgiFormat.R16_UINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedShort),
        DxgiFormat.R16_SNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Short),
        DxgiFormat.R16_SINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Short),
		DxgiFormat.D16_UNORM => new FormatInfo(InternalFormat.DepthStencil, PixelFormat.DepthStencil, PixelType.UnsignedShort),
		DxgiFormat.R32G8X24_TYPELESS => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedInt),
		DxgiFormat.D32_FLOAT_S8X24_UINT => new FormatInfo(InternalFormat.DepthStencil, PixelFormat.DepthStencil, PixelType.Float),
		DxgiFormat.R32_FLOAT_X8X24_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedInt),
		DxgiFormat.X32_TYPELESS_G8X24_UINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Green, PixelType.Byte),
		DxgiFormat.R10G10B10A2_TYPELESS => new FormatInfo(InternalFormat.Rgb10A2, PixelFormat.Rgba, PixelType.UnsignedInt1010102),
		DxgiFormat.R10G10B10A2_UNORM => new FormatInfo(InternalFormat.Rgb10A2, PixelFormat.Rgba, PixelType.UnsignedInt1010102),
		DxgiFormat.R10G10B10A2_UINT => new FormatInfo(InternalFormat.Rgb10A2, PixelFormat.Rgba, PixelType.UnsignedInt1010102),
		DxgiFormat.R11G11B10_FLOAT => new FormatInfo(InternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.Float),
		DxgiFormat.R8G8B8A8_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Byte),
		DxgiFormat.R8G8B8A8_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedByte),
		DxgiFormat.R8G8B8A8_UNORM_SRGB => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedByte),
		DxgiFormat.R8G8B8A8_UINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedByte),
		DxgiFormat.R8G8B8A8_SNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Byte),
		DxgiFormat.R8G8B8A8_SINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Byte),
		DxgiFormat.R24G8_TYPELESS => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedInt248),
		DxgiFormat.D24_UNORM_S8_UINT => new FormatInfo(InternalFormat.DepthStencil, PixelFormat.DepthStencil, PixelType.UnsignedInt248),
		DxgiFormat.R24_UNORM_X8_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.RedExt, PixelType.UnsignedInt248),
		DxgiFormat.X24_TYPELESS_G8_UINT => new FormatInfo(InternalFormat.Rgba, PixelFormat.Alpha, PixelType.UnsignedInt248),
		DxgiFormat.R8G8_TYPELESS => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedByte),
		DxgiFormat.R8G8_UNORM => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedByte),
		DxgiFormat.R8G8_UINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.UnsignedByte),
		DxgiFormat.R8G8_SNORM => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Byte),
		DxgiFormat.R8G8_SINT => new FormatInfo(InternalFormat.RG, PixelFormat.RG, PixelType.Byte),
		DxgiFormat.R8_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedByte),
		DxgiFormat.R8_UNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedByte),
		DxgiFormat.R8_UINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedByte),
		DxgiFormat.R8_SNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Byte),
		DxgiFormat.R8_SINT => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Byte),
		DxgiFormat.A8_UNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Alpha, PixelType.UnsignedByte),
		DxgiFormat.R1_UNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedByte),
		DxgiFormat.R9G9B9E5_SHAREDEXP => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.Float),
		DxgiFormat.R8G8_B8G8_UNORM => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.UnsignedByte),
		DxgiFormat.G8R8_G8B8_UNORM => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.UnsignedByte),

		DxgiFormat.BC1_TYPELESS => new FormatInfo(InternalFormat.Rgb, PixelFormat.Rgb, PixelType.UnsignedInt),
		DxgiFormat.BC1_UNORM => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt1Ext, PixelFormat.Rgb, PixelType.UnsignedInt),
		DxgiFormat.BC1_UNORM_SRGB => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt1Ext, PixelFormat.Rgb, PixelType.UnsignedInt),
		DxgiFormat.BC2_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC2_UNORM => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt3Ext, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC2_UNORM_SRGB => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt3Ext, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC3_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC3_UNORM => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt3Ext, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC3_UNORM_SRGB => new FormatInfo(InternalFormat.CompressedRgbaS3TCDxt3Ext, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC4_TYPELESS => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.UnsignedInt),
		DxgiFormat.BC4_UNORM => new FormatInfo(InternalFormat.CompressedRedRgtc1, PixelFormat.Red, PixelType.UnsignedInt),
		DxgiFormat.BC4_SNORM => new FormatInfo(InternalFormat.Red, PixelFormat.Red, PixelType.Int),
		DxgiFormat.BC5_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC5_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC5_SNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Int),
		DxgiFormat.BC6H_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedShort),
		DxgiFormat.BC6H_UF16 => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedShort),
		DxgiFormat.BC6H_SF16 => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.Short),
		DxgiFormat.BC7_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC7_UNORM => new FormatInfo(InternalFormat.CompressedRgbaBptcUnorm, PixelFormat.Rgba, PixelType.UnsignedInt),
		DxgiFormat.BC7_UNORM_SRGB => new FormatInfo(InternalFormat.CompressedRgbaBptcUnorm, PixelFormat.Rgba, PixelType.UnsignedInt),

		DxgiFormat.B5G6R5_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgr, PixelType.UnsignedShort565),
		DxgiFormat.B5G5R5A1_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedShort5551),
		DxgiFormat.B8G8R8A8_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedInt8888),
		DxgiFormat.B8G8R8X8_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.BgrExt, PixelType.UnsignedInt8888),
		DxgiFormat.R10G10B10_XR_BIAS_A2_UNORM => new FormatInfo(InternalFormat.Rgba, PixelFormat.Rgba, PixelType.UnsignedInt1010102),
		DxgiFormat.B8G8R8A8_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedInt8888),
		DxgiFormat.B8G8R8A8_UNORM_SRGB => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedInt8888),
		DxgiFormat.B8G8R8X8_TYPELESS => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedInt8888Ext),
		DxgiFormat.B8G8R8X8_UNORM_SRGB => new FormatInfo(InternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedInt8888Ext),
        _ => throw new NotImplementedException(),
    };
#endregion
}
