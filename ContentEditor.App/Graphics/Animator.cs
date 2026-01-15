using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class Animator(ContentWorkspace Workspace)
{
    private FileHandle? animationFile;
    private readonly Dictionary<string, MotFileBase> motions = new();
    public MotFile? ActiveMotion { get; private set; }
    private float currentTime;
    private float clipFramerate;
    private float clipDuration;

    private AnimatedMeshHandle? mesh;
    public AnimatedMeshHandle? Mesh => mesh;

    public FileHandle? File => animationFile;

    /// <summary>
    /// Whether there is an animation currently active. Should be true at any time the mesh is not in its default mesh pose, whether currently playing or not.
    /// </summary>
    public bool IsActive { get; private set; }
    public bool IsPlaying { get; private set; }

    public bool IgnoreRootMotion { get; set; } = true;

    public int AnimationCount => motions.Count;
    public IEnumerable<string> AnimationNames => motions.Keys;
    public IEnumerable<KeyValuePair<string, MotFileBase>> Animations => motions.OrderBy(k => k.Key);

    public float CurrentTime => currentTime;
    public float TotalTime => clipDuration;
    public int TotalFrames => (int)(ActiveMotion?.Header.frameCount ?? 0);
    public int CurrentFrame => (int)Math.Round(currentTime * clipFramerate);
    public float FrameRate => clipFramerate;
    public float FrameDuration => 1 / clipFramerate;

    public void Play()
    {
        IsPlaying = true;
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Stop()
    {
        IsPlaying = false;
        currentTime = 0;
        RevertMeshPose();
    }

    public void Restart()
    {
        currentTime = 0;
        Update(0);
    }

    public void Seek(float time)
    {
        currentTime = Math.Clamp(time, 0, clipDuration);
    }

    public void SeekPercentage(float time)
    {
        currentTime = Math.Clamp(time / TotalTime, 0, clipDuration);
    }

    public static float GetInterpolation<TValue>(TValue[] array1, TValue[] array2, int frame, float time)
    {
        return 0;
    }

    public void SetMesh(AnimatedMeshHandle mesh)
    {
        this.mesh = mesh;
    }

    public bool SetActiveMotion(string name)
    {
        if (motions.TryGetValue(name, out var mot)) {
            SetActiveMotion(mot);
            return true;
        }
        return false;
    }

    public void SetActiveMotion(MotFileBase mot)
    {
        ActiveMotion = (MotFile)mot;

        if (ActiveMotion.Header.FrameRate > 0) {
            clipFramerate = ActiveMotion.Header.FrameRate;
        } else {
            // shouldn't really happen but can't hurt to make sure we don't NaN it
            clipFramerate = 60;
        }
        clipDuration = ActiveMotion.Header.frameCount / clipFramerate;
        currentTime = 0;
        Update(0);
    }

    public void Unload()
    {
        Stop();
        if (animationFile != null) {
            // do we wanna unload here?
            animationFile = null;
        }
        motions.Clear();
        ActiveMotion = null;
        // animator doesn't own the mesh, we can just forget about it here
        mesh = null;
    }

    public void LoadAnimationList(string fileSource)
    {
        Unload();
        if (Workspace.ResourceManager.TryGetOrLoadFile(fileSource, out var file)) {
            animationFile = file;
            if (file.Resource is BaseFileResource<MotlistFile> motlist) {
                foreach (var submot in motlist.File.MotFiles) {
                    if (submot is MotFile mmo) {
                        motions.Add(mmo.Header.motName, mmo);
                    }
                }
            } else if (file.Resource is BaseFileResource<MotFile> mot) {
                motions.Add(file.Filename.ToString(), mot.File);
            } else if (file.GetFile<MotlistFile>() is MotlistFile customMotlist) {
                foreach (var submot in customMotlist.MotFiles) {
                    if (submot is MotFile mmo) {
                        motions.Add(mmo.Header.motName, mmo);
                    }
                }
            } else {
                Logger.Error("Unsupported animation source file " + fileSource);
            }
        } else {
            Logger.Error("Could not resolve file " + fileSource);
        }
    }

    public void Update(float deltaTime)
    {
        if (mesh == null || ActiveMotion == null) return;

        IsActive = true;
        if (mesh is AnimatedMeshHandle animMesh && animMesh.Bones != null) {
            if (animMesh.DeformBoneMatrices.Length != animMesh.Bones.DeformBones.Count) {
                animMesh.DeformBoneMatrices = new Matrix4x4[animMesh.Bones.DeformBones.Count];
            }
            ref var transformCache = ref animMesh.BoneMatrices;
            if (transformCache.Length != animMesh.Bones.Bones.Count) {
                transformCache = new Matrix4x4[animMesh.Bones.Bones.Count];
            }

            currentTime += deltaTime;
            if (currentTime > clipDuration) {
                currentTime -= clipDuration;
            }
            var frame = (currentTime * clipFramerate);
            foreach (var bone in animMesh.Bones.Bones) {

                if (IgnoreRootMotion && bone.parentIndex == -1) {
                    transformCache[bone.index] = Matrix4x4.Identity;
                    continue;
                }

                var boneHash = MurMur3HashUtils.GetHash(bone.name);
                var clip = ActiveMotion.BoneClips.FirstOrDefault(bc => bc.ClipHeader.boneHash == boneHash);
                Matrix4x4.Decompose(bone.localTransform.ToSystem(), out var localScale, out var localRot, out var localPos);
                var motBone = ActiveMotion.Bones.FirstOrDefault(bh => bh.boneHash == boneHash);

                if (motBone != null) {
                    localPos = motBone.translation;
                    localRot = motBone.quaternion;
                }

                if (clip?.HasTranslation == true && clip.Translation!.frameIndexes != null) {
                    var (t1, t2, interp) = FindFrames(clip.Translation, frame);
                    if (t1 >= 0) {
                        localPos = Vector3.Lerp(clip.Translation.translations![t1], clip.Translation.translations![t2], interp);
                    }
                }

                if (clip?.HasRotation == true && clip.Rotation!.frameIndexes != null) {
                    var (t1, t2, interp) = FindFrames(clip.Rotation, frame);
                    if (t1 >= 0) {
                        localRot = Quaternion.Lerp(clip.Rotation.rotations![t1], clip.Rotation.rotations![t2], interp);
                    }
                }

                if (clip?.HasScale == true && clip.Scale!.frameIndexes != null) {
                    var (t1, t2, interp) = FindFrames(clip.Scale, frame);
                    if (t1 >= 0) {
                        localScale = Vector3.Lerp(clip.Scale.translations![t1], clip.Scale.translations![t2], interp);
                    }
                }

                var localTransform = Matrix4x4.CreateScale(localScale) * Matrix4x4.CreateFromQuaternion(localRot) * Matrix4x4.CreateTranslation(localPos);

                var worldMat = bone.Parent == null ? localTransform : localTransform * transformCache[bone.Parent.index];
                transformCache[bone.index] = worldMat;

                if (bone.remapIndex != -1) {
                    animMesh.DeformBoneMatrices[bone.remapIndex] = bone.inverseGlobalTransform.ToSystem() * worldMat;
                }
            }
        }
    }

    private void RevertMeshPose()
    {
        if (mesh is AnimatedMeshHandle animMesh && animMesh.Bones != null) {
            foreach (var bone in animMesh.Bones.Bones) {
                if (bone.remapIndex != -1) {
                    animMesh.DeformBoneMatrices[bone.remapIndex] = Matrix4x4.Identity;
                }
                animMesh.BoneMatrices[bone.index] = bone.globalTransform.ToSystem();
            }
        }
        IsActive = false;
    }

    public static (int first, int second, float interpolation) FindFrames(Track track, float frame)
    {
        var keyframes = track.frameIndexes!;
        var len = keyframes.Length;
        if (len == 0) return (-1, -1, 0);

        int t2 = -1;
        for (int i = 0; i < len; ++i) {
            var val = keyframes[i];
            if (val > frame) {
                t2 = i;
                break;
            }
        }
        if (t2 <= 0) return keyframes.Length > 0 && frame > keyframes[^1] ? (keyframes.Length - 1, 0, 0) : (0, 0, 0);

        var t1 = t2 - 1;

        var f1 = keyframes[t1];
        var f2 = keyframes[t2];
        var w = (frame - f1) / (f2 - f1);

        return (t1, t2, w);
    }
}
