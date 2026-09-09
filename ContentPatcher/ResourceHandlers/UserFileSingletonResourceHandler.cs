using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("user-singleton", nameof(Deserialize))]
public class UserFileSingletonResourceHandler : ResourceHandler
{
    public static UserFileSingletonResourceHandler Deserialize(ResourceConfig resource, EntityResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new UserFileSingletonResourceHandler() {
            Files = [data.SingleFile],
            Config = resource
        };
    }

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new ObjectField();

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var userfile = workspace.ResourceManager.ReadFileResource<UserFile>(Files[0]);

        var instance = userfile.Instance!;
        var id = Config.IDGeneratorRequired.GetID(instance);
        dict[id] = new RSZObjectResource(instance, Files[0]);
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        throw new NotImplementedException();
    }
}
