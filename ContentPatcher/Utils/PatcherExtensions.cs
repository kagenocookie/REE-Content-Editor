using ReeLib;

namespace ContentPatcher;

public static class PatcherExtensions
{
    public static bool IsDefaultReplacedBundleResource(this KnownFileFormats format)
        => format is KnownFileFormats.UserData or KnownFileFormats.Message ? false : true;

    public static bool IsRSZBasedFormat(this KnownFileFormats format)
        => format is KnownFileFormats.Prefab or KnownFileFormats.UserData or KnownFileFormats.Scene or KnownFileFormats.RequestSetCollider
            or ReeLib.KnownFileFormats.MotionFsm2 or ReeLib.KnownFileFormats.BehaviorTree or ReeLib.KnownFileFormats.Fsm2 or ReeLib.KnownFileFormats.TimelineFsm2
            or KnownFileFormats.WwiseAudioRSZ or KnownFileFormats.AIMap
            or KnownFileFormats.Dialogue or KnownFileFormats.DialogueList;
}