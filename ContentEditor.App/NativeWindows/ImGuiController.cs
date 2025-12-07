// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.Hexa.ImGui;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace Silk.NET.OpenGL.Extensions.Hexa.ImGui;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

public class ImGuiController : IDisposable
{
    private GL _gl;
    private IView _view;
    private IInputContext _input;
    private bool _frameBegun;
    private readonly List<char> _pressedChars = new();
    private IKeyboard _keyboard;

    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;
    private uint _vboHandle;
    private uint _elementsHandle;
    private uint _vertexArrayObject;

    private ContentEditor.App.Graphics.Shader _shader;

    private int _windowWidth;
    private int _windowHeight;

    public ImGuiContextPtr Context;

    /// <summary>
    /// Constructs a new ImGuiController.
    /// </summary>
    public ImGuiController(GL gl, IView view, IInputContext input) : this(gl, view, input, null)
    {
    }

    /// <summary>
    /// Constructs a new ImGuiController with font configuration and onConfigure Action.
    /// </summary>
    public ImGuiController(GL gl, IView view, IInputContext input, Action? onConfigureIO = null)
    {
        Init(gl, view, input);

        var io = global::Hexa.NET.ImGui.ImGui.GetIO();

        onConfigureIO?.Invoke();

        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;

        CreateDeviceResources();

        SetPerFrameImGuiData(1f / 60f);

        BeginFrame();
    }

    public void MakeCurrent()
    {
        global::Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
    }

    private void Init(GL gl, IView view, IInputContext input)
    {
        _gl = gl;
        _view = view;
        _input = input;
        _windowWidth = view.Size.X;
        _windowHeight = view.Size.Y;

        Context = global::Hexa.NET.ImGui.ImGui.CreateContext();
        global::Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
        global::Hexa.NET.ImGui.ImGui.StyleColorsDark();
    }

    private void BeginFrame()
    {
        global::Hexa.NET.ImGui.ImGui.NewFrame();
        _frameBegun = true;
        _keyboard = _input.Keyboards[0];
        _view.Resize += WindowResized;
        _keyboard.KeyDown += OnKeyDown;
        _keyboard.KeyUp += OnKeyUp;
        _keyboard.KeyChar += OnKeyChar;
    }

    /// <summary>
    /// Delegate to receive keyboard key down events.
    /// </summary>
    /// <param name="keyboard">The keyboard context generating the event.</param>
    /// <param name="keycode">The native keycode of the pressed key.</param>
    /// <param name="scancode">The native scancode of the pressed key.</param>
    private static void OnKeyDown(IKeyboard keyboard, Key keycode, int scancode) =>
        OnKeyEvent(keyboard, keycode, scancode, down: true);

    /// <summary>
    /// Delegate to receive keyboard key up events.
    /// </summary>
    /// <param name="keyboard">The keyboard context generating the event.</param>
    /// <param name="keycode">The native keycode of the released key.</param>
    /// <param name="scancode">The native scancode of the released key.</param>
    private static void OnKeyUp(IKeyboard keyboard, Key keycode, int scancode) =>
        OnKeyEvent(keyboard, keycode, scancode, down: false);

    /// <summary>
    /// Delegate to receive keyboard key events.
    /// </summary>
    /// <param name="keyboard">The keyboard context generating the event.</param>
    /// <param name="keycode">The native keycode of the key generating the event.</param>
    /// <param name="scancode">The native scancode of the key generating the event.</param>
    /// <param name="down">True if the event is a key down event, otherwise False</param>
    private static void OnKeyEvent(IKeyboard keyboard, Key keycode, int scancode, bool down)
    {
        var io = global::Hexa.NET.ImGui.ImGui.GetIO();
        var imGuiKey = TranslateInputKeyToImGuiKey(keycode);
        if (imGuiKey != ImGuiKey.None) {
            io.AddKeyEvent(imGuiKey, down);
            io.SetKeyEventNativeData(imGuiKey, (int)keycode, scancode);
        }
    }

    private void OnKeyChar(IKeyboard arg1, char arg2)
    {
        _pressedChars.Add(arg2);
    }

    private void WindowResized(Vector2D<int> size)
    {
        _windowWidth = size.X;
        _windowHeight = size.Y;
    }

