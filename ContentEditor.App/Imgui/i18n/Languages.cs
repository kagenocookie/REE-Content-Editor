using ReeLib;

namespace ContentEditor.App;

public class Languages
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
            _ => code,
        };
    }
}
