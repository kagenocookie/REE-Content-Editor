using System.Runtime.CompilerServices;

namespace ContentEditor.App.Graphics;

public record struct VertexAttribute(int Offset, int Size, uint Index);

public readonly struct MeshLayout(params VertexAttribute[] attributes)
{
    public readonly int VertexSize { get; } = attributes[^1].Offset + attributes[^1].Size;
    public readonly MeshAttributeFlag Flags { get; } = MapAttributesToFlags(attributes);
    public readonly VertexAttribute[] Attributes { get; } = attributes;


    public readonly int[] AttributeIndexOffsets { get; } = GetAttributeOffsetLookups(attributes);

    public const int Index_Position = 0;
    public const int Index_UV = 1;
    public const int Index_Normal = 2;
    public const int Index_Index = 3;
    public const int Index_Tangent = 4;
    public const int Index_BoneIndex = 5;
    public const int Index_BoneWeight = 6;
    public const int Index_Color = 7;
    public const int Index_InstancesMatrix = 8;

    private readonly static VertexAttribute Attr_Position = new(0, 3, Index_Position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasAttributes(MeshAttributeFlag flags) => (Flags & flags) == flags;

    private static readonly VertexAttribute[] DefaultAttributes = [
        new VertexAttribute(0, 3, Index_Position),
        new VertexAttribute(3, 2, Index_UV),
        new VertexAttribute(5, 3, Index_Normal),
        new VertexAttribute(8, 1, Index_Index),
    ];

    private static readonly VertexAttribute[] DefaultAttributesNoIndex = [
        new VertexAttribute(0, 3, Index_Position),
        new VertexAttribute(3, 2, Index_UV),
        new VertexAttribute(5, 3, Index_Normal),
    ];

    public static readonly MeshLayout BasicTriangleMesh = new MeshLayout(DefaultAttributes);
    public static readonly MeshLayout BasicTriangleMeshNoIndex = new MeshLayout(DefaultAttributesNoIndex);
    public static readonly MeshLayout SkinnedTriangleMesh = new MeshLayout([
        .. DefaultAttributes,
        new VertexAttribute(9, 4, Index_BoneIndex),
        new VertexAttribute(13, 4, Index_BoneWeight)
    ]);

    public static readonly MeshLayout PositionOnly = new MeshLayout(Attr_Position);
    public static readonly MeshLayout ColoredPositions = new MeshLayout(Attr_Position, new VertexAttribute(3, 1, Index_Color));

    private static MeshAttributeFlag MapAttributesToFlags(VertexAttribute[] attributes)
    {
        MeshAttributeFlag flags = 0;
        foreach (var attr in attributes) {
            flags |= attr.Index switch {
                Index_Position => MeshAttributeFlag.Position,
                Index_UV => MeshAttributeFlag.UV,
                Index_Normal => MeshAttributeFlag.Normal,
                Index_Index => MeshAttributeFlag.Index,
                Index_Tangent => MeshAttributeFlag.Tangent,
                Index_BoneIndex => MeshAttributeFlag.Weight,
                Index_BoneWeight => MeshAttributeFlag.Weight,
                Index_Color => MeshAttributeFlag.Color,
                Index_InstancesMatrix => MeshAttributeFlag.Instanced,
                _ => throw new NotImplementedException("Unknown attribute index " + attr.Index),
            };
        }

        return flags;
    }

    private static int[] GetAttributeOffsetLookups(VertexAttribute[] attributes)
    {
        var lookups = new int[Index_InstancesMatrix + 1];
        Array.Fill(lookups, -1);
        foreach (var attr in attributes) {
            lookups[attr.Index] = attr.Offset;
        }

        return lookups;
    }

    public static MeshLayout Get(MeshAttributeFlag flags)
    {
        flags |= MeshAttributeFlag.Position;
        switch (flags) {
            case MeshAttributeFlag.Position:
                return PositionOnly;
            case MeshAttributeFlag.Position|MeshAttributeFlag.Color:
                return ColoredPositions;
            case MeshAttributeFlag.Triangles:
                return BasicTriangleMesh;
            case MeshAttributeFlag.TrianglesNoIndex:
                return BasicTriangleMeshNoIndex;
            case MeshAttributeFlag.Triangles|MeshAttributeFlag.Weight:
                return SkinnedTriangleMesh;
            default:
                throw new NotImplementedException("Unsupported mesh attribute combination " + flags);
        }
    }
}

[Flags]
public enum MeshAttributeFlag
{
    None = 0,
    Position = 1 << 0,
    UV = 1 << 1,
    Normal = 1 << 2,
    Index = 1 << 3,
    Tangent = 1 << 4,
    Weight = 1 << 5,
    Color = 1 << 6,
    Instanced = 1 << 7,

    Triangles = Position|UV|Normal|Index,
    TrianglesNoIndex = Position|UV|Normal,
}
