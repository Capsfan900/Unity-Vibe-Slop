using System;
using System.Collections.Generic;
using System.Linq;

namespace VibeGame1.EditorTools
{
    /// <summary>Pure durable-key selection state. Scene tools consume its one-shot focus intent later.</summary>
    public sealed class LevelStudioSelection
    {
        readonly List<string> selected = new List<string>();
        public IReadOnlyList<string> selectedKeys { get { return selected; } }
        public string activeKey { get; private set; }
        string focusKey;

        public void Select(string key, bool additive, bool range, IEnumerable<string> orderedKeys)
        {
            if (string.IsNullOrEmpty(key)) return;
            var order = (orderedKeys ?? new string[0]).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (range && !string.IsNullOrEmpty(activeKey))
            {
                int first = order.IndexOf(activeKey), last = order.IndexOf(key);
                if (first >= 0 && last >= 0)
                {
                    if (!additive) selected.Clear();
                    for (int i = Math.Min(first, last); i <= Math.Max(first, last); i++) if (!selected.Contains(order[i])) selected.Add(order[i]);
                    activeKey = key;
                    return;
                }
            }
            if (!additive) selected.Clear();
            if (!selected.Contains(key)) selected.Add(key);
            activeKey = key;
        }

        public void Toggle(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!selected.Remove(key)) { selected.Add(key); activeKey = key; }
            else if (activeKey == key) activeKey = selected.Count > 0 ? selected[selected.Count - 1] : null;
        }

        public void Clear() { selected.Clear(); activeKey = null; focusKey = null; }

        public void Reconcile(IEnumerable<string> visibleKeys)
        {
            var visible = new HashSet<string>(visibleKeys ?? new string[0], StringComparer.Ordinal);
            selected.RemoveAll(key => !visible.Contains(key));
            if (!visible.Contains(activeKey)) activeKey = selected.Count > 0 ? selected[selected.Count - 1] : null;
            if (!visible.Contains(focusKey)) focusKey = null;
        }

        public void RequestFocus() { focusKey = activeKey; }
        public string ConsumeFocusIntent() { string value = focusKey; focusKey = null; return value; }
    }
}
