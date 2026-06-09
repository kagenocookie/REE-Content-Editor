using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.via;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ContentEditor.App.Graphics;

public sealed class FilePreviewGenerator : IDisposable
{
    public string PreviewCacheFolder { get; }

    private readonly ConcurrentQueue<(string path, ulong hash)> _pendingPreviews = new();
    private readonly ContentWorkspace workspace;
    private readonly ConcurrentDictionary<ulong, Texture> _loadedPreviews = new();
    private readonly ConcurrentDictionary<ulong, PreviewImageStatus> _statuses = new();

    private static readonly Int2 ThumbnailSize = new Int2(256, 256);
    private readonly GL _mainGl;
    private readonly string[]? pakFiles;
    private static WindowOptions _workerWindowOptions = WindowOptions.Default with {
        IsVisible = false,
        ShouldSwapAutomatically = false,
        Size = new Silk.NET.Maths.Vector2D<int>(1, 1),
    };
    private IWindow? _workerWindow;
    private GL _threadGL = null!;

    private bool _cancelRequested;
    private bool _stopRequested;
    private bool _threadRunning;

    private Thread? thread;

    public FilePreviewGenerator(ContentWorkspace originalWorkspace, GL gl, string[]? pakFiles)
    {
        this._mainGl = gl;
        this.pakFiles = pakFiles;
        PreviewCacheFolder = Path.Combine(AppConfig.Instance.ThumbnailCacheFilepath.Get()!, originalWorkspace.Game.name);
        workspace = originalWorkspace.CreateTempClone();
        if (originalWorkspace.CurrentBundle != null) {
            workspace.SetBundle(originalWorkspace.CurrentBundle.Name);
        }
        workspace.ResourceManager.SetupFileLoaders(typeof(MeshLoader).Assembly);
        // we manually load tex files so we can skip unnecessary mipmaps
        workspace.ResourceManager.RemoveFileLoader<TexFileLoader>();
        if (pakFiles != null) {
            workspace.Env.PakReader.PakFilePriority = pakFiles.ToList();
        }
    }

    public void CancelCurrentQueue()
    {
        _cancelRequested = true;
        _pendingPreviews.Clear();
        foreach (var item in _statuses) {
            if (item.Value is PreviewImageStatus.Pending) {
                _statuses.TryRemove(item);
            }
        }
    }

    private string GetThumbnailPath(ulong hash)
    {
        var hex = hash.ToString("X16");
        var mainFolder = hex.Substring(0, 2);
        var fn = hex.AsSpan(2);
        return Path.Combine(PreviewCacheFolder, mainFolder, string.Concat(fn, ".jpg")).NormalizeFilepath();
    }

    private static ulong CombineHash(ulong pathHash, ulong extraHash)
    {
        var low = AppUtils.StableHashCombine((uint)pathHash, (uint)extraHash);
        var high = AppUtils.StableHashCombine((uint)(pathHash >> 32), (uint)(extraHash >> 32));
        return low | ((ulong)high << 32);
    }

    public PreviewImageStatus FetchPreview(string file, out Texture? texture, string? discriminator = null)
    {
        file = PathUtils.GetNonStreamingPath(file.NormalizeFilepath()).ToString();
        var fileHash = MurMur3HashUtils.GetPakFilepathHash(file);
        if (discriminator != null) {
            fileHash = CombineHash(fileHash, MurMur3HashUtils.GetPakFilepathHash(discriminator));
        }
        if (workspace.CurrentBundle?.ContainsResource(file) == true) {
            fileHash = CombineHash(fileHash, MurMur3HashUtils.GetPakFilepathHash(workspace.CurrentBundle.Name));
        }
        texture = null;
        if (_statuses.TryGetValue(fileHash, out var status)) {
            if (status == PreviewImageStatus.Generated) {
                if (LoadGeneratedThumbnail(fileHash, out texture)) {
                    return PreviewImageStatus.Ready;
                } else {
                    _statuses[fileHash] = PreviewImageStatus.Failed;
                }
            }
            if (status == PreviewImageStatus.Ready) {
                texture = _loadedPreviews[fileHash];
            }
            return status;
        }

        var fmt = PathUtils.ParseFileFormat(file);
        if (fmt.format is KnownFileFormats.Texture or KnownFileFormats.Mesh) {
            _statuses[fileHash] = PreviewImageStatus.Pending;
            EnqueueFile(file, fileHash);
            return PreviewImageStatus.Pending;
        } else {
            return _statuses[fileHash] = AppIcons.GetIcon(fmt.format).icon == '\0' ? PreviewImageStatus.Unsupported : PreviewImageStatus.PredefinedIcon;
        }
    }

