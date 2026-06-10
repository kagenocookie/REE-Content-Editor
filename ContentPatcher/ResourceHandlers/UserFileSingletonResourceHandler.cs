using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("user-singleton", nameof(Deserialize))]
public class UserFileSingletonResourceHandler : ResourceHandler
{
    public override void ReadResources(ContentWorkspace workspace, ClassConfig config, Dictionary<long, IContentResource> dict)
    {
        var userfile = workspace.ResourceManager.ReadFileResource<UserFile>(Files[0]);

        var instance = userfile.RSZ.ObjectList[0];
        var id = IDGenerator.GenerateID(instance, config.IDFields!);
        dict[id] = new RSZObjectResource(instance, Files[0]);
    }

    public static UserFileSingletonResourceHandler Deserialize(string resourceKey, Dictionary<string, object> data)
    {
        var files = new List<string>(((IEnumerable<object>)data["files"]).Cast<string>());
        if (files.Count != 1) {
            throw new InvalidDataException("user-singleton requires exactly one file");
        }
        return new UserFileSingletonResourceHandler() { Files = files };
    }

    public override void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        throw new NotImplementedException();
    }
}
