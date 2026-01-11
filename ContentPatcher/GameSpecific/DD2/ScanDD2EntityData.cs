using System.Text.Json;
using ReeLib;
using ReeLib.Common;
using ReeLib.Il2cpp;

namespace ContentPatcher.DD2;

public class ScanDD2EntityData : IEntitySetup
{
    public string[]? SupportedGames => [GameIdentifier.dd2.name];

    public void Setup(ContentWorkspace workspace)
    {
        ScanArmors(workspace);
    }

    private static void ScanArmors(ContentWorkspace workspace)
    {
        // note: accessing the files directly because entity setup happens too early to use the ResourceManager
        var armorFile = new UserFile(workspace.Env.RszFileOption, new FileHandler(workspace.Env.GetFile("natives/stm/appsystem/item/itemdata/itemarmordata.user.2")!));
        var itemNames = new MsgFile(new FileHandler(workspace.Env.GetFile("natives/stm/message/ui/itemname.msg.22")!));
        armorFile.Read();
        itemNames.Read();
        var nameDict = itemNames.ToNameDictionary(ReeLib.Msg.Language.English);
        var listContainer = armorFile.RSZ.ObjectList[0];
        var listFieldIndex = Array.FindIndex(listContainer.Fields, f => f.array);
        var list = ((List<object>)listContainer.Values[listFieldIndex]).Cast<RszInstance>();

        var helms = workspace.Env.TypeCache.GetEnumDescriptor("app.HelmStyle");
        var helmsNo = workspace.Env.TypeCache.GetEnumDescriptor("ce.HelmStyleNo");
        if (helmsNo.IsEmpty) helmsNo = workspace.Env.TypeCache.CreateEnum("ce.HelmStyleNo", "System.Int16")!;

        var tops = workspace.Env.TypeCache.GetEnumDescriptor("app.TopsStyle");
        var topsNo = workspace.Env.TypeCache.GetEnumDescriptor("ce.TopsStyleNo");
        if (topsNo.IsEmpty) topsNo = workspace.Env.TypeCache.CreateEnum("ce.TopsStyleNo", "System.Int16")!;

        var pants = workspace.Env.TypeCache.GetEnumDescriptor("app.PantsStyle");
        var pantsNo = workspace.Env.TypeCache.GetEnumDescriptor("ce.PantsStyleNo");
        if (pantsNo.IsEmpty) pantsNo = workspace.Env.TypeCache.CreateEnum("ce.PantsStyleNo", "System.Int16")!;

        var mantles = workspace.Env.TypeCache.GetEnumDescriptor("app.MantleStyle");
        var mantlesNo = workspace.Env.TypeCache.GetEnumDescriptor("ce.MantleStyleNo");
        if (mantlesNo.IsEmpty) mantlesNo = workspace.Env.TypeCache.CreateEnum("ce.MantleStyleNo", "System.Int16")!;

        var facewear = workspace.Env.TypeCache.GetEnumDescriptor("app.FacewearStyle");
        var facewearNo = workspace.Env.TypeCache.GetEnumDescriptor("ce.FacewearStyleNo");
        if (facewearNo.IsEmpty) facewearNo = workspace.Env.TypeCache.CreateEnum("ce.FacewearStyleNo", "System.Int16")!;

        foreach (var item in list) {
            var id = item.Get(RszFieldCache.DD2.ItemCommonParam._Id);
            var equipType = item.Get(RszFieldCache.DD2.ItemArmorParam._EquipCategory);
            var styleNo = item.Get(RszFieldCache.DD2.ItemArmorParam._StyleNo);

            var nameKey = $"item_name_{id}";
            nameDict.TryGetValue(nameKey, out var name);

            EnumDescriptor styleHashEnum, styleNoEnum;
            string styleName;
            switch (equipType) {
                case 2:
                    styleName = $"Helm_{styleNo:D03}";
                    styleHashEnum = helms;
                    styleNoEnum = helmsNo;
                    break;
                case 3:
                    styleName = $"Tops_{styleNo:D03}";
                    styleHashEnum = tops;
                    styleNoEnum = topsNo;
                    break;
                case 4:
                    styleName = $"Pants_{styleNo:D03}";
                    styleHashEnum = pants;
                    styleNoEnum = pantsNo;
                    break;
                case 5:
                    styleName = $"Mantle_{styleNo:D03}";
                    styleHashEnum = mantles;
                    styleNoEnum = mantlesNo;
                    break;
                case 7:
                    styleName = $"Facewear_{styleNo:D03}";
                    styleHashEnum = facewear;
                    styleNoEnum = facewearNo;
                    break;
                default:
                    continue;
            }

            styleNoEnum.AddValue(styleNo, styleName, $"{styleName} {name}".Trim());
            if (!string.IsNullOrEmpty(name)) {
                var valueEl = styleHashEnum.BackingType == typeof(int)
                    ? JsonSerializer.SerializeToElement((int)MurMur3HashUtils.GetHash(styleName))
                    : JsonSerializer.SerializeToElement(MurMur3HashUtils.GetHash(styleName));
                styleHashEnum.SetDisplayLabel(valueEl, $"{styleName} {name}".Trim());
            }
        }
    }
}
