using ContentEditor.App.FileLoaders;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App.Graphics;

public sealed class MaterialGroup
{
    public List<Material> Materials { get; } = new();
    public uint ContentHash { get; private set; }

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
        UpdateHash();
    }

    public MaterialGroup(MaterialGroupWrapper sourceMaterial)
    {
        SourceMaterial = sourceMaterial;
    }

    public void RefreshParameters(RenderContext context, FileHandle file)
    {
        SourceMaterial = file.Resource as MaterialGroupWrapper ?? SourceMaterial;
        var hash = 0;
        for (int i = 0; i < SourceMaterial.Materials.Count; i++) {
            var sm = SourceMaterial.Materials[i];
            var index = GetMaterialIndex(sm.NameHash);
            if (index == -1) {
                context.LoadSingleMaterial(file, this, sm);
            }
        }
        for (int i = 0; i < Materials.Count; i++) {
            var mat = Materials[i];
            var sourceMat = SourceMaterial.GetByName(mat.Name);
            if (sourceMat == null) {
                // remove + unload resources (textures) from context
                var rmMat = Materials[i];
                Materials.RemoveAt(i--);
                context.UnloadMaterial(rmMat);
                continue;
            }

            if (sourceMat.AlbedoTexture != null && mat.HasTextureParameter(Silk.NET.OpenGL.TextureUnit.Texture0)) {
                var tex = mat.GetTexture(Silk.NET.OpenGL.TextureUnit.Texture0);
                if (context.UpdateTextureReference(ref tex, sourceMat.AlbedoTexture.texPath, Flags)) {
                    mat.SetParameter(Silk.NET.OpenGL.TextureUnit.Texture0, tex);
                }
            }
            if (mat.HasColorParameter("_MainColor")) {
                if (sourceMat.BaseColor != null) {
                    mat.SetParameter("_MainColor", sourceMat.BaseColor.Color);
                } else {
                    mat.SetParameter("_MainColor", new Color(uint.MaxValue));
                }
            }
            hash = HashCode.Combine(hash, mat.Hash);
        }
        ContentHash = (uint)hash;
    }

    private void UpdateHash()
    {
        var hash = 1;
        foreach (var mat in Materials) {
            hash = HashCode.Combine(hash, mat.Hash);
        }
        ContentHash = (uint)hash;
    }

    public void Add(Material material)
    {
        Materials.Add(material);
        ContentHash = (uint)HashCode.Combine((int)ContentHash, material.Hash);
    }

    public Material? GetByName(string name)
    {
        return Materials.FirstOrDefault(m => m.Name == name);
    }
    public int GetMaterialIndex(uint nameHash)
    {
        return Materials.FindIndex(m => (m.SourceMaterial?.NameHash ?? m.NameHash) == nameHash);
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
