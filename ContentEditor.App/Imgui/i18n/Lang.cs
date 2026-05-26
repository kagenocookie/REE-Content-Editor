using System.Reflection;
using System.Text;
using ContentEditor.App.Internationalization;
using ReeLib.Msg;

namespace ContentEditor.App;

public static partial class Lang
{
    public static Language CurrentLanguage { get; private set; } = Language.English;

    private static Dictionary<string, TranslatableBase> Translatables { get; } = new();

    // ignore Arabic because we have no way of RTL text
    // ignore Thai because font
    // ignore Fiction because what
    public static readonly Language[] SupportableLanguages = Enum.GetValues<Language>()
        .Where(x => x is not Language.Max and not Language.Arabic and not Language.Thai and not Language.Fiction)
        .ToArray();

    public static readonly byte[] SupportableLanguageNames = Encoding.UTF8.GetBytes(
        string.Join('\0', SupportableLanguages.Select(TranslateLanguage).ToArray())
    );

    public static string FormatDate(DateTime? dateTime)
    {
        return dateTime == null ? "[unknown date]" : FormatDate(dateTime.Value);
    }

    public static string FormatDate(DateTime dateTime)
    {
        return dateTime.ToLocalTime().ToString(Time.DateTimeFormat);
    }

    public static void ChangeLanguage(Language language)
    {
        if (CurrentLanguage == language) return;

        if (Translatables.Count == 0) {
            FindTranslatables(typeof(Lang), null);
        }

        CurrentLanguage = language;
        LoadTranslations(language);
    }

    public static Dictionary<string, string> GetTranslationsJson()
    {
        if (Translatables.Count == 0) {
            FindTranslatables(typeof(Lang), null);
        }
        var output = new Dictionary<string, string>();
        foreach (var (key, str) in Translatables) {
            output[key] = str.Format;
        }
        return output;
    }

    private static readonly string TranslationsBasePath = Path.Combine(AppContext.BaseDirectory, "i18n");

    private static string GetLangFilepath(Language lang) => Path.Combine(TranslationsBasePath, lang.ToString() + ".lang.yaml");

    private static bool LoadTranslations(Language language, HashSet<string>? whitelistPaths = null)
    {
        var translationFile = GetLangFilepath(language);
        if (!File.Exists(translationFile)) {
            return false;
        }

        var translationBytes = File.ReadAllBytes(translationFile);
        var json = VYaml.Serialization.YamlSerializer.Deserialize<Dictionary<string, string>>(translationBytes);
        if (json == null) return false;

        var missingTranslations = Translatables.ToDictionary();
        foreach (var (key, fmt) in json) {
            if (fmt == null || whitelistPaths?.Contains(key) == false) continue;

            if (missingTranslations.Remove(key, out var translatable)) {
                translatable.Format = fmt;
            } else {
                Logger.Debug($"Found unknown translation key '{key}' for {language} with format '{fmt}'");
            }
        }

        if (missingTranslations.Count > 0 && language != Language.English) {
            Logger.Debug($"Language {language} is missing {missingTranslations.Count} translations:\n{string.Join("\n", missingTranslations.Keys)}");
            // load from english as the fallback language
            LoadTranslations(Language.English, missingTranslations.Keys.ToHashSet());
        }

        return true;
    }

    private static void FindTranslatables(Type ownerType, string? path)
    {
        foreach (var field in ownerType.GetFields(BindingFlags.Static | BindingFlags.Public)) {
            if (field.FieldType.IsAssignableTo(typeof(TranslatableBase))) {
                Translatables[path == null ? field.Name : $"{path}.{field.Name}"] = (TranslatableBase)field.GetValue(null)!;
            } else if (field.FieldType.IsAssignableTo(typeof(TranslatableGroup))) {
                var group = (TranslatableGroup)field.GetValue(null)!;
                foreach (var key in group.Translatables) {
                    Translatables[path == null ? $"{field.Name}.{key.key}" : $"{path}.{field.Name}.{key.key}"] = (TranslatableBase)key.text;
                }
            }
        }

        foreach (var prop in ownerType.GetProperties(BindingFlags.Static | BindingFlags.Public)) {
            if (prop.PropertyType.IsAssignableTo(typeof(TranslatableBase))) {
                Translatables[path == null ? prop.Name : $"{path}.{prop.Name}"] = (TranslatableBase)prop.GetValue(null)!;
            } else if (prop.PropertyType.IsAssignableTo(typeof(TranslatableGroup))) {
                var group = (TranslatableGroup)prop.GetValue(null)!;
                foreach (var key in group.Translatables) {
                    Translatables[path == null ? $"{prop.Name}.{key.key}" : $"{path}.{prop.Name}.{key.key}"] = (TranslatableBase)key.text;
                }
            }
        }

        var subs = ownerType.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var sub in subs) {
            FindTranslatables(sub, path == null ? sub.Name : $"{path}.{sub.Name}");
        }
    }
}
