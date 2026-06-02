using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class MacroShelf
    {
        public static readonly FixedString Tooltip_CreateMacro = "Create a new Macro";
        public static readonly FixedString Tooltip_ClearMacroData = "Clear Macro data";
        public static readonly FixedString Tooltip_RescanMacros = "Re-scan Macros folder";
        public static readonly FixedString Tooltip_OpenMacrosFolder = "Open Macros folder in File Explorer";
        public static readonly FixedString Tooltip_IconColor = "Icon Color";
        public static readonly FixedString Tooltip_MacroType = "Whether or not this macro can be used for multiple games or only the currently selected game.";
        public static readonly FixedString Tooltip_NewGroup = "Define a new Group";
        public static readonly FixedString Tooltip_AddGroup = "Add Group";
        public static readonly FixedString Tooltip_NoDesc = "No description provided...";

        public static readonly FixedString Label_MacroLuaPath = "LUA Script Path";
        public static readonly FixedString Label_MacroName = "Macro Name";
        public static readonly FixedString Label_MacroDesc = "Description";
        public static readonly FixedString Label_MacroGroup = "Group";
        public static readonly FixedString Label_MacroIcon = "Icon";
        public static readonly FixedString Label_MacroType = "Game Specific";
        public static readonly FixedString Label_NoMacros = "No macros added.";
        public static readonly FixedString Label_MacroTypeA = "Type: Current Game";
        public static readonly FixedString Label_MacroTypeB = "Type: Global";

        public static readonly FixedString Hint_NewGroup = "Enter new group name here...";

        public static readonly IconString Button_AddMacro = new IconString("{0} Add Macro", AppIcons.SI_GenericAdd);
        public static readonly IconString Button_SaveMacro = new IconString("{0} Save Macro", AppIcons.SI_Save);
        public static readonly IconString MenuItem_RunMacro = new IconString("{0} Run Macro", AppIcons.Play);
        public static readonly IconString MenuItem_EditMacro = new IconString("{0} Edit Macro metadata", AppIcons.Pencil);
        public static readonly IconString MenuItem_DeleteMacro = new IconString("{0} Delete Macro", AppIcons.SI_GenericDelete2);

        public static readonly FixedString Error_InvalidLuaPath = "Lua file must be inside the User Lua folder!";
        public static readonly InterpolatedString<string> Confirm_DeleteMacroFile = new InterpolatedString<string>("Are you sure you want to delete the {0} macro?");
    }
}
