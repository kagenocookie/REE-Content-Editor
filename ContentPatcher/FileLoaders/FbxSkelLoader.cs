using ReeLib;

namespace ContentPatcher;

public class FbxSkelLoader : DefaultFileMultiLoader<FbxSkelFile>
{
    public FbxSkelLoader() : base(KnownFileFormats.FbxSkeleton, KnownFileFormats.RefSkeleton, KnownFileFormats.Skeleton) { }
}