    /// <summary>
    /// Renders the ImGui draw list data.
    /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
    /// or index data has increased beyond the capacity of the existing buffers.
    /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
    /// </summary>
    public void Render()
    {
        if (_frameBegun) {
            var oldCtx = global::Hexa.NET.ImGui.ImGui.GetCurrentContext();

            if (oldCtx != Context) {
                global::Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            }

            _frameBegun = false;
            global::Hexa.NET.ImGui.ImGui.Render();
            RenderImDrawData(global::Hexa.NET.ImGui.ImGui.GetDrawData());

            if (oldCtx != Context) {
                global::Hexa.NET.ImGui.ImGui.SetCurrentContext(oldCtx);
            }
        }
    }

    /// <summary>
    /// Updates ImGui input and IO configuration state.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        var oldCtx = global::Hexa.NET.ImGui.ImGui.GetCurrentContext();

        if (oldCtx != Context) {
            global::Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
        }

        if (_frameBegun) {
            global::Hexa.NET.ImGui.ImGui.Render();
        }

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput();

        _frameBegun = true;
        global::Hexa.NET.ImGui.ImGui.NewFrame();

        if (oldCtx != Context) {
            global::Hexa.NET.ImGui.ImGui.SetCurrentContext(oldCtx);
        }
    }

    /// <summary>
    /// Sets per-frame data based on the associated window.
    /// This is called by Update(float).
    /// </summary>
    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = global::Hexa.NET.ImGui.ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth, _windowHeight);

        if (_windowWidth > 0 && _windowHeight > 0) {
            io.DisplayFramebufferScale = new Vector2(_view.FramebufferSize.X / _windowWidth,
                _view.FramebufferSize.Y / _windowHeight);
        }

        io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
    }

    private void UpdateImGuiInput()
    {
        var io = global::Hexa.NET.ImGui.ImGui.GetIO();

        var mouseState = _input.Mice[0];

        io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
        io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
        io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

        var point = new Point((int)mouseState.Position.X, (int)mouseState.Position.Y);
        io.MousePos = new Vector2(point.X, point.Y);

        var wheel = mouseState.ScrollWheels[0];
        io.MouseWheel = wheel.Y;
        io.MouseWheelH = wheel.X;

        foreach (var c in _pressedChars) {
            io.AddInputCharacter(c);
        }

        _pressedChars.Clear();

        io.KeyCtrl = _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight);
        io.KeyAlt = _keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight);
        io.KeyShift = _keyboard.IsKeyPressed(Key.ShiftLeft) || _keyboard.IsKeyPressed(Key.ShiftRight);
        io.KeySuper = _keyboard.IsKeyPressed(Key.SuperLeft) || _keyboard.IsKeyPressed(Key.SuperRight);
    }

    internal void PressChar(char keyChar)
    {
        _pressedChars.Add(keyChar);
    }

    /// <summary>
    /// Translates a Silk.NET.Input.Key to an ImGuiKey.
    /// </summary>
    /// <param name="key">The Silk.NET.Input.Key to translate.</param>
    /// <returns>The corresponding ImGuiKey.</returns>
    private static ImGuiKey TranslateInputKeyToImGuiKey(Key key)
    {
        return key switch {
            Key.Tab => ImGuiKey.Tab,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Apostrophe => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Minus => ImGuiKey.Minus,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Equal => ImGuiKey.Equal,
            Key.LeftBracket => ImGuiKey.LeftBracket,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.RightBracket => ImGuiKey.RightBracket,
            Key.GraveAccent => ImGuiKey.GraveAccent,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.NumLock => ImGuiKey.NumLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.Keypad0 => ImGuiKey.Keypad0,
            Key.Keypad1 => ImGuiKey.Keypad1,
            Key.Keypad2 => ImGuiKey.Keypad2,
            Key.Keypad3 => ImGuiKey.Keypad3,
            Key.Keypad4 => ImGuiKey.Keypad4,
            Key.Keypad5 => ImGuiKey.Keypad5,
            Key.Keypad6 => ImGuiKey.Keypad6,
            Key.Keypad7 => ImGuiKey.Keypad7,
            Key.Keypad8 => ImGuiKey.Keypad8,
            Key.Keypad9 => ImGuiKey.Keypad9,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.KeypadEqual => ImGuiKey.KeypadEqual,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.Menu => ImGuiKey.Menu,
            Key.Number0 => ImGuiKey.Keypad0,
            Key.Number1 => ImGuiKey.Keypad1,
            Key.Number2 => ImGuiKey.Keypad2,
            Key.Number3 => ImGuiKey.Keypad3,
            Key.Number4 => ImGuiKey.Keypad4,
            Key.Number5 => ImGuiKey.Keypad5,
            Key.Number6 => ImGuiKey.Keypad6,
            Key.Number7 => ImGuiKey.Keypad7,
            Key.Number8 => ImGuiKey.Keypad8,
            Key.Number9 => ImGuiKey.Keypad9,
            Key.A => ImGuiKey.A,
            Key.B => ImGuiKey.B,
            Key.C => ImGuiKey.C,
            Key.D => ImGuiKey.D,
            Key.E => ImGuiKey.E,
            Key.F => ImGuiKey.F,
            Key.G => ImGuiKey.G,
            Key.H => ImGuiKey.H,
            Key.I => ImGuiKey.I,
            Key.J => ImGuiKey.J,
            Key.K => ImGuiKey.K,
            Key.L => ImGuiKey.L,
            Key.M => ImGuiKey.M,
            Key.N => ImGuiKey.N,
            Key.O => ImGuiKey.O,
            Key.P => ImGuiKey.P,
            Key.Q => ImGuiKey.Q,
            Key.R => ImGuiKey.R,
            Key.S => ImGuiKey.S,
            Key.T => ImGuiKey.T,
            Key.U => ImGuiKey.U,
            Key.V => ImGuiKey.V,
            Key.W => ImGuiKey.W,
            Key.X => ImGuiKey.X,
            Key.Y => ImGuiKey.Y,
            Key.Z => ImGuiKey.Z,
            Key.F1 => ImGuiKey.F1,
            Key.F2 => ImGuiKey.F2,
            Key.F3 => ImGuiKey.F3,
            Key.F4 => ImGuiKey.F4,
            Key.F5 => ImGuiKey.F5,
            Key.F6 => ImGuiKey.F6,
            Key.F7 => ImGuiKey.F7,
            Key.F8 => ImGuiKey.F8,
            Key.F9 => ImGuiKey.F9,
            Key.F10 => ImGuiKey.F10,
            Key.F11 => ImGuiKey.F11,
            Key.F12 => ImGuiKey.F12,
            Key.F13 => ImGuiKey.F13,
            Key.F14 => ImGuiKey.F14,
            Key.F15 => ImGuiKey.F15,
            Key.F16 => ImGuiKey.F16,
            Key.F17 => ImGuiKey.F17,
            Key.F18 => ImGuiKey.F18,
            Key.F19 => ImGuiKey.F19,
            Key.F20 => ImGuiKey.F20,
            Key.F21 => ImGuiKey.F21,
            Key.F22 => ImGuiKey.F22,
            Key.F23 => ImGuiKey.F23,
            Key.F24 => ImGuiKey.F24,
            _ => ImGuiKey.None,
        };
    }

    private unsafe void SetupRenderState(ImDrawDataPtr drawDataPtr, int framebufferWidth, int framebufferHeight)
    {
        // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
        _gl.Enable(GLEnum.Blend);
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Enable(GLEnum.ScissorTest);
#if !GLES && !LEGACY
        _gl.Disable(GLEnum.PrimitiveRestart);
        _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

        float L = drawDataPtr.DisplayPos.X;
        float R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
        float T = drawDataPtr.DisplayPos.Y;
        float B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

        Span<float> orthoProjection = stackalloc float[] {
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f,
        };

        _gl.UseProgram(_shader.Handle);
        _gl.Uniform1(_attribLocationTex, 0);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);
        _gl.CheckGlError("Projection");

        _gl.BindSampler(0, 0);

        // Setup desired GL state
        // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
        // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
        _vertexArrayObject = _gl.GenVertexArray();
        _gl.BindVertexArray(_vertexArrayObject);
        _gl.CheckGlError("VAO");

        // Bind vertex/index buffers and setup attributes for ImDrawVert
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
        _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
        _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
        _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);
    }

    private Dictionary<ulong, ContentEditor.App.Graphics.Texture> textures = new();
    private unsafe void RenderImDrawData(ImDrawDataPtr drawDataPtr)
    {
        int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
        int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
            return;

        // Backup GL state
        _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);

        if (drawDataPtr.Textures.Data != null) {
            UpdateTextures(drawDataPtr);
        }

        // Backup GL state pt. 2
        _gl.ActiveTexture(GLEnum.Texture0);

        _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

        _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);

        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

        Span<int> lastPolygonMode = stackalloc int[2];
        _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);

        Span<int> lastScissorBox = stackalloc int[4];
        _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

        _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
        _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);

        _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
        _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

        _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
        _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

        bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
        bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
        bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
        bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
        bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

        bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);

        SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

        // Will project scissor/clipping rectangles into framebuffer space
        Vector2 clipOff = drawDataPtr.DisplayPos;         // (0,0) unless using multi-viewports
        Vector2 clipScale = drawDataPtr.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

        // Render command lists
        for (int n = 0; n < drawDataPtr.CmdListsCount; n++) {
            ImDrawListPtr cmdListPtr = drawDataPtr.CmdLists[n];

            // Upload vertex/index buffers

            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)cmdListPtr.VtxBuffer.Data, GLEnum.StreamDraw);
            _gl.CheckGlError($"Data Vert {n}");
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(cmdListPtr.IdxBuffer.Size * sizeof(ushort)), (void*)cmdListPtr.IdxBuffer.Data, GLEnum.StreamDraw);
            _gl.CheckGlError($"Data Idx {n}");

            for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++) {
                ImDrawCmd cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                if (cmdPtr.UserCallback != null) {
                    throw new NotImplementedException();
                } else {
                    Vector4 clipRect;
                    clipRect.X = (cmdPtr.ClipRect.X - clipOff.X) * clipScale.X;
                    clipRect.Y = (cmdPtr.ClipRect.Y - clipOff.Y) * clipScale.Y;
                    clipRect.Z = (cmdPtr.ClipRect.Z - clipOff.X) * clipScale.X;
                    clipRect.W = (cmdPtr.ClipRect.W - clipOff.Y) * clipScale.Y;

                    if (clipRect.X < framebufferWidth && clipRect.Y < framebufferHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f) {
                        // Apply scissor/clipping rectangle
                        _gl.Scissor((int)clipRect.X, (int)(framebufferHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));
                        _gl.CheckGlError("Scissor");

                        // Bind texture, Draw
                        _gl.BindTexture(GLEnum.Texture2D, (uint)cmdPtr.TexRef.GetTexID());
                        _gl.CheckGlError("Texture");

                        _gl.DrawElementsBaseVertex(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)(cmdPtr.IdxOffset * sizeof(ushort)), (int)cmdPtr.VtxOffset);
                        _gl.CheckGlError("Draw");
                    }
                }
            }
        }

        // Destroy the temporary VAO
        _gl.DeleteVertexArray(_vertexArrayObject);
        _vertexArrayObject = 0;

        // Restore modified GL state
        _gl.UseProgram((uint)lastProgram);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);

        _gl.BindSampler(0, (uint)lastSampler);

        _gl.ActiveTexture((GLEnum)lastActiveTexture);

        _gl.BindVertexArray((uint)lastVertexArrayObject);

        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
        _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

        if (lastEnableBlend) {
            _gl.Enable(GLEnum.Blend);
        } else {
            _gl.Disable(GLEnum.Blend);
        }

        if (lastEnableCullFace) {
            _gl.Enable(GLEnum.CullFace);
        } else {
            _gl.Disable(GLEnum.CullFace);
        }

        if (lastEnableDepthTest) {
            _gl.Enable(GLEnum.DepthTest);
        } else {
            _gl.Disable(GLEnum.DepthTest);
        }

        if (lastEnableStencilTest) {
            _gl.Enable(GLEnum.StencilTest);
        } else {
            _gl.Disable(GLEnum.StencilTest);
        }

        if (lastEnableScissorTest) {
            _gl.Enable(GLEnum.ScissorTest);
        } else {
            _gl.Disable(GLEnum.ScissorTest);
        }

        if (lastEnablePrimitiveRestart) {
            _gl.Enable(GLEnum.PrimitiveRestart);
        } else {
            _gl.Disable(GLEnum.PrimitiveRestart);
        }

        _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);

        _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
    }

    private unsafe void UpdateTextures(ImDrawDataPtr drawDataPtr)
    {
        var prevTex = _gl.GetInteger(GetPName.TextureBinding2D);
        for (int i = 0; i < drawDataPtr.Textures.Size; ++i) {
            var tex = drawDataPtr.Textures[i];
            if (tex.Status == ImTextureStatus.WantCreate) {
                var gltex = new ContentEditor.App.Graphics.Texture(_gl);
                var texid = new ImTextureID(gltex.Handle);
                var lin = (int)TextureMagFilter.Linear;
                var clamp = (int)GLEnum.ClampToEdge;
                _gl.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureMagFilter, in lin);
                _gl.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureMinFilter, in lin);
                _gl.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureWrapS, in clamp);
                _gl.TexParameterI(GLEnum.Texture2D, TextureParameterName.TextureWrapT, in clamp);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, (uint)tex.Width, (uint)tex.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, tex.GetPixels());
                textures[texid.Handle] = gltex;
                tex.SetTexID(texid);
                tex.SetStatus(ImTextureStatus.Ok);
            }
            if (tex.Status == ImTextureStatus.WantUpdates) {
                _gl.BindTexture(TextureTarget.Texture2D, (uint)tex.TexID.Handle);

                _gl.PixelStore(GLEnum.UnpackRowLength, tex.Width);
                for (int x = 0; x < tex.Updates.Size; ++x) {
                    var update = tex.Updates[x];
                    _gl.TexSubImage2D(TextureTarget.Texture2D, 0, update.X, update.Y, (uint)update.W, (uint)update.H, PixelFormat.Rgba, PixelType.UnsignedByte, tex.GetPixelsAt(update.X, update.Y));
                }
                _gl.PixelStore(GLEnum.UnpackRowLength, 0);
                tex.SetStatus(ImTextureStatus.Ok);
            }
            if (tex.Status == ImTextureStatus.WantDestroy && tex.UnusedFrames > 0) {
                textures.Remove(tex.TexID.Handle, out var gltex);
                gltex!.Dispose();
                tex.SetTexID(ImTextureID.Null);
                tex.SetStatus(ImTextureStatus.Destroyed);
            }
        }
        _gl.BindTexture(TextureTarget.Texture2D, (uint)prevTex);
    }

    private void CreateDeviceResources()
    {
        // Backup GL state

        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

        string vertexSource =
            @"#version 330
    layout (location = 0) in vec2 Position;
    layout (location = 1) in vec2 UV;
    layout (location = 2) in vec4 Color;
    uniform mat4 ProjMtx;
    out vec2 Frag_UV;
    out vec4 Frag_Color;
    void main()
    {
        Frag_UV = UV;
        Frag_Color = Color;
        gl_Position = ProjMtx * vec4(Position.xy,0,1);
    }";

        string fragmentSource =
            @"#version 330
    in vec2 Frag_UV;
    in vec4 Frag_Color;
    uniform sampler2D Texture;
    layout (location = 0) out vec4 Out_Color;
    void main()
    {
        Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
    }";

        _shader = new ContentEditor.App.Graphics.Shader(_gl, vertexSource, fragmentSource);

        _attribLocationTex = _gl.GetUniformLocation(_shader.Handle, "Texture");
        _attribLocationProjMtx = _gl.GetUniformLocation(_shader.Handle, "ProjMtx");
        _attribLocationVtxPos = _gl.GetAttribLocation(_shader.Handle, "Position");
        _attribLocationVtxUV = _gl.GetAttribLocation(_shader.Handle, "UV");
        _attribLocationVtxColor = _gl.GetAttribLocation(_shader.Handle, "Color");

        _vboHandle = _gl.GenBuffer();
        _elementsHandle = _gl.GenBuffer();



        // Restore modified GL state
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

        _gl.BindVertexArray((uint)lastVertexArray);

        _gl.CheckGlError("End of ImGui setup");
    }

    /// <summary>
    /// Frees all graphics resources used by the renderer.
    /// </summary>
    public void Dispose()
    {
        _view.Resize -= WindowResized;
        _keyboard.KeyChar -= OnKeyChar;

        _gl.DeleteBuffer(_vboHandle);
        _gl.DeleteBuffer(_elementsHandle);
        _gl.DeleteVertexArray(_vertexArrayObject);

        _shader.Dispose();

        global::Hexa.NET.ImGui.ImGui.DestroyContext(Context);
    }
}


internal static class ImGuiCtrlExtensions
{
    public static void CheckGlError(this GL gl, string msg)
    {
        var err = gl.GetError();
        if (err != GLEnum.None) {
            Console.Error.WriteLine($"{err}: {msg}");
        }
    }
}