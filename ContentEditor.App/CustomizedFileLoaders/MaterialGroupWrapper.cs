using ContentPatcher;
using ReeLib;
using ReeLib.Mdf;

namespace ContentEditor.App.FileLoaders;

public class MaterialGroupWrapper(MdfFile mdf2) : BaseFileResource<MdfFile>(mdf2)
{
    private List<MaterialLookupData> lookups = new();
    public IReadOnlyList<MaterialLookupData> Materials => lookups;

    public MaterialLookupData? GetByName(string name)
    {
        foreach (var mat in lookups) {
            if (mat.Name == name) return mat;
        }

        return null;
    }

    public static readonly HashSet<string> AlbedoTextureNames = ["BaseDielectricMap", "ALBD", "ALBDmap", "BackMap", "BaseMetalMap", "BaseDielectricMapBase", "BaseAlphaMap", "BaseShiftMap"];
    public static readonly HashSet<string> NormalTextureNames = ["NormalRoughnessMap", "NormalRoughnessCavityMap"];
    public static readonly HashSet<string> ATXXTextureNames = ["AlphaTranslucentOcclusionCavityMap", "AlphaTranslucentOcclusionSSSMap", "AlphaCavityOcclusionTranslucentMap"];

    public sealed class MaterialLookupData(MaterialData material)
    {
        public string Name { get; } = material.Name;
        public IEnumerable<TexHeader> Textures => material.Textures;

        public ParamHeader? BaseColor { get; set; }

        public TexHeader? AlbedoTexture { get; set; }
        public TexHeader? NormalTexture { get; set; }
        public TexHeader? ATXXTexture { get; set; }

        public override string ToString() => $"{Name} [{AlbedoTexture?.texPath ?? "no albedo"}]";
    }

    public void UpdateMaterialLookups()
    {
        lookups.Clear();
        foreach (var srcMat in File.Materials) {
            var mat = new MaterialLookupData(srcMat);
            // TODO: cache these lookups per mmtr to speed things up a bit?

            foreach (var param in srcMat.Parameters) {
                if (mat.BaseColor == null && param.paramName == "BaseColor") {
                    mat.BaseColor = param;
                }
            }

            foreach (var tex in srcMat.Textures) {
                if (mat.AlbedoTexture == null && !tex.texPath.Contains("/null", StringComparison.InvariantCultureIgnoreCase) && AlbedoTextureNames.Contains(tex.texType)) {
                    mat.AlbedoTexture = tex;
                }
                if (mat.NormalTexture == null && NormalTextureNames.Contains(tex.texType)) {
                    mat.NormalTexture = tex;
                }
                if (mat.ATXXTexture == null && ATXXTextureNames.Contains(tex.texType)) {
                    mat.ATXXTexture = tex;
                }
            }

            mat.AlbedoTexture ??= srcMat.Textures.FirstOrDefault(static tex => AlbedoTextureNames.Contains(tex.texType));
            lookups.Add(mat);
        }
    }
}
