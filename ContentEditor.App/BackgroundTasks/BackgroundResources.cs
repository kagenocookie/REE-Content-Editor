using System.Diagnostics;
using ContentEditor.App;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace ContentEditor.BackgroundTasks;

/// <summary>
/// Provides per-thread access to "global" disposable resources.
/// </summary>
public class BackgroundResources : IDisposable
{
    public static ThreadLocal<BackgroundResources> Instance { get; } = new ThreadLocal<BackgroundResources>(() => new BackgroundResources());

    private static WindowOptions _workerWindowOptions = WindowOptions.Default with {
        IsVisible = false,
        ShouldSwapAutomatically = false,
        Size = new Silk.NET.Maths.Vector2D<int>(1, 1),
    };

    private IWindow? _workerWindow;
    private GL? _gl;
    private IntPtr? d3dDevice;

    private BackgroundResources()
    {
    }

    public GL GetGL()
    {
        if (_workerWindow == null || _gl == null) {
            var newWindow = Window.Create(_workerWindowOptions);
            Interlocked.CompareExchange(ref _workerWindow, newWindow, _workerWindow);
            _workerWindow = Window.Create(_workerWindowOptions);
            _workerWindow.Load += () => {
                _gl = _workerWindow.CreateOpenGL();
            };
            _workerWindow.Initialize();
            _workerWindow.MakeCurrent();
            Debug.Assert(_gl != null);
        }

        return _gl;
    }

    public bool TryGetD3DDevice(out IntPtr d3dDevicePtr)
    {
        if (d3dDevice != null) {
            if (d3dDevice.HasValue) {
                d3dDevicePtr = d3dDevice.Value;
                return true;
            }
            d3dDevicePtr = IntPtr.Zero;
            return false;
        }

        if (PlatformUtils.CreateD3D11Context(out var device)) {
            d3dDevice = d3dDevicePtr = device;
            return true;
        }

        d3dDevice = IntPtr.Zero;
        d3dDevicePtr = IntPtr.Zero;
        return false;
    }

    public unsafe void ReleaseResources()
    {
        if (_gl != null) {
            _gl.Dispose();
            _gl = null;
        }
        if (_workerWindow != null) {
            _workerWindow.Dispose();
            _workerWindow = null;
        }
        if (d3dDevice.HasValue) {
            void** vtbl = *(void***)d3dDevice.Value.ToPointer();
            // Release() is supposed to be the 3rd vtable entry
            var releaseFunc = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[2];
            releaseFunc(d3dDevice.Value);
            d3dDevice = null;
            Console.WriteLine("Releasing dx11");
        }
    }

    public void Dispose()
    {
        ReleaseResources();
    }
}