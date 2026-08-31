namespace TuckPane.Core;

using System.Net;
using TuckPane.Models;

internal static class OrganizerNoteRules
{
    internal const double MinimumFontSize = 8;
    internal const double MaximumFontSize = 48;

    internal static string CreateDefaultName(IEnumerable<string> names)
    {
        var used = names.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (int id = 1; ; id++)
        {
            string candidate = $"便签 {id}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    internal static bool IsNameAvailable(
        IEnumerable<string> names,
        string candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        !names.Any(name => name.Equals(candidate.Trim(), StringComparison.CurrentCultureIgnoreCase));

    internal static string ItemKey(Guid noteId) => $"note:{noteId:N}";

    internal static string PlainTextToHtml(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>", StringComparison.Ordinal);

    internal static WidgetItem CreateItem(NoteDefinition note) =>
        new(note.Name, string.Empty, ItemKey(note.Id), WidgetItemKind.Note, note.Id);
}
