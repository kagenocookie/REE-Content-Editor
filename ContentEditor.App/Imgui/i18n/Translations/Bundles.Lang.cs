using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Bundles
    {
        public static readonly FixedString Bundle = "Bundle";
        public static readonly FixedString Title = "Bundles";
        public static readonly InterpolatedString<string, string> ConfirmDeleteBundleFile = "Are you sure you want to delete {0} from {1}?";
        public static readonly FixedString LoadOrder = "Load Order";
        public static readonly FixedString NewBundle = "New Bundle";
        public static readonly InterpolatedString<int, int> BundleCount = "Total Bundles: {0} | Active Bundles: {1}";
        public static readonly FixedString CreateFromLooseFiles = "Create Bundle from Loose Files";
        public static readonly FixedString CreateFromPakFile = "Create Bundle from PAK File";
        public static readonly FixedString OpenGameFolder = "Open game folder in File Explorer";
        public static readonly FixedString OpenBundlesFolder = "Open Bundles folder in File Explorer";
        public static readonly FixedString PublishMod = "Publish Mod";
        public static readonly FixedString ApplyPatchesLoose = "Apply patches (Loose file)";
        public static readonly FixedString ApplyPatchesPak = "Apply patches (PAK file)";
        public static readonly FixedString PatchTo = "Patch to...";
        public static readonly FixedString RevertPatches = "Revert patches";
        public static readonly FixedString BundleAlreadyExists = "Bundle already exists!";
        public static readonly FixedString NoBundlesFound = "No Bundles found!";
        public static readonly FixedString SelectBundle = "Select a bundle to view its details";
        public static readonly FixedString SaveBundleMetadata = "Save bundle metadata";
        public static readonly FixedString OpenCurrentBundleFolder = "Open Current Bundle folder in File Explorer";
        public static readonly FixedString UnloadCurrentBundle = "Unload current Bundle";
        public static readonly FixedString RebuildPatchDiffs = "Force Rebuild Patch Diffs";
        public static readonly FixedString OpenFileInEditor = "Open file in Editor";
        public static readonly FixedString EditTargetPathPopup = "EditTargetPath";
        public static readonly FixedString EditTargetPathHeader = "Edit Target Path";
        public static readonly FixedString FileCouldNotBeOpened = "File could not be opened";
        public static readonly InterpolatedString<string> CreatedAt = "Created at: {0}";
        public static readonly InterpolatedString<string> UpdatedAt = "Updated at: {0}";
        public static readonly FixedString EditTargetPathHelp = "The final target file path this file should read as.\nThe platform specific natives/stm/ prefix will be automatically added\nduring patching or publishing and does not need to be included here.";
        public static readonly InterpolatedString<string> ShowChangesTooltip = "Show changes\nPartial patch generated at: {0}";
        public static readonly FixedString BundleNamePlaceholder = "Enter Bundle name here...";
        public static readonly InterpolatedString<string, string> NameVersion_ReadOnly = "Bundle: {0} ({1})";
        public static readonly InterpolatedString<string> Author_ReadOnly = "Author: {0}";
        public static readonly InterpolatedString<string> Homepage_ReadOnly = "Homepage: {0}";
        public static readonly InterpolatedString<string> Description_ReadOnly = "Description:\n{0}";
        public static readonly InterpolatedString<string> BundleLastUpdate_ReadOnly = "Last Update: {0}";
        public static readonly FixedString Author = "Author";
        public static readonly FixedString Homepage = "Homepage";
        public static readonly FixedString Version = "Version";
        public static readonly FixedString Description = "Description";
        public static readonly FixedString Image = "Image";
        public static readonly FixedString Preview = "Preview";
        public static readonly FixedString EntityType = "Type";
        public static readonly FixedString LegacyEntities = "Legacy entities";
        public static readonly FixedString Entities = "Entities";
        public static readonly FixedString Files = "Files";
        public static readonly FixedString UnknownLegacyEntityType = "Unknown legacy entity type";
    }
}
