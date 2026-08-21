using System.Collections.ObjectModel;
using TuckPane.Models;

namespace TuckPane.Services;

internal static class CatalogCollectionSync
{
    internal static void Apply(ObservableCollection<WidgetItem> items, IReadOnlyList<WidgetItem> desired)
    {
        for (int slot = 0; slot < desired.Count; slot++)
        {
            WidgetItem next = desired[slot];
            if (slot < items.Count &&
                items[slot].RelativeName.Equals(next.RelativeName, StringComparison.OrdinalIgnoreCase))
            {
                if (!items[slot].HasSameValue(next)) items[slot] = next;
                continue;
            }

            int existing = -1;
            for (int index = slot + 1; index < items.Count; index++)
            {
                if (items[index].RelativeName.Equals(next.RelativeName, StringComparison.OrdinalIgnoreCase))
                {
                    existing = index;
                    break;
                }
            }

            if (existing >= 0)
            {
                items.Move(existing, slot);
                if (!items[slot].HasSameValue(next)) items[slot] = next;
            }
            else
            {
                items.Insert(slot, next);
            }
        }

        while (items.Count > desired.Count) items.RemoveAt(items.Count - 1);
    }

    internal static bool ApplyReorderInPlace(ObservableCollection<WidgetItem> slots, IReadOnlyList<WidgetItem> desired)
    {
        if (slots.Count != desired.Count) throw new ArgumentException("A reorder must preserve the item count.", nameof(desired));
        WidgetItem[] values = desired.Select(item => item.CopyValue()).ToArray();
        bool changed = false;
        for (int slot = 0; slot < slots.Count; slot++)
        {
            if (slots[slot].HasSameValue(values[slot])) continue;
            slots[slot].ApplyValue(values[slot]);
            changed = true;
        }
        return changed;
    }
}
