using System.Numerics;
using System.Text.Json;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using Silk.NET.Input;

namespace ContentEditor.App;

public class SceneController(Scene scene)
{
    public Scene Scene { get; init; } = scene;
    public IKeyboard Keyboard { get; set; } = null!;
    private DragMode dragMode;
    private enum DragMode { None, Rotation }

    public float MoveSpeed { get; set; } = 8f;
    public float RotateSpeed { get; set; } = 2f;
    public float ZoomSpeed { get; set; } = 2f;
    public bool UseMeshViewerCameraBindings { get; set; }
    public SceneCameraMode CameraMode { get; private set; } = SceneCameraMode.FPSCamera;

    private float camYaw, camPitch;
    private bool orbitCameraDrag;
    private float orbitDistance;
    private bool hasCameraPivot;
    private Vector3 cameraPivot;

    public void ShowCameraControls()
    {
        ImGui.TextUnformatted("Camera Mode");
        if (ImGui.RadioButton("FPS Camera", CameraMode == SceneCameraMode.FPSCamera)) SetCameraMode(SceneCameraMode.FPSCamera, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("Pivot Camera", CameraMode == SceneCameraMode.PivotCamera)) SetCameraMode(SceneCameraMode.PivotCamera, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("Ortho Camera", CameraMode == SceneCameraMode.OrthoCamera)) SetCameraMode(SceneCameraMode.OrthoCamera, true);
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_ResetCamera}")) {
            ResetCameraToScene();
        }
        ImguiHelpers.Tooltip("Reset View Camera");
        if (Scene.ActiveCamera.ProjectionMode == CameraProjection.Perspective) {
            float fov = Scene.ActiveCamera.FieldOfView;
            if (ImGui.SliderAngle("Field of View", ref fov, 10.0f, 120.0f)) {
                Scene.ActiveCamera.FieldOfView = fov;
            }
        } else {
            float ortho = Scene.ActiveCamera.OrthoSize;
            if (ImGui.SliderFloat("Orthographic Size", ref ortho, 0.1f, 10.0f)) {
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
        ImGui.Spacing();
        var pos = Scene.ActiveCamera.Transform.Position;
        if (ImGui.DragFloat3("Position", ref pos, 0.01f)) {
            Scene.ActiveCamera.Transform.Position = pos;
        }
        if (ImGui.BeginPopupContextItem("Pos")) {
            if (ImGui.Selectable("Copy value")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(pos, JsonConfig.jsonOptionsIncludeFields), $"Copied position!");
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Paste value")) {
                if (EditorWindow.CurrentWindow?.GetClipboard()?.TryDeserializeJson<Vector3>(out pos, out var err, JsonConfig.jsonOptionsIncludeFields) == true) {
                    Scene.ActiveCamera.Transform.Position = pos;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (AppConfig.Instance.ShowQuaternionsAsEuler) {
            var euler = Scene.ActiveCamera.Transform.Rotation.ToEuler();
            if (ImGui.DragFloat3("Rotation", ref euler, 0.01f)) {
                euler *= TransformExtensions.Deg2Rad;
                Scene.ActiveCamera.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(euler.Y, euler.X, euler.Z);
            }
        } else {
            var rot = Scene.ActiveCamera.Transform.Rotation.ToVector4();
            if (ImGui.DragFloat4("Rot", ref rot, 0.01f)) {
                Scene.ActiveCamera.Transform.Rotation = rot.ToQuaternion();
            }
        }

        if (ImGui.BeginPopupContextItem("Rot")) {
            var rot = Scene.ActiveCamera.Transform.Rotation;
            if (ImGui.Selectable("Copy value")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(rot, JsonConfig.jsonOptionsIncludeFields), $"Copied position!");
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Paste value")) {
                if (EditorWindow.CurrentWindow?.GetClipboard()?.TryDeserializeJson<Quaternion>(out rot, out var err, JsonConfig.jsonOptionsIncludeFields) == true) {
                    Scene.ActiveCamera.Transform.Rotation = rot;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public void SetCameraMode(SceneCameraMode mode, bool resetCamera = false)
    {
        CameraMode = mode;
        Scene.ActiveCamera.ProjectionMode = mode == SceneCameraMode.OrthoCamera
            ? CameraProjection.Orthographic
            : CameraProjection.Perspective;
        orbitCameraDrag = false;
        if (mode == SceneCameraMode.FPSCamera) {
            hasCameraPivot = false;
        } else {
            EnsureCameraPivot();
        }
        if (resetCamera) ResetCameraToScene();
    }

    public void OnMouseDragStart(IMouse mouse, ImGuiMouseButton startButton, Vector2 position)
    {
        var startCameraDrag = startButton == ImGuiMouseButton.Right;
        orbitCameraDrag = startCameraDrag && CameraMode != SceneCameraMode.FPSCamera;
        var initializeMouseDragDepth = orbitCameraDrag;
        if (UseMeshViewerCameraBindings) {
            var rotateBinding = AppConfig.Instance.Key_MeshViewer_CameraRotate.Get();
            var startRotateDrag = IsDragStartBinding(rotateBinding, startButton);
            var translateBinding = AppConfig.Instance.Key_MeshViewer_CameraTranslate.Get();
            var startTranslateDrag = IsDragStartBinding(translateBinding, startButton);
            startCameraDrag = startRotateDrag || startTranslateDrag;
            orbitCameraDrag = startRotateDrag && CameraMode != SceneCameraMode.FPSCamera;
            initializeMouseDragDepth = startRotateDrag || startTranslateDrag && translateBinding.mouseDrag;
        }
        if (startCameraDrag) {
            mouse.Cursor.CursorMode = CursorMode.Disabled;
            dragMode = DragMode.Rotation;

            var fwd = Scene.ActiveCamera.Transform.LocalForward;
            camYaw = MathF.Atan2(-fwd.X, -fwd.Z);
            camPitch = MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f));

            if (initializeMouseDragDepth) {
                InitializeMouseDragDepth();
            }
        }
    }

    public void OnMouseDragEnd(IMouse mouse, ImGuiMouseButton startButton, Vector2 position, Vector2 dragStartPosition)
    {
        if (dragMode == DragMode.Rotation) {
            mouse.Cursor.CursorMode = CursorMode.Normal;
            mouse.Position = dragStartPosition;
            foreach (var window in MainLoop.Instance.Windows) {
                window.NotifyCursorMoved();
            }
        }
        dragMode = DragMode.None;
        orbitCameraDrag = false;
    }

    public void OnMouseDrag(MouseButtonFlags buttons, Vector2 position, Vector2 delta)
    {
        if (dragMode == DragMode.Rotation) {
            var rotateCamera = buttons == MouseButtonFlags.Right;
            var invertRotationDrag = false;
            var translateWithMouseDrag = false;
            var invertTranslationDrag = false;
            if (UseMeshViewerCameraBindings) {
                var rotateBinding = AppConfig.Instance.Key_MeshViewer_CameraRotate.Get();
                rotateCamera = IsDragBindingActive(rotateBinding, buttons);
                invertRotationDrag = rotateBinding.invertDrag;
                var translateBinding = AppConfig.Instance.Key_MeshViewer_CameraTranslate.Get();
                translateWithMouseDrag = translateBinding.mouseDrag && IsDragBindingActive(translateBinding, buttons, includeFpsVerticalMovement: true);
                invertTranslationDrag = translateBinding.invertDrag;
            }
            if (rotateCamera) {
                var multiplier = 0.002f * RotateSpeed;
                if (invertRotationDrag) multiplier = -multiplier;
                camYaw = camYaw - delta.X * multiplier;
                camPitch = Math.Clamp(camPitch - delta.Y * multiplier, -80f * MathF.PI / 180, 80f * MathF.PI / 180);
                Scene.ActiveCamera.GameObject.Transform.LocalRotation = Quaternion.CreateFromYawPitchRoll(camYaw, camPitch, 0);
                if (CameraMode != SceneCameraMode.FPSCamera && orbitCameraDrag) {
                    Scene.ActiveCamera.Transform.Position = cameraPivot - Scene.ActiveCamera.Transform.Forward * orbitDistance;
                }
            } else if (translateWithMouseDrag) {
                var multiplier = GetMouseDragWorldUnitsPerPixel();
                if (invertTranslationDrag) multiplier = -multiplier;
                var camera = Scene.ActiveCamera.Transform;
                var translation = (camera.Right * delta.X - camera.Up * delta.Y) * multiplier;
                camera.Position += translation;
                if (CameraMode != SceneCameraMode.FPSCamera) {
                    cameraPivot += translation;
                }
            } else if (buttons == MouseButtonFlags.Left) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(-delta.X, 0, delta.Y) * -0.04f);
            } else if ((buttons & (MouseButtonFlags.Left|MouseButtonFlags.Right)) != 0) {
                Scene.ActiveCamera.GameObject.Transform.TranslateForwardAligned(new Vector3(delta.X, -delta.Y, 0) * 0.04f);
            }
        }
    }

    public void Update(float deltaTime)
    {
        var translateCamera = dragMode == DragMode.Rotation;
        if (UseMeshViewerCameraBindings) {
            var binding = AppConfig.Instance.Key_MeshViewer_CameraTranslate.Get();
            var fpsCamera = CameraMode == SceneCameraMode.FPSCamera;
            translateCamera = translateCamera && !binding.mouseDrag
                && binding.IsDown(allowExtraShift: fpsCamera)
                && (!binding.wasd || IsCameraTranslationKeyPressed());
        }
        if (translateCamera) {
            var moveVec = new Vector3();
            if (Keyboard.IsKeyPressed(Key.W)) moveVec.Z -= 1;
            if (Keyboard.IsKeyPressed(Key.S)) moveVec.Z += 1;
            if (Keyboard.IsKeyPressed(Key.A)) moveVec.X -= 1;
            if (Keyboard.IsKeyPressed(Key.D)) moveVec.X += 1;
            if (CameraMode == SceneCameraMode.FPSCamera) {
                if (Keyboard.IsKeyPressed(Key.E)) moveVec.Y += 1;
                if (Keyboard.IsKeyPressed(Key.Q)) moveVec.Y -= 1;
            }
            if (Scene.ActiveCamera.ProjectionMode == CameraProjection.Orthographic) {
                // redirect W/S to ortho up/down
                moveVec.Y -= moveVec.Z;
            }
            if (CameraMode == SceneCameraMode.FPSCamera
                && (Keyboard.IsKeyPressed(Key.ShiftLeft) || Keyboard.IsKeyPressed(Key.ShiftRight))) moveVec *= 10;

            var camera = Scene.ActiveCamera.Transform;
            var previousPosition = camera.Position;
            camera.TranslateForwardAligned(MoveSpeed * moveVec * deltaTime);
            if (CameraMode != SceneCameraMode.FPSCamera && hasCameraPivot) cameraPivot += camera.Position - previousPosition;
        }
    }

    private bool IsDragStartBinding(KeyBinding binding, ImGuiMouseButton startButton)
    {
        var startKey = startButton switch {
            ImGuiMouseButton.Left => ImGuiKey.MouseLeft,
            ImGuiMouseButton.Right => ImGuiKey.MouseRight,
            ImGuiMouseButton.Middle => ImGuiKey.MouseMiddle,
            _ => ImGuiKey.None,
        };
        var allowExtraShift = CameraMode == SceneCameraMode.FPSCamera;
        return (binding.Key == startKey && binding.AreModifiersDown(allowExtraShift))
            || (!IsMouseButton(binding.Key) && binding.IsDown(allowExtraShift));
    }

    private bool IsDragBindingActive(KeyBinding binding, MouseButtonFlags buttons, bool includeFpsVerticalMovement = false)
    {
        var bindingButton = binding.Key switch {
            ImGuiKey.MouseLeft => MouseButtonFlags.Left,
            ImGuiKey.MouseRight => MouseButtonFlags.Right,
            ImGuiKey.MouseMiddle => MouseButtonFlags.Middle,
            _ => (MouseButtonFlags)0,
        };
        var bindingActive = bindingButton != 0
            ? buttons == bindingButton && binding.AreModifiersDown(CameraMode == SceneCameraMode.FPSCamera)
            : binding.IsDown(CameraMode == SceneCameraMode.FPSCamera);
        return bindingActive && (!binding.wasd || (includeFpsVerticalMovement ? IsCameraTranslationKeyPressed() : IsWASDPressed()));
    }

    private static bool IsMouseButton(ImGuiKey key) => key is ImGuiKey.MouseLeft or ImGuiKey.MouseRight or ImGuiKey.MouseMiddle;

    private bool IsCameraTranslationKeyPressed() => IsWASDPressed() || CameraMode == SceneCameraMode.FPSCamera && (Keyboard.IsKeyPressed(Key.Q) || Keyboard.IsKeyPressed(Key.E));

    private bool IsWASDPressed() => Keyboard.IsKeyPressed(Key.W) || Keyboard.IsKeyPressed(Key.A) || Keyboard.IsKeyPressed(Key.S) || Keyboard.IsKeyPressed(Key.D);

    private void InitializeMouseDragDepth()
    {
        var camera = Scene.ActiveCamera.Transform;
        if (CameraMode == SceneCameraMode.FPSCamera) {
            var bounds = Scene.RootFolder.GetWorldSpaceBounds();
            orbitDistance = !bounds.IsInvalid
                ? Math.Max(Math.Abs(Vector3.Dot(bounds.Center - camera.Position, camera.Forward)), 0.001f)
                : 1.0f;
        } else {
            EnsureCameraPivot();
            orbitDistance = Math.Max(Vector3.Distance(camera.Position, cameraPivot), 0.001f);
        }
    }

    private void EnsureCameraPivot()
    {
        var camera = Scene.ActiveCamera.Transform;
        if (hasCameraPivot) {
            var pivotDirection = cameraPivot - camera.Position;
            if (pivotDirection.LengthSquared() > 0.000001f && Vector3.Dot(Vector3.Normalize(pivotDirection), camera.Forward) > 0.999f) {
                orbitDistance = pivotDirection.Length();
                return;
            }
        }

        var bounds = Scene.RootFolder.GetWorldSpaceBounds();
        if (!bounds.IsInvalid) {
            cameraPivot = bounds.Center;
            var pivotDepth = Vector3.Dot(cameraPivot - camera.Position, camera.Forward);
            if (pivotDepth <= 0.001f) cameraPivot = camera.Position + camera.Forward * Math.Max(bounds.Size.Length(), 0.1f);
        } else {
            cameraPivot = camera.Position + camera.Forward;
        }
        hasCameraPivot = true;
        orbitDistance = Vector3.Distance(camera.Position, cameraPivot);
    }

    public void SetCameraPivot(Vector3 pivot)
    {
        if (CameraMode == SceneCameraMode.FPSCamera) return;
        cameraPivot = pivot;
        hasCameraPivot = true;
        orbitDistance = Math.Max(Vector3.Distance(Scene.ActiveCamera.Transform.Position, cameraPivot), 0.001f);
    }

    public void ZoomCamera(float wheel)
    {
        if (CameraMode == SceneCameraMode.OrthoCamera) {
            var ortho = Scene.ActiveCamera.OrthoSize;
            ortho *= 1.0f - wheel * ZoomSpeed * 0.1f;
            Scene.ActiveCamera.OrthoSize = Math.Clamp(ortho, 0.01f, 100.0f);
            return;
        }

        var camera = Scene.ActiveCamera.Transform;
        if (CameraMode == SceneCameraMode.FPSCamera) {
            camera.LocalPosition += camera.LocalForward * (wheel * ZoomSpeed * 0.1f);
            return;
        }

        EnsureCameraPivot();
        var zoomAmount = wheel * ZoomSpeed * Math.Max(orbitDistance, 0.1f) * 0.1f;
        orbitDistance = Math.Max(orbitDistance - zoomAmount, 0.001f);
        camera.Position = cameraPivot - camera.Forward * orbitDistance;
    }

    private void ResetCameraToScene()
    {
        var bounds = Scene.RootFolder.GetWorldSpaceBounds();
        Scene.ActiveCamera.LookAt(bounds, true);
        if (!bounds.IsInvalid) SetCameraPivot(bounds.Center);
    }

    private float GetMouseDragWorldUnitsPerPixel()
    {
        var camera = Scene.ActiveCamera;
        var viewportHeight = Math.Max(Scene.RenderContext.ViewportSize.Y, 1.0f);
        if (camera.ProjectionMode == CameraProjection.Orthographic) {
            return camera.OrthoSize / viewportHeight;
        }
        return 2.0f * orbitDistance * MathF.Tan(camera.FieldOfView * 0.5f) / viewportHeight;
    }

    public void UpdateGizmo(EditorWindow window, GizmoManager manager)
    {
        foreach (var ui in window.ActiveImguiWindows) {
            if (ui.Handler is ObjectInspector inspector && inspector.Target is GameObject go && go.Scene?.RootScene == Scene && go.IsInTree) {
                var gizmo = manager.GetOrAddStandaloneGizmo(go.Transform);
                gizmo.Cur.Push().TransformHandle(go.Transform);
            }
        }
    }
}

public enum SceneCameraMode
{
    FPSCamera,
    PivotCamera,
    OrthoCamera,
}

[Flags]
public enum MouseButtonFlags
{
    Left = 1,
    Right = 2,
    Middle = 4,
}
