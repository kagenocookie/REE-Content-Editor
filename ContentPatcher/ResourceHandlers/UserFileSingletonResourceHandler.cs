using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("user-singleton", nameof(Deserialize))]
public class UserFileSingletonResourceHandler : ResourceHandler
{
    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        var userfile = workspace.ResourceManager.ReadFileResource<UserFile>(file!);

        var instance = userfile.RSZ.ObjectList[0];
        var id = IDGenerator.GenerateID(instance, config.IDFields!);
        dict[id] = new RSZObjectResource(instance, file!);
    }

    public static UserFileSingletonResourceHandler Deserialize(string resourceKey, Dictionary<string, object> data)
    {
        return new UserFileSingletonResourceHandler() { file = (string)data["file"] };
    }

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        throw new NotImplementedException();
    }
}
