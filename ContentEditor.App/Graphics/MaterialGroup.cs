using ContentEditor.App.FileLoaders;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Graphics;

public sealed class MaterialGroup
{
    public List<Material> Materials { get; } = new();

    public MaterialGroupWrapper SourceMaterial { get; private set; }

    public ShaderFlags Flags { get; init; }

    public MaterialGroup()
    {
        // TODO initialize data somehow?
        SourceMaterial = new MaterialGroupWrapper(new MdfFile(new FileHandler()));
    }

    public MaterialGroup(params Material[] materials) : this()
    {
        Materials.AddRange(materials);
    }

    public MaterialGroup(MaterialGroupWrapper sourceMaterial)
    {
        SourceMaterial = sourceMaterial;
    }

    public void RefreshParameters(RenderContext context, FileHandle file)
    {
        SourceMaterial = file.Resource as MaterialGroupWrapper ?? SourceMaterial;
        foreach (var mat in Materials) {
            var sourceMat = SourceMaterial.GetByName(mat.name);
            if (sourceMat == null) {
                // remove + unload?
                continue;
            }

            if (sourceMat.AlbedoTexture != null && mat.HasTextureParameter(Silk.NET.OpenGL.TextureUnit.Texture0)) {
                var tex = mat.GetTexture(Silk.NET.OpenGL.TextureUnit.Texture0);
                if (context.UpdateTextureReference(ref tex, sourceMat.AlbedoTexture.texPath, Flags)) {
                    mat.SetParameter(Silk.NET.OpenGL.TextureUnit.Texture0, tex);
                }
            }
            if (sourceMat.BaseColor != null && mat.HasColorParameter("_MainColor")) {
                mat.SetParameter("_MainColor", sourceMat.BaseColor.Color);
            }
        }
    }

    public void Add(Material material)
    {
        Materials.Add(material);
    }

    public Material? GetByName(string name)
    {
        return Materials.FirstOrDefault(m => m.name == name);
    }
    public int GetMaterialIndex(string name)
    {
        return Materials.FindIndex(m => m.name == name);
    }
    public int GetMaterialIndex(Material material)
    {
        return Materials.IndexOf(material);
    }

    public MaterialGroup Clone()
    {
        var grp = new MaterialGroup();
        grp.Materials.AddRange(Materials.Select(m => m.Clone()));
        return grp;
    }

    public override string ToString() => $"[Mat count: {Materials.Count}]";
}
