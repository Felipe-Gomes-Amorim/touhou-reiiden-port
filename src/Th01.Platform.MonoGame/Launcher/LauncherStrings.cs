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
    PanelGameInUse, PanelOrigin, PanelState, PanelVersion, StateValid, StateInvalid, VerJapanese, VerTranslated, VerUnknown, ButtonChange,
    FirstRunHeader, FirstRunLead, FirstRunBody, FirstRunStateNone, ButtonSelect, PlayOff,
    ErrNotFound, ErrDiskImage, ErrMissingFile, ErrDamaged,
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
        [Str.PanelGameInUse] = ("GAME IN USE", "使用中のゲーム"),
        [Str.PanelOrigin] = ("ORIGIN", "場所"),
        [Str.PanelState] = ("STATE", "状態"),
        [Str.PanelVersion] = ("DETECTED VERSION", "検出された版"),
        [Str.StateValid] = ("VALID", "有効"),
        [Str.StateInvalid] = ("NO GAME SELECTED", "ゲーム未選択"),
        [Str.VerJapanese] = ("JAPANESE (ORIGINAL)", "日本語版(原作)"),
        [Str.VerTranslated] = ("TRANSLATED", "翻訳版"),
        [Str.VerUnknown] = ("UNKNOWN", "不明"),
        [Str.ButtonChange] = ("Change…", "変更…"),
        [Str.FirstRunHeader] = ("FIRST RUN", "はじめての起動"),
        [Str.FirstRunLead] = ("Before playing, point to your copy of the original game.", "遊ぶ前に、原作ゲームの場所を指定してください。"),
        [Str.FirstRunBody] = ("Select the game file or folder. The launcher remembers your choice.", "ゲームのファイルかフォルダを選んでください。選んだ場所は次回以降も使われます。"),
        [Str.FirstRunStateNone] = ("STATE: no game selected", "状態:ゲーム未選択"),
        [Str.ButtonSelect] = ("Select game…", "ゲームを選ぶ…"),
        [Str.PlayOff] = ("PLAY is off: select the original game to continue.", "「はじめる」は使えません。続けるには原作のゲームを選んでください。"),
        [Str.ErrNotFound] = ("Game not found. The selected item does not look like a valid copy of the game. Try another file or folder.", "ゲームが見つかりません。選んだものは有効なゲームのコピーではないようです。別のファイルかフォルダを試してください。"),
        [Str.ErrDiskImage] = ("The disk image could not be read. Choose the .hdi file of the original game.", "ディスクイメージを読み込めません。原作ゲームの.hdiファイルを選んでください。"),
        [Str.ErrMissingFile] = ("A file the game needs is missing:", "ゲームに必要なファイルがありません:"),
        [Str.ErrDamaged] = ("A game file is damaged or cannot be read:", "ゲームのファイルが壊れているか、読み込めません:"),
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
