using System.Text;
using System.Text.RegularExpressions;
using ContentEditor.Core;
using ReeLib.Msg;
using VYaml.Serialization;
using System.Text.Json;
using Hexa.NET.ImGui;

namespace ContentEditor.App.Tests;

[NonParallelizable]
public class LocalizationTests
{
    [TearDown]
    public void RestoreEnglish()
    {
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Lang.ChangeLanguage(Language.English);
        FixedString.SetTranslations(new(), new());
    }

    [Test]
    public void ChineseTranslationsUseRegisteredKeysAndPreservePlaceholders()
    {
        Lang.ChangeLanguage(Language.English);
        var english = Lang.GetTranslationsJson();
        var path = Path.Combine(AppContext.BaseDirectory, "i18n", "SimplifiedChinese.lang.yaml");
        var chinese = YamlSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path));
        Assert.That(chinese, Is.Not.Null.And.Not.Empty);
        Assert.That(chinese!.Keys, Is.EquivalentTo(english.Keys), "All registered UI strings require a translation.");
        foreach (var (key, value) in chinese!) {
            Assert.That(english.ContainsKey(key), Is.True, $"Unregistered key: {key}");
            var placeholders = (string text) => Regex.Matches(text, @"\{[^{}]+\}")
                .Select(match => match.Value).Distinct().Order().ToArray();
            Assert.That(placeholders(value), Is.EqualTo(placeholders(english[key])), key);
        }
    }

    [Test]
    public void SwitchingLanguagesRestoresEnglishWithoutAnEnglishLanguagePack()
    {
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Assert.That(Lang.Home.Menu_File.String, Is.EqualTo("文件"));
        Lang.ChangeLanguage(Language.English);
        Assert.That(Lang.Home.Menu_File.String, Is.EqualTo("File"));
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Lang.ChangeLanguage(Language.French);
        Assert.That(Lang.Home.Menu_File.String, Is.EqualTo("File"));
    }

    [Test]
    public void IconLabelsKeepStringAndUtf8InSync()
    {
        var label = new IconString("{0} Settings", '*');
        label.Format = "{0} 设置";
        Assert.That(label.String, Is.EqualTo("* 设置"));
        Assert.That(Encoding.UTF8.GetString(label.UTF8).TrimEnd('\0'), Is.EqualTo(label.String));
        Assert.That(label.UTF8[^1], Is.Zero, "Native ImGui text must be null terminated.");
    }

    [Test]
    public void DynamicRegistryStringsClearCachedArgumentsWhenLanguageChanges()
    {
        Lang.ChangeLanguage(Language.English);
        Assert.That(Decode(Lang.Home.ActiveGame.Format("Name")), Is.EqualTo("Game: Name"));
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Assert.That(Decode(Lang.Home.ActiveGame.Format("Name")), Is.EqualTo("游戏：Name"));
        Lang.ChangeLanguage(Language.English);
        Assert.That(Decode(Lang.Home.ActiveGame.Format("Name")), Is.EqualTo("Game: Name"));
    }

    [Test]
    public void AllUiTranslationsPreserveFormatTokensAndAreNotBlank()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "SimplifiedChinese.ui.json")));
        var seen = new HashSet<string>();
        foreach (var property in json.RootElement.EnumerateObject()) {
            Assert.That(seen.Add(property.Name), Is.True, $"Duplicate key: {property.Name}");
            var translated = property.Value.GetString()!;
            Assert.That(translated, Is.Not.Empty, property.Name);
            var tokens = (string text) => Regex.Matches(text, @"\{[^{}]+\}").Select(m => m.Value).Order().ToArray();
            Assert.That(tokens(translated), Is.EqualTo(tokens(property.Name)), property.Name);
            if (Regex.IsMatch(property.Name, @"\{\d")) {
                Assert.DoesNotThrow(() => System.Text.CompositeFormat.Parse(translated), property.Name);
            }
        }
    }

    [Test]
    public void InterpolationTranslatesTemplateWithoutChangingUserArguments()
    {
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        var userPath = "Name";
        Assert.That(UiText.F($"File {userPath} not found."), Is.EqualTo("未找到文件 Name。"));
        Assert.That(UiText.T("natives/stm/name/file.mesh.1"), Is.EqualTo("natives/stm/name/file.mesh.1"));
    }

    [TestCase("Save")]
    [TestCase("Save##file1")]
    [TestCase("Save###persistent-window")]
    [TestCase("##hidden-control")]
    public void LabelsKeepTheirNativeImGuiIdAcrossLanguages(string source)
    {
        Lang.ChangeLanguage(Language.English);
        var english = UiText.Label(source);
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        var chinese = UiText.Label(source);
        Assert.That(ImGuiP.ImHashStr(chinese), Is.EqualTo(ImGuiP.ImHashStr(english)));
        if (!source.StartsWith("##")) Assert.That(chinese, Does.StartWith("保存###"));
    }

    [Test]
    public void InterpolatedLabelsTranslateVisibleFormatAndKeepHiddenIdentity()
    {
        Lang.ChangeLanguage(Language.English);
        var english = UiText.FormatLabel($"{'*'} Add##{42}");
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        var chinese = UiText.FormatLabel($"{'*'} Add##{42}");
        Assert.That(chinese, Does.StartWith("* 添加###"));
        Assert.That(ImGuiP.ImHashStr(chinese), Is.EqualTo(ImGuiP.ImHashStr(english)));
    }

    [Test]
    public void CachedLabelsIconsAndUtf8FollowLanguageSwitches()
    {
        Lang.ChangeLanguage(Language.English);
        var cached = FixedString.Cached("Save");
        var direct = new FixedString("Save");
        var icon = new IconString("{0} Add", '*');
        var english = Decode(UiText.Utf8("Save"));
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Assert.That(cached.String, Is.EqualTo("保存"));
        Assert.That(direct.String, Is.EqualTo("保存"));
        Assert.That(icon.String, Is.EqualTo("* 添加"));
        Assert.That(Decode(UiText.Utf8("Save")), Is.EqualTo("保存"));
        Lang.ChangeLanguage(Language.English);
        Assert.That(cached.String, Is.EqualTo("Save"));
        Assert.That(direct.String, Is.EqualTo("Save"));
        Assert.That(icon.String, Is.EqualTo("* Add"));
        Assert.That(Decode(UiText.Utf8("Save")), Is.EqualTo(english));
    }

    [Test]
    public void ContextSpecificCacheDoesNotLeakBetweenEditors()
    {
        FixedString.SetTranslations(new(), new() { ["a"] = new() { ["Word"] = "甲" }, ["b"] = new() { ["Word"] = "乙" } });
        var a = FixedString.Cached("Word", "a");
        var b = FixedString.Cached("Word", "b");
        Assert.That(a.String, Is.EqualTo("甲"));
        Assert.That(b.String, Is.EqualTo("乙"));
        FixedString.SetTranslations(new(), new());
        Assert.That(a.String, Is.EqualTo("Word"));
        Assert.That(b.String, Is.EqualTo("Word"));
    }

    [Test]
    public void MultilineTranslationsWorkWithEitherSourceLineEnding()
    {
        UiText.SetTranslations(new Dictionary<string, string> { ["First\r\nSecond"] = "第一行\n第二行" });
        Assert.That(UiText.T("First\nSecond"), Is.EqualTo("第一行\n第二行"));
        Assert.That(UiText.T("First\r\nSecond"), Is.EqualTo("第一行\n第二行"));
    }

    [Test]
    public void EnumDisplayNamesRefreshWithoutWritingConfiguration()
    {
        Lang.ChangeLanguage(Language.English);
        var index = Array.IndexOf(Lang.Settings.Keys.Values, ImGuiKey.LeftArrow);
        Assert.That(Lang.Settings.Keys.NameStrings[index], Is.EqualTo("Left Arrow"));
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Assert.That(Lang.Settings.Keys.NameStrings[index], Is.EqualTo("左方向键"));
    }

    [Test]
    public void ContextLookupStillUsesTheOriginalKeyAfterTranslation()
    {
        Lang.ChangeLanguage(Language.English);
        var root = UIContext.CreateRootContext("root", new object());
        var child = root.AddChild("Skeleton", new object());
        Lang.ChangeLanguage(Language.SimplifiedChinese);
        Assert.That(child.label.String, Is.EqualTo("骨架"));
        Assert.That(root.GetChild("Skeleton"), Is.SameAs(child));
        Lang.ChangeLanguage(Language.English);
        Assert.That(root.GetChild("Skeleton"), Is.SameAs(child));
    }

    private static string Decode(ReadOnlySpan<byte> value) => Encoding.UTF8.GetString(value).TrimEnd('\0');
}
