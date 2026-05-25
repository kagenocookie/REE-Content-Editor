using ReeLib;

namespace ContentPatcher;

public class ClipFileLoader() : DefaultFileMultiLoader<ClipFile>(
    KnownFileFormats.Timeline,
    KnownFileFormats.Clip,
    KnownFileFormats.UserCurve,
    KnownFileFormats.DialogueTimeline)
{
}
