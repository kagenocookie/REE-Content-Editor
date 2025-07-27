namespace ContentPatcher;

using ReeLib.Common;

public readonly record struct HashedString(string str)
{
    public readonly uint hash = MurMur3HashUtils.GetHash(str);

    public static implicit operator HashedString(string str) => new HashedString(str);
    public static implicit operator uint(HashedString hash) => hash.hash;
    public static implicit operator int(HashedString hash) => (int)hash.hash;
}
