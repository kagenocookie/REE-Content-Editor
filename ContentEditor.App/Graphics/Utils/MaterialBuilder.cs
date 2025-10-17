using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public struct MaterialBuilder(RenderContext context)
{
    private Material? mat;

    public MaterialBuilder From(Material mat)
    {
        this.mat = mat.Clone();
        return this;
    }

    public MaterialBuilder Builtin(BuiltInMaterials type, string name)
    {
        mat = context.GetBuiltInMaterial(type);
        mat.name = name;
        return this;
    }

    public MaterialBuilder Color(string propertyName, Color color)
    {
        mat!.SetParameter(propertyName, color);
        return this;
    }

    public MaterialBuilder Blend(BlendingFactor BlendModeSrc = BlendingFactor.SrcAlpha, BlendingFactor BlendModeDest = BlendingFactor.OneMinusSrcAlpha)
    {
        mat!.BlendMode = new MaterialBlendMode(true, BlendModeSrc, BlendModeDest);
        return this;
    }

    public MaterialBuilder NoBlend()
    {
        mat!.BlendMode = new MaterialBlendMode(false);
        return this;
    }

    public Material Create(string? name = null)
    {
        return mat!.Clone(name ?? mat.name);
    }

    public (Material, Material) Create2(string name1, string name2)
    {
        return (mat!.Clone(name1), mat!.Clone(name2));
    }

    public Material Get()
    {
        return mat!;
    }

    public static implicit operator Material(MaterialBuilder mbr) => mbr.Get();
}

public static class MaterialBuilderExtensions
{
    public static MaterialBuilder GetMaterialBuilder(this RenderContext material) => new MaterialBuilder(material);
    public static MaterialBuilder GetMaterialBuilder(this RenderContext material, BuiltInMaterials builtin, string name = "") => new MaterialBuilder(material).Builtin(builtin, name);
    public static MaterialBuilder GetBuilder(this Material material, RenderContext context) => new MaterialBuilder(context).From(material);
}