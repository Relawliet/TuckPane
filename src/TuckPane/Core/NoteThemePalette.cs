namespace TuckPane.Core;

using Microsoft.UI;
using TuckPane.Models;
using Windows.UI;

internal sealed record NoteThemeColors(
    NoteTheme Theme,
    string NameKey,
    string Surface,
    string Editor,
    string Accent,
    string Border,
    string Text,
    string Muted)
{
    internal IReadOnlyDictionary<string, string> Css => new Dictionary<string, string>
    {
        ["surface"] = Surface,
        ["editor"] = Editor,
        ["accent"] = Accent,
        ["border"] = Border,
        ["text"] = Text,
        ["muted"] = Muted
    };

    internal Color SurfaceColor => Parse(Surface);
    internal Color EditorColor => Parse(Editor);
    internal Color AccentColor => Parse(Accent);
    internal Color BorderColor => Parse(Border);
    internal Color TextColor => Parse(Text);
    internal Color MutedColor => Parse(Muted);

    private static Color Parse(string value) => ColorHelper.FromArgb(
        255,
        Convert.ToByte(value.Substring(1, 2), 16),
        Convert.ToByte(value.Substring(3, 2), 16),
        Convert.ToByte(value.Substring(5, 2), 16));
}

internal static class NoteThemePalette
{
    internal static IReadOnlyList<NoteThemeColors> All { get; } =
    [
        new(NoteTheme.RainBlue, "NoteThemeRainBlue", "#34414A", "#43515B", "#9BB6C5", "#5F707A", "#F1F4F5", "#BAC3C8"),
        new(NoteTheme.Graphite, "NoteThemeGraphite", "#3A3C3E", "#494C4F", "#C0A778", "#64686B", "#F3F0EA", "#BDB8AF"),
        new(NoteTheme.SunYellow, "NoteThemeSunYellow", "#BDB48A", "#D9D0AA", "#E9D27C", "#9D936A", "#342F20", "#6F684C"),
        new(NoteTheme.InkBlack, "NoteThemeInkBlack", "#101315", "#1B1F22", "#A9B2B8", "#343A3F", "#F1F3F4", "#A4ABB0"),
        new(NoteTheme.TransparentGlass, "NoteThemeTransparentGlass", "#2E3943", "#53636E", "#AFC5D0", "#788993", "#FFFFFF", "#D2DBE0"),
        new(NoteTheme.CloudPaper, "NoteThemeCloudPaper", "#AEB8BD", "#CBD2D5", "#58798B", "#939FA5", "#273138", "#606D73"),
        new(NoteTheme.WheatPaper, "NoteThemeWheatPaper", "#BBAE9C", "#D6C9B7", "#7D624F", "#A2927E", "#302820", "#685C52")
    ];

    internal static NoteThemeColors Get(NoteTheme theme) =>
        All.FirstOrDefault(colors => colors.Theme == theme) ?? All[0];
}
