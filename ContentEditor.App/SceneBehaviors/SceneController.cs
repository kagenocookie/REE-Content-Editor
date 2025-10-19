using System.Net.Cache;
using System.Numerics;
using ContentEditor.Core;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace ContentEditor.App;

public class SceneController
{
    public Scene Scene { get; set; } = null!;
    public IKeyboard Keyboard { get; set; } = null!;
    private DragMode dragMode;
    private enum DragMode { None, Rotation }

    public float MoveSpeed { get; set; } = 10f;
    public float RotateSpeed { get; set; } = 2f;
    public float ZoomSpeed { get; set; } = 2f;

    private float camYaw, camPitch;

    public void ShowCameraControls()
    {
        if (ImGui.RadioButton("Orthographic", Scene.ActiveCamera.ProjectionMode == CameraProjection.Orthographic)) {
            Scene.ActiveCamera.ProjectionMode = CameraProjection.Orthographic;
            // CenterCameraToSceneObject();
            Scene.ActiveCamera.LookAt(Scene.RootFolder.GetWorldSpaceBounds(), true);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Perspective", Scene.ActiveCamera.ProjectionMode == CameraProjection.Perspective)) {
            Scene.ActiveCamera.ProjectionMode = CameraProjection.Perspective;
            // CenterCameraToSceneObject();
            Scene.ActiveCamera.LookAt(Scene.RootFolder.GetWorldSpaceBounds(), true);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetCamera}")) {
            // CenterCameraToSceneObject();
            Scene.ActiveCamera.LookAt(Scene.RootFolder.GetWorldSpaceBounds(), true);
        }
        ImguiHelpers.Tooltip("Reset View Camera");
        if (Scene.ActiveCamera.ProjectionMode == CameraProjection.Perspective) {
            float fov = Scene.ActiveCamera.FieldOfView;
            if (ImGui.SliderAngle("Field of View", ref fov, 10.0f, 120.0f)) {
                Scene.ActiveCamera.FieldOfView = fov;
            }
        } else {
            float ortho = Scene.ActiveCamera.OrthoSize;
            if (ImGui.SliderFloat("Field of View", ref ortho, 0.1f, 10.0f)) {
                Scene.ActiveCamera.OrthoSize = ortho;
            }
        }

        var moveSpeed = MoveSpeed;
        var rotateSpeed = RotateSpeed;
        var zoomSpeed = ZoomSpeed;
        if (ImGui.SliderFloat("Move Speed", ref moveSpeed, 1.0f, 50.0f)) {
            MoveSpeed = moveSpeed;
        }
        ImguiHelpers.Tooltip("[Hold] Left Shift to move 10x faster.");
        if (ImGui.SliderFloat("Rotate Speed", ref rotateSpeed, 0.1f, 10.0f)) {
            RotateSpeed = rotateSpeed;
        }
        if (ImGui.SliderFloat("Zoom Speed", ref zoomSpeed, 0.01f, 1.0f)) {
            ZoomSpeed = zoomSpeed;
        }
    }

    public void OnMouseDragStart(IMouse mouse, ImGuiMouseButton startButton, Vector2 position)
    {
        if (startButton == ImGuiMouseButton.Right) {
            mouse.Cursor.CursorMode = CursorMode.Disabled;
            dragMode = DragMode.Rotation;

            var fwd = Scene.ActiveCamera.Transform.LocalForward;
            camYaw = MathF.Atan2(fwd.X, fwd.Z);
            camPitch = -MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f));
        }
    }

    public void OnMouseDragEnd(IMouse mouse, ImGuiMouseButton startButton, Vector2 position, Vector2 dragStartPosition)
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
                var multiplier = Time.Delta * 0.25f * RotateSpeed;
                camYaw = camYaw - delta.X * multiplier;
                camPitch = Math.Clamp(camPitch - delta.Y * multiplier, -80f * MathF.PI / 180, 80f * MathF.PI / 180);
                Scene.ActiveCamera.GameObject.Transform.LocalRotation = Quaternion<float>.CreateFromYawPitchRoll(camYaw, camPitch, 0).ToSystem();
            } else if (buttons == MouseButtonFlags.Left) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(-delta.X, 0, delta.Y) * -0.04f);
            } else if ((buttons & (MouseButtonFlags.Left|MouseButtonFlags.Right)) != 0) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(delta.X, -delta.Y, 0) * 0.04f);
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

            Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(MoveSpeed * moveVec * deltaTime);
        }

        float wheel = ImGui.GetIO().MouseWheel;
        if (Math.Abs(wheel) > float.Epsilon) {
            if (Scene.ActiveCamera.ProjectionMode == CameraProjection.Perspective) {
                var zoom = Scene.Camera.GameObject.Transform.LocalForward * (wheel * ZoomSpeed) * -1.0f;
                Scene.Camera.GameObject.Transform.LocalPosition += zoom;
            } else {
                float ortho = Scene.ActiveCamera.OrthoSize;
                ortho *= (1.0f - wheel * ZoomSpeed);
                ortho = Math.Clamp(ortho, 0.01f, 100.0f);
                Scene.ActiveCamera.OrthoSize = ortho;
            }
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