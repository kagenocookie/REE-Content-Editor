using System.Net.Cache;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace ContentEditor.App;

public class ActiveSceneBehavior
{
    public Scene Scene { get; set; } = null!;
    public IKeyboard Keyboard { get; set; } = null!;
    private DragMode dragMode;
    private enum DragMode { None, Rotation }

    private float camYaw, camPitch;

    public void OnMouseDragStart(IMouse mouse, MouseButton startButton, Vector2 position)
    {
        if (startButton == MouseButton.Right) {
            mouse.Cursor.CursorMode = CursorMode.Disabled;
            dragMode = DragMode.Rotation;

            var fwd = Scene.ActiveCamera.Transform.LocalForward;
            camYaw = MathF.Atan2(fwd.X, fwd.Z);
            camPitch = -MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f));
        }
    }

    public void OnMouseDragEnd(IMouse mouse, MouseButton startButton, Vector2 position, Vector2 dragStartPosition)
    {
        if (dragMode == DragMode.Rotation) {
            mouse.Cursor.CursorMode = CursorMode.Normal;
            mouse.Position = dragStartPosition;
        }
        dragMode = DragMode.None;
    }

    public void OnMouseDrag(MouseButtonFlags buttons, Vector2 position, Vector2 delta)
    {
        if (dragMode == DragMode.Rotation) {
            if (buttons == MouseButtonFlags.Right) {
                camYaw = camYaw + delta.X * 0.008f;
                camPitch = Math.Clamp(camPitch + delta.Y * 0.008f, -80f * MathF.PI / 180, 80f * MathF.PI / 180);
                Scene.ActiveCamera.GameObject.Transform.LocalRotation = Quaternion<float>.CreateFromYawPitchRoll(camYaw, camPitch, 0).ToSystem();
            } else if (buttons == MouseButtonFlags.Left) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(delta.X, 0, -delta.Y) * -0.04f);
            } else if ((buttons & (MouseButtonFlags.Left|MouseButtonFlags.Right)) != 0) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(-delta.X, delta.Y, 0) * 0.04f);
            }
        }
    }

    public void Update(float deltaTime)
    {
        if (dragMode == DragMode.Rotation) {
            var moveVec = new Vector3();
            if (Keyboard.IsKeyPressed(Key.W)) moveVec.Z -= 1;
            if (Keyboard.IsKeyPressed(Key.S)) moveVec.Z += 1;
            if (Keyboard.IsKeyPressed(Key.A)) moveVec.X -= 1;
            if (Keyboard.IsKeyPressed(Key.D)) moveVec.X += 1;
            if (Keyboard.IsKeyPressed(Key.E)) moveVec.Y += 1;
            if (Keyboard.IsKeyPressed(Key.Q)) moveVec.Y -= 1;
            if (Keyboard.IsKeyPressed(Key.ShiftLeft)) moveVec *= 10;

            Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(moveVec * deltaTime * 10);
        }
    }
}

[Flags]
public enum MouseButtonFlags
{
    Left = 1,
    Right = 2,
    Middle = 4,
}