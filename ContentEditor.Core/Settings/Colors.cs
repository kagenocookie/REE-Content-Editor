using System.Numerics;
using System.Reflection;

namespace ContentEditor;

public static class Colors
{
    public static AppColors Current { get; set; } = new AppColors();

    public static Vector4 Default => Current.Default;
    public static Vector4 Disabled => Current.Disabled;
    public static Vector4 Faded => Current.Faded;
    public static Vector4 Error => Current.Error;
    public static Vector4 Danger => Current.Danger;
    public static Vector4 Warning => Current.Warning;
    public static Vector4 Info => Current.Info;
    public static Vector4 Note => Current.Note;
    public static Vector4 Success => Current.Success;
    public static Vector4 GameObject => Current.GameObject;
    public static Vector4 Folder => Current.Folder;
    public static Vector4 Colliders => new Vector4(0, 1, 0, 0.8f);
    public static Vector4 RequestSetColliders => new Vector4(1, 0.4f, 0.1f, 0.8f);
    public static Vector4 Lights => new Vector4(1, 1, 0, 0.8f);
    public static Vector4 TextActive => Current.TextActive;
    public static Vector4 FileTypePFB => Current.FileTypePFB;
    public static Vector4 FileTypeSCN => Current.FileTypeSCN;
    public static Vector4 FileTypeMESH => Current.FileTypeMESH;
    public static Vector4 FileTypeMCOL => Current.FileTypeMCOL;
    public static Vector4 FileTypeRCOL => Current.FileTypeRCOL;
    public static Vector4 FileTypeMDF => Current.FileTypeMDF;
    public static Vector4 FileTypeMMTR => Current.FileTypeMMTR;
    public static Vector4 FileTypeGPUMOTLIST => Current.FileTypeGPUMOTLIST;
    public static Vector4 FileTypeGPBF => Current.FileTypeGPBF;
    public static Vector4 FileTypeGPUC => Current.FileTypeGPUC;
    public static Vector4 FileTypeUVS => Current.FileTypeUVS;
    public static Vector4 FileTypeUSER => Current.FileTypeUSER;
    public static Vector4 FileTypeUVAR => Current.FileTypeUVAR;
    public static Vector4 FileTypeCOCO => Current.FileTypeCOCO;
    public static Vector4 FileTypeMOT => Current.FileTypeMOT;
    public static Vector4 FileTypeMOTTREE => Current.FileTypeMOTTREE;
    public static Vector4 FileTypeMOTLIST => Current.FileTypeMOTLIST;
    public static Vector4 FileTypeMOTBANK => Current.FileTypeMOTBANK;
    public static Vector4 FileTypeMOTFSM => Current.FileTypeMOTFSM;
    public static Vector4 FileTypeCFIL => Current.FileTypeCFIL;
    public static Vector4 FileTypeCDEF => Current.FileTypeCDEF;
    public static Vector4 FileTypeCMAT => Current.FileTypeCMAT;
    public static Vector4 FileTypeFOL => Current.FileTypeFOL;
    public static Vector4 FileTypeEFX => Current.FileTypeEFX;
    public static Vector4 FileTypeGUI => Current.FileTypeGUI;
    public static Vector4 FileTypeGCP => Current.FileTypeGCP;
    public static Vector4 FileTypeGSTY => Current.FileTypeGSTY;
    public static Vector4 FileTypeGCF => Current.FileTypeGCF;
    public static Vector4 FileTypeCHAIN => Current.FileTypeCHAIN;
    public static Vector4 FileTypeUCURVE => Current.FileTypeUCURVE;
    public static Vector4 FileTypeUCURVELIST => Current.FileTypeUCURVELIST;
    public static Vector4 FileTypeFSM => Current.FileTypeFSM;
    public static Vector4 FileTypeMSG => Current.FileTypeMSG;
    public static Vector4 FileTypeRTEX => Current.FileTypeRTEX;
    public static Vector4 FileTypeTML => Current.FileTypeTML;
    public static Vector4 FileTypeCLIP => Current.FileTypeCLIP;
    public static Vector4 FileTypeTMLFSM2 => Current.FileTypeTMLFSM2;
    public static Vector4 FileTypeMOTCAM => Current.FileTypeMOTCAM;
    public static Vector4 FileTypeMCAMLIST => Current.FileTypeMCAMLIST;
    public static Vector4 FileTypeMCAMBANK => Current.FileTypeMCAMBANK;
    public static Vector4 FileTypeAIMAP => Current.FileTypeAIMAP;
    public static Vector4 FileTypeHF => Current.FileTypeHF;
    public static Vector4 FileTypeCHF => Current.FileTypeCHF;
    public static Vector4 FileTypeBHVT => Current.FileTypeBHVT;
    public static Vector4 FileTypeJMAP => Current.FileTypeJMAP;
    public static Vector4 FileTypeSDFTEX => Current.FileTypeSDFTEX;
    public static Vector4 FileTypeSDF => Current.FileTypeSDF;
    /// <summary>
    /// Main color for icons, it should be the same as the text color of the current theme
    ///</summary>
    public static Vector4 IconPrimary => Current.IconPrimary;
    /// <summary>
    /// Accent color for icons, should be the same as the secondary color of the current theme
    ///</summary>
    public static Vector4 IconSecondary => Current.IconSecondary;
    /// <summary>
    /// Use it in place of the secondary color when the icon removes or deletes something
    ///</summary>
    public static Vector4 IconTertiary => Current.IconTertiary;
    /// <summary>
    /// Active color of icons when used with methods such as ToggleButton etc.
    ///</summary>
    public static Vector4 IconActive => Current.IconActive;
    public static Vector4 IconOverlay => Current.IconOverlay;
    public static Vector4 IconOverlayBackground => Current.IconOverlayBackground;
    public static Vector4 TagAnimation => Current.TagAnimation;
    public static Vector4 TagAnimationHovered => Current.TagAnimationHovered;
    public static Vector4 TagAnimationSelected => Current.TagAnimationSelected;
    public static Vector4 TagCharacter => Current.TagCharacter;
    public static Vector4 TagCharacterHovered => Current.TagCharacterHovered;
    public static Vector4 TagCharacterSelected => Current.TagCharacterSelected;
    public static Vector4 TagDLC => Current.TagDLC;
    public static Vector4 TagDLCHovered => Current.TagDLCHovered;
    public static Vector4 TagDLCSelected => Current.TagDLCSelected;
    public static Vector4 TagEnemy => Current.TagEnemy;
    public static Vector4 TagEnemyHovered => Current.TagEnemyHovered;
    public static Vector4 TagEnemySelected => Current.TagEnemySelected;
    public static Vector4 TagItem => Current.TagItem;
    public static Vector4 TagItemHovered => Current.TagItemHovered;
    public static Vector4 TagItemSelected => Current.TagItemSelected;
    public static Vector4 TagMisc => Current.TagMisc;
    public static Vector4 TagMiscHovered => Current.TagMiscHovered;
    public static Vector4 TagMiscSelected => Current.TagMiscSelected;
    public static Vector4 TagPrefab => Current.TagPrefab;
    public static Vector4 TagPrefabHovered => Current.TagPrefabHovered;
    public static Vector4 TagPrefabSelected => Current.TagPrefabSelected;
    public static Vector4 TagStage => Current.TagStage;
    public static Vector4 TagStageHovered => Current.TagStageHovered;
    public static Vector4 TagStageSelected => Current.TagStageSelected;
    public static Vector4 TagUI => Current.TagUI;
    public static Vector4 TagUIHovered => Current.TagUIHovered;
    public static Vector4 TagUISelected => Current.TagUISelected;
    public static Vector4 TagWeapon => Current.TagWeapon;
    public static Vector4 TagWeaponHovered => Current.TagWeaponHovered;
    public static Vector4 TagWeaponSelected => Current.TagWeaponSelected;
}

