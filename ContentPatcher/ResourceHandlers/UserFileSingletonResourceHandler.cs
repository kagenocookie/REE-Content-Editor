using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

[ResourcePatcher("user-singleton")]
public class UserFileSingletonResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new UserFileSingletonResourceHandler() {
            Files = [data.SingleFile],
            Config = resource
        };
    }

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new ObjectField();

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var filepath = Files[0];
        var userfile = workspace.ResourceManager.GetFileContents<UserFile>(filepath);

        var instance = userfile.Instance!;
        var id = MurMur3HashUtils.GetHash(filepath);
        dict[id] = new RSZObjectResource(Config, instance, filepath);
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        if (resource is not RSZObjectResource obj) {
            throw new NotImplementedException($"Can't create new resources of type {Config.Resource} ({Config.Type})");
        }

        workspace.Diff.ApplyDiff(obj.Instance, data);
        return obj;
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        foreach (var (id, res) in resources) {
            if (res is not RSZObjectResource resource || string.IsNullOrEmpty(res.FileResourcePath)) {
                continue;
            }

            if (workspace.ResourceManager.TryResolveGameFile(res.FileResourcePath, out var file)) {
                var user = file.GetFile<UserFile>();
                if (user.Instance != resource.Instance) {
                    user.Clear();
                    resource.Instance.Index = -1;
                    user.RSZ.AddToObjectTable(resource.Instance);
                    file.Modified = true;
                }
            }
        }
    }
}
