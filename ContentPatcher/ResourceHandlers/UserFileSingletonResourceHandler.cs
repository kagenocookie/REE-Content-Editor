using System.Text.Json.Nodes;
using ReeLib;

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
        var userfile = workspace.ResourceManager.GetFileContents<UserFile>(Files[0]);

        var instance = userfile.Instance!;
        var id = Config.IDGeneratorRequired.GetID(instance);
        dict[id] = new RSZObjectResource(Config, instance, Files[0]);
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
        throw new NotImplementedException();
    }
}