public sealed class AppColors
{
    public Vector4 Default = new Vector4(1, 1, 1, 1);
    public Vector4 Disabled = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);
    public Vector4 Faded = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);
    public Vector4 Error = new Vector4(1, 0.4f, 0.4f, 1);
    public Vector4 Danger = new Vector4(1, 0, 0.6f, 1);
    public Vector4 Warning = new Vector4(1, 0.75f, 0.5f, 1);
    public Vector4 Info = new Vector4(0.625f, 0.625f, 1, 1);
    public Vector4 Note = new Vector4(0.53f, 0.93f, 1, 1);
    public Vector4 Success = new Vector4(0.36f, 1, 0.32f, 1);

    public Vector4 GameObject = new Vector4(1, 0.96f, 0.92f, 1);
    public Vector4 Folder = new Vector4(0.9f, 1f, 0.9f, 1);

    public Vector4 TextActive = new Vector4(0.114f, 0.6f, 0.93f, 1);

    public Vector4 FileTypePFB = new Vector4(0.80f, 0.62f, 0.38f, 1);
    public Vector4 FileTypeSCN = new Vector4(0.53f, 0.29f, 0.91f, 1);
    public Vector4 FileTypeMESH = new Vector4(0, 0.54f, 0.94f, 1);
    public Vector4 FileTypeMCOL = new Vector4(0, 0.85f, 1, 1);
    public Vector4 FileTypeRCOL = new Vector4(0.85f, 0.27f, 0.25f, 1);
    public Vector4 FileTypeMDF = new Vector4(0.93f, 0.55f, 0.07f, 1);
    public Vector4 FileTypeMMTR = new Vector4(0.93f, 0.55f, 0.07f, 1);
    public Vector4 FileTypeGPUMOTLIST = new Vector4(0.46f, 0.73f, 0, 1);
    public Vector4 FileTypeGPBF = new Vector4(0.46f, 0.73f, 0, 1);
    public Vector4 FileTypeGPUC = new Vector4(0.46f, 0.73f, 0, 1);
    public Vector4 FileTypeUVS = new Vector4(0.32f, 0.83f, 0.72f, 1);
    public Vector4 FileTypeUSER = new Vector4(0.60f, 0.87f, 1, 1);
    public Vector4 FileTypeUVAR = new Vector4(0.87f, 0.60f, 1, 1);
    public Vector4 FileTypeCOCO = new Vector4(0.95f, 0.17f, 0.39f, 1);
    public Vector4 FileTypeMOT = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMOTTREE = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMOTLIST = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMOTBANK = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMOTFSM = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMOTCAM = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMCAMLIST = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeMCAMBANK = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 FileTypeCFIL = new Vector4(1, 0.70f, 0.16f, 1);
    public Vector4 FileTypeCDEF = new Vector4(1, 0.70f, 0.16f, 1);
    public Vector4 FileTypeCMAT = new Vector4(1, 0.70f, 0.16f, 1);
    public Vector4 FileTypeCHF = new Vector4(1, 0.70f, 0.16f, 1);
    public Vector4 FileTypeFOL = new Vector4(0.15f, 0.65f, 0, 1);
    public Vector4 FileTypeEFX = new Vector4(0.21f, 0.88f, 0, 1);
    public Vector4 FileTypeGUI = new Vector4(1, 0.4f, 0.1f, 0.8f);
    public Vector4 FileTypeGCF = new Vector4(1, 0.4f, 0.1f, 0.8f);
    public Vector4 FileTypeGCP = new Vector4(1, 0.4f, 0.1f, 0.8f);
    public Vector4 FileTypeGSTY = new Vector4(1, 0.4f, 0.1f, 0.8f);
    public Vector4 FileTypeCHAIN = new Vector4(0.16f, 1, 0.76f, 1);
    public Vector4 FileTypeUCURVE = new Vector4(0.84f, 0.61f, 0.52f, 1);
    public Vector4 FileTypeUCURVELIST = new Vector4(0.84f, 0.61f, 0.52f, 1);
    public Vector4 FileTypeFSM = new Vector4(0.35f, 0.55f, 0.98f, 1);
    public Vector4 FileTypeMSG = new Vector4(1, 0.78f, 0, 1);
    public Vector4 FileTypeRTEX = new Vector4(0.98f, 0.36f, 0.57f, 1);
    public Vector4 FileTypeTML = new Vector4(0.98f, 0.36f, 0.57f, 1);
    public Vector4 FileTypeCLIP = new Vector4(0.98f, 0.36f, 0.57f, 1);
    public Vector4 FileTypeTMLFSM2 = new Vector4(0.98f, 0.36f, 0.57f, 1);
    public Vector4 FileTypeAIMAP = new Vector4(0.05f, 0.66f, 0.51f, 1);
    public Vector4 FileTypeBHVT = new Vector4(0.05f, 0.66f, 0.51f, 1);
    public Vector4 FileTypeHF = new Vector4(0, 0.89f, 0.73f, 1);
    public Vector4 FileTypeJMAP = new Vector4(0.98f, 0.65f, 0.76f, 1);
    public Vector4 FileTypeSDFTEX = new Vector4(0.46f, 0.73f, 0, 1);
    public Vector4 FileTypeSDF = new Vector4(0.46f, 0.73f, 0, 1);

    public Vector4 IconPrimary = new Vector4(1, 1, 1, 1);
    public Vector4 IconSecondary = new Vector4(0.114f, 0.6f, 0.93f, 1);
    public Vector4 IconTertiary = new Vector4(1, 0.4f, 0.4f, 1);
    public Vector4 IconActive = new Vector4(0.114f, 0.6f, 0.93f, 1);
    public Vector4 IconOverlay = new Vector4(0.114f, 0.6f, 0.93f, 1);
    public Vector4 IconOverlayBackground = new Vector4(0.1f, 0.1f, 0.1f, 0.8f);

    public Vector4 TagAnimation = new Vector4(0.6f, 0.6f, 1, 0.5f);
    public Vector4 TagAnimationHovered = new Vector4(0.6f, 0.6f, 1, 0.8f);
    public Vector4 TagAnimationSelected = new Vector4(0.6f, 0.6f, 1, 1);
    public Vector4 TagCharacter = new Vector4(0.2f, 0.6f, 1, 0.5f);
    public Vector4 TagCharacterHovered = new Vector4(0.2f, 0.6f, 1, 0.8f);
    public Vector4 TagCharacterSelected = new Vector4(0.2f, 0.6f, 1, 1);
    public Vector4 TagDLC = new Vector4(1, 0, 1, 0.5f);
    public Vector4 TagDLCHovered = new Vector4(1, 0, 1, 0.8f);
    public Vector4 TagDLCSelected = new Vector4(1, 0, 1, 1);
    public Vector4 TagEnemy = new Vector4(0.9f, 0.3f, 0.3f, 0.5f);
    public Vector4 TagEnemyHovered = new Vector4(0.9f, 0.3f, 0.3f, 0.8f);
    public Vector4 TagEnemySelected = new Vector4(0.9f, 0.3f, 0.3f, 1);
    public Vector4 TagItem = new Vector4(0.3f, 0.8f, 0.3f, 0.5f);
    public Vector4 TagItemHovered = new Vector4(0.4f, 0.9f, 0.4f, 0.8f);
    public Vector4 TagItemSelected = new Vector4(0.2f, 0.7f, 0.2f, 1);
    public Vector4 TagMisc = new Vector4(0.9f, 0.8f, 0.2f, 0.5f);
    public Vector4 TagMiscHovered = new Vector4(1, 0.9f, 0.3f, 0.8f);
    public Vector4 TagMiscSelected = new Vector4(0.8f, 0.7f, 0.1f, 1);
    public Vector4 TagPrefab = new Vector4(0.80f, 0.62f, 0.38f, 0.5f);
    public Vector4 TagPrefabHovered = new Vector4(0.80f, 0.62f, 0.38f, 0.8f);
    public Vector4 TagPrefabSelected = new Vector4(0.80f, 0.62f, 0.38f, 1);
    public Vector4 TagStage = new Vector4(0.6f, 0.3f, 0.9f, 0.5f);
    public Vector4 TagStageHovered = new Vector4(0.6f, 0.3f, 0.9f, 0.8f);
    public Vector4 TagStageSelected = new Vector4(0.6f, 0.3f, 0.9f, 1);
    public Vector4 TagUI = new Vector4(1, 0.4f, 0.1f, 0.5f);
    public Vector4 TagUIHovered = new Vector4(1, 0.4f, 0.1f, 0.8f);
    public Vector4 TagUISelected = new Vector4(1, 0.4f, 0.1f, 1);
    public Vector4 TagWeapon = new Vector4(0.9f, 0.3f, 0.3f, 0.5f);
    public Vector4 TagWeaponHovered = new Vector4(1, 0.4f, 0.4f, 0.8f);
    public Vector4 TagWeaponSelected = new Vector4(0.8f, 0.2f, 0.2f, 1);
    public static AppColors GetDarkThemeColors() => new AppColors();

    public static AppColors GetLightThemeColors() => new AppColors() {
        Default = new Vector4(0.02f, 0, 0, 1),
        Disabled = new Vector4(0.5f, 0.5f, 0.5f, 0.9f),
        Faded = new Vector4(0.18f, 0.27f, 0.37f, 0.8f),
        Error = new Vector4(1, 0.13f, 0.13f, 1),
        Danger = new Vector4(1, 0, 0.6f, 1),
        Warning = new Vector4(0.93f, 0.6f, 0.24f, 1),
        Info = new Vector4(0.34f, 0.5f, 0.88f, 1),
        Note = new Vector4(0.22f, 0.08f, 0.66f, 1),
        Success = new Vector4(0.04f, 0.73f, 0.13f, 1),
        GameObject = new Vector4(0.36f, 0, 0, 1),
        Folder = new Vector4(0.03f, 0.28f, 0.065f, 1),
    };

    public static readonly FieldInfo[] ColorFields = typeof(AppColors).GetFields().Where(f => !f.IsStatic).ToArray();
}
