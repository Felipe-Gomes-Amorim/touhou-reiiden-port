namespace Th01.Platform.MonoGame.Launcher;

public enum LauncherLanguage { English = 0, Japanese = 1 }

/// <summary>Every piece of launcher text. The launcher has two languages, English and Japanese; there is no Portuguese.</summary>
public enum Str
{
    Title, Subtitle,
    MenuPlay, MenuOptions, MenuDirectory, MenuQuit,
    Notice, Disclaimer,
    HintNavigate, HintSelect, HintBack, HintCategory, HintChange,
    AboutLink, LanguageSwitch,
    AboutHeading, AboutProject, AboutLauncher, AboutBasedOn, AboutBasedOnText, AboutOriginal, AboutOriginalText, AboutLink2, AboutNote,
    OptionsHeading,
    TabAccessibility, TabVideo, TabAudio, TabFont, TabControls, TabGame, TabData, TabDiagnostics,
}

public static class LauncherStrings
{
    public const string ProjectUrl = "https://github.com/Felipe-Gomes-Amorim/touhou-reiiden-port";

    static readonly Dictionary<Str, (string En, string Ja)> Table = new()
    {
        [Str.Title] = ("Touhou Reiiden — fan port", "東方靈異伝 ファンポート"),
        [Str.Subtitle] = ("(東方靈異伝 ~ The Highly Responsive to Prayers)", "(東方靈異伝 ~ The Highly Responsive to Prayers)"),
        [Str.MenuPlay] = ("PLAY", "はじめる"),
        [Str.MenuOptions] = ("OPTIONS", "オプション"),
        [Str.MenuDirectory] = ("DIRECTORY", "ゲームの場所"),
        [Str.MenuQuit] = ("QUIT", "おわる"),
        [Str.Notice] = ("NOTICE", "注意"),
        [Str.Disclaimer] = ("Unofficial fan work. Requires your own copy of the original game.", "非公式の二次創作です。プレイには原作のゲームが必要です。"),
        [Str.HintNavigate] = ("Navigate", "えらぶ"),
        [Str.HintSelect] = ("Select", "決定"),
        [Str.HintBack] = ("Back", "戻る"),
        [Str.HintCategory] = ("Category", "カテゴリ"),
        [Str.HintChange] = ("Change", "変更"),
        [Str.AboutLink] = ("About & credits", "概要とクレジット"),
        [Str.LanguageSwitch] = ("EN / 日本語", "EN / 日本語"),
        [Str.AboutHeading] = ("ABOUT & CREDITS", "概要とクレジット"),
        [Str.AboutProject] = ("PROJECT", "プロジェクト"),
        [Str.AboutLauncher] = ("THIS LAUNCHER", "このランチャー"),
        [Str.AboutBasedOn] = ("BASED ON", "参考にしたもの"),
        [Str.AboutBasedOnText] = ("ReC98, the code restoration project for the first five PC-98 Touhou games.", "ReC98(PC-98版東方Project初期5作のコード復元プロジェクト)"),
        [Str.AboutOriginal] = ("ORIGINAL WORK", "原作"),
        [Str.AboutOriginalText] = ("Touhou Project by ZUN / Team Shanghai Alice.", "東方Project:ZUN / 上海アリス幻樂団"),
        [Str.AboutLink2] = ("PROJECT LINK", "プロジェクトのリンク"),
        [Str.AboutNote] = ("Unofficial fan work. Requires your own copy of the original game. No game files are distributed with this program.", "非公式の二次創作です。プレイには原作のゲームが必要です。このプログラムにゲームのファイルは含まれません。"),
        [Str.OptionsHeading] = ("OPTIONS", "オプション"),
        [Str.TabAccessibility] = ("Accessibility", "アクセシビリティ"),
        [Str.TabVideo] = ("Video", "映像"),
        [Str.TabAudio] = ("Audio", "音声"),
        [Str.TabFont] = ("Font & language", "文字と言語"),
        [Str.TabControls] = ("Controls", "操作"),
        [Str.TabGame] = ("Game", "ゲーム"),
        [Str.TabData] = ("Data", "データ"),
        [Str.TabDiagnostics] = ("Diagnostics", "診断"),
    };

    public static IEnumerable<Str> All => Table.Keys;

    public static string Get(Str key, LauncherLanguage language) =>
        language == LauncherLanguage.Japanese ? Table[key].Ja : Table[key].En;
}
