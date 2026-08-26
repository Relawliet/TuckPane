namespace TuckPane.Models;

using System.Text.Json.Serialization;

internal sealed class PortableNoteDocument
{
    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; set; } = "TuckPane.Note";

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonRequired]
    [JsonPropertyName("theme")]
    public NoteTheme Theme { get; set; } = NoteTheme.RainBlue;

    [JsonRequired]
    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 14;

    [JsonRequired]
    [JsonPropertyName("showRuledLines")]
    public bool ShowRuledLines { get; set; }

    [JsonRequired]
    [JsonPropertyName("placement")]
    public PortableNotePlacement? Placement { get; set; }

    [JsonRequired]
    [JsonPropertyName("html")]
    public string Html { get; set; } = string.Empty;
}

internal sealed class PortableNotePlacement
{
    [JsonRequired]
    [JsonPropertyName("monitorDevice")]
    public string MonitorDevice { get; set; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("xDip")]
    public double XDip { get; set; }

    [JsonRequired]
    [JsonPropertyName("yDip")]
    public double YDip { get; set; }

    [JsonRequired]
    [JsonPropertyName("widthDip")]
    public double WidthDip { get; set; } = 360;

    [JsonRequired]
    [JsonPropertyName("heightDip")]
    public double HeightDip { get; set; } = 300;
}
