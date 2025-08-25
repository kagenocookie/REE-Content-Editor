using System.Numerics;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class MaterialGroup
{
    public Dictionary<string, Material> Materials { get; } = new();

    public MaterialGroup()
    {
    }

    public Material? Get(string name) => Materials.GetValueOrDefault(name);
    public bool Add(string name, Material material) => Materials.TryAdd(name, material);

    public override string ToString() => $"[Mat count: {Materials.Count}]";
}