    private void EnqueueFile(string file, ulong pathHash)
    {
        if (_pendingPreviews.IsEmpty) {
            _cancelRequested = true;
            thread?.Join();
            _cancelRequested = false;
            _stopRequested = false;
            thread = new Thread(RunPreviewGenerationQueue);
            thread.Start();
        }
        _pendingPreviews.Enqueue((file, pathHash));
    }

    private bool LoadGeneratedThumbnail(ulong hash, out Texture? texture)
    {
        var path = GetThumbnailPath(hash);
        using var fs = File.OpenRead(path);
        texture = new Texture(_mainGl);
        texture.LoadFromStream(fs, false);
        _loadedPreviews[hash] = texture;
        _statuses[hash] = PreviewImageStatus.Ready;
        return true;
    }

    private void RunPreviewGenerationQueue()
    {
        while (_threadRunning) {
            Thread.Sleep(1);
        }
        _threadRunning = true;

        if (_workerWindow == null) {
            var newWindow = Window.Create(_workerWindowOptions);
            Interlocked.CompareExchange(ref _workerWindow, newWindow, _workerWindow);
            _workerWindow = Window.Create(_workerWindowOptions);
            _workerWindow.Load += () => {
                _threadGL = _workerWindow.CreateOpenGL();
            };
            _workerWindow.Initialize();
            _workerWindow.MakeCurrent();
            Debug.Assert(_threadGL != null);
        }

        try {
            while (!_stopRequested && !_cancelRequested && _pendingPreviews.TryDequeue(out var queueItem)) {
                var (path, hash) = queueItem;
                var thumbPath = GetThumbnailPath(hash);
                if (File.Exists(thumbPath)) {
                    // TODO verify content hash for changes somehow?
                    _statuses[hash] = PreviewImageStatus.Generated;
                    continue;
                }

                var fmt = PathUtils.ParseFileFormat(path).format;
                FileHandle f;
                try {
                    if (!workspace.ResourceManager.TryGetOrLoadFile(path, out f!)) {
                        _statuses[hash] = PreviewImageStatus.Failed;
                        continue;
                    }
                } catch (Exception e) {
                    _statuses[hash] = PreviewImageStatus.Failed;
                    Logger.Error($"Could not load file {path} for preview generation ({e.Message})");
                    continue;
                }
                if (_stopRequested || _cancelRequested) break;
                if (f == null) {
                    Logger.Debug("Failed to resolve preview file: " + path);
                    _statuses[hash] = PreviewImageStatus.Failed;
                    continue;
                }

                try {
                    switch (fmt) {
                        case KnownFileFormats.Texture:
                            GenerateTextureThumbnail(path, hash, f.Stream);
                            break;
                        case KnownFileFormats.Mesh:
                            GenerateMeshThumbnail(path, hash);
                            break;
                        default:
                            _statuses[hash] = PreviewImageStatus.Failed;
                            break;
                    }
                } catch (Exception e) {
                    Logger.Debug("Failed to generate file thumbnail: " + e.Message);
                    _statuses[hash] = PreviewImageStatus.Failed;
                }
                workspace.ResourceManager.CloseAllFiles();
            }
        } finally {
            _workerWindow?.Dispose();
            _workerWindow = null;
            _threadRunning = false;
            _cancelRequested = false;
        }
    }

