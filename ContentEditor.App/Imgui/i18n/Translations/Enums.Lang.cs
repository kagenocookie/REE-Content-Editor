using ReeLib;
using ReeLib.Msg;

namespace ContentEditor.App;

public partial class Lang
{
    public static string TranslateGame(string code)
    {
        return code switch {
            nameof(GameName.dmc5) => "Devil May Cry 5",
            nameof(GameName.dd2) => "Dragon's Dogma 2",
            nameof(GameName.re2) => "Resident Evil 2",
            nameof(GameName.re2rt) => "Resident Evil 2 (RT)",
            nameof(GameName.re3) => "Resident Evil 3",
            nameof(GameName.re3rt) => "Resident Evil 3 (RT)",
            nameof(GameName.re4) => "Resident Evil 4",
            nameof(GameName.re7) => "Resident Evil 7",
            nameof(GameName.re7rt) => "Resident Evil 7 (RT)",
            nameof(GameName.re8) => "Resident Evil 8",
            nameof(GameName.re9) => "Resident Evil 9",
            nameof(GameName.sf6) => "Street Fighter 6",
            nameof(GameName.mhrise) => "Monster Hunter Rise",
            nameof(GameName.mhwilds) => "Monster Hunter Wilds",
            nameof(GameName.mhsto3) => "Monster Hunter Stories 3",
            nameof(GameName.oni2) => "Onimusha 2",
            nameof(GameName.drdr) => "Dead Rising Deluxe Remaster",
            nameof(GameName.apollo) => "Apollo Justice: Ace Attorney",
            nameof(GameName.gtrick) => "Ghost Trick",
            nameof(GameName.kunitsu) => "Kunitsu-Gami",
            nameof(GameName.pragmata) => "Pragmata",
            nameof(GameName.oniws) => "Onimusha: WotS",
            _ => code,
        };
    }

    public static string TranslateLanguage(Language lang)
    {
        return lang switch {
            Language.Bulgarian => "български",
            Language.Czech => "Čeština",
            Language.Danish => "Dansk",
            Language.Dutch => "Nederlands",
            Language.English => "English",
            Language.German => "Deutsch",
            Language.Greek => "ελληνικά",
            Language.French => "Français",
            Language.Finnish => "Suomi",
            Language.Hindi => "हिन्दी",
            Language.Hungarian => "Magyar",
            Language.Italian => "Italiano",
            Language.Indonesian => "Bahasa Indonesia",
            Language.Japanese => "日本語",
            Language.Korean => "한국어",
            Language.LatinAmericanSpanish => "Español latinoamericano",
            Language.Norwegian => "Norsk",
            Language.Polish => "Polski",
            Language.Portuguese => "Português",
            Language.PortugueseBr => "Português brasileiro",
            Language.Romanian => "Românește",
            Language.Russian => "Русский",
            Language.SimplifiedChinese => "简化字",
            Language.Slovak => "Slovenčina",
            Language.Spanish => "Español",
            Language.Swedish => "Svenska",
            Language.Thai => "ภาษาไทย",
            Language.TraditionalChinese => "正体字",
            Language.Turkish => "Türkçe",
            Language.Ukrainian => "Українська",
            Language.Vietnamese => "Tiếng Việt",
            _ => lang.ToString(),
        };
    }
}