    private void GenerateTextureThumbnail(string path, ulong hash, Stream stream)
    {
        _statuses[hash] = PreviewImageStatus.Loading;
        var texFile = new TexFile(new FileHandler(stream, path));
        texFile.Read();
        var totalMips = texFile.Header.mipCount;
        var targetMip = texFile.GetBestMipLevelForDimensions(ThumbnailSize.x, ThumbnailSize.y);
        var outPath = GetThumbnailPath(hash);

        using var fullTexture = new Texture(_threadGL);
        fullTexture.LoadFromTex(texFile);

        using var img = fullTexture.GetAsImage(targetMip);
        img.ProcessPixelRows(accessor => {
            for (int y = 0; y < accessor.Height; y++) {
                Span<Rgba32> data = accessor.GetRowSpan(y);
                for (int i = 0; i < data.Length; ++i) data[i] = data[i] with { A = 255 };
            }
        });
        img.Mutate(x => x.Resize(ThumbnailSize.x, ThumbnailSize.y));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        img.SaveAsJpeg(outPath, null);
        _statuses[hash] = PreviewImageStatus.Generated;
    }

    private void GenerateMeshThumbnail(string path, ulong hash)
    {
        _statuses[hash] = PreviewImageStatus.Loading;
        var outPath = GetThumbnailPath(hash);

        using var tmpScene = new Scene("_preview", "", workspace, null, null, _threadGL);
        tmpScene.SetActive(true);
        tmpScene.ActiveCamera.ProjectionMode = CameraProjection.Orthographic;

        var go = new GameObject("mesh", workspace.Env, tmpScene.RootFolder, tmpScene);
        var comp = go.AddComponent<MeshComponent>();
        var basePath = PathUtils.GetFilepathWithoutExtensionOrVersion(path).ToString();
        workspace.ResourceManager.TryResolveGameFile(basePath + ".mdf2", out var mdfHandle);
        if (mdfHandle == null) workspace.ResourceManager.TryResolveGameFile(basePath + "_Mat.mdf2", out mdfHandle);
        if (mdfHandle == null) workspace.ResourceManager.TryResolveGameFile(basePath + "_00.mdf2", out mdfHandle);
        if (mdfHandle == null) workspace.ResourceManager.TryResolveGameFile(basePath + "_A.mdf2", out mdfHandle);

        comp.SetMesh(path, mdfHandle?.Filepath);
        if (comp.MeshHandle == null) {
            _statuses[hash] = PreviewImageStatus.Failed;
            Logger.Debug($"Failed to generate thumbnail for mesh " + path);
            return;
        }

        tmpScene.ActiveCamera.LookAt(go, true);
        tmpScene.ActiveCamera.OrthoSize = go.GetWorldSpaceBounds().Size.Length() * 0.8f;
        tmpScene.OwnRenderContext.SetRenderToTexture(new Vector2(ThumbnailSize.x, ThumbnailSize.y));
        tmpScene.Render(0);
        _threadGL.Flush();
        var texture = new Texture(tmpScene.OwnRenderContext.RenderTargetTextureHandle, _threadGL, ThumbnailSize.x, ThumbnailSize.y);
        using var img = texture.GetAsImage(0);
        img.Mutate(x => x.Flip(FlipMode.Vertical));
        tmpScene.SetActive(false);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        img.SaveAsJpeg(outPath, null);

        _statuses[hash] = PreviewImageStatus.Generated;
    }

    public void Dispose()
    {
        CancelCurrentQueue();
        foreach (var pv in _loadedPreviews) {
            pv.Value.Dispose();
        }

        _loadedPreviews.Clear();
        _stopRequested = true;
    }
}

public enum PreviewImageStatus
{
    Unsupported,
    Pending,
    Loading,
    Generated,
    Ready,
    PredefinedIcon,
    Failed,
}
