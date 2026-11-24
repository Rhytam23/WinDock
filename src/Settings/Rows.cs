// WinDock - the building blocks of a Settings page: sections, cards and rows (SPEC 11.1).
using System;
using System.Collections.Generic;
using System.Drawing;

namespace WinDock
{
    public class SettingsPage
    {
        public string Id, Title, Glyph, Group;
        public Color Color;
        public string OpensUri;                  // SYSTEM entries that just open Windows Settings
        public Func<List<Section>> Build;        // rebuilt each time the page is shown
        public List<Section> Sections = new List<Section>();
    }

    public class Section
    {
        public string Heading, Footnote;
        public List<Row> Rows = new List<Row>();
        public Section(string heading) { Heading = heading; }
        public Section Add(Row r) { Rows.Add(r); return this; }
        public Section Note(string text) { Footnote = text; return this; }
    }

    public abstract class Row
    {
        public string Label;
        public float Height = 40;               // logical px
        public RectangleF Bounds;               // last drawn, in content coordinates (physical px)
        public float Flash;                     // search hit highlight
        public virtual bool Interactive { get { return true; } }
    }

    public class ToggleRow : Row
    {
        public Func<bool> Get; public Action<bool> Set;
        public float Anim = -1;
        public ToggleRow(string label, Func<bool> get, Action<bool> set) { Label = label; Get = get; Set = set; }
    }

    public class SliderRow : Row
    {
        public int Min, Max; public Func<int> Get; public Action<int> Set;
        public string MinLabel, MaxLabel;
        public RectangleF Track;
        public SliderRow(string label, int min, int max, Func<int> get, Action<int> set, string minLabel, string maxLabel)
        {
            Label = label; Min = min; Max = max; Get = get; Set = set; MinLabel = minLabel; MaxLabel = maxLabel;
            if (minLabel != null) Height = 52;
        }
    }

    public class PickerRow : Row
    {
        public string[] Values, Labels; public Func<string> Get; public Action<string> Set;
        public PickerRow(string label, string[] values, string[] labels, Func<string> get, Action<string> set) { Label = label; Values = values; Labels = labels; Get = get; Set = set; }
        public string CurrentLabel
        {
            get
            {
                string v = Get();
                int i = Array.IndexOf(Values, v);
                return i >= 0 ? Labels[i] : v;
            }
        }
    }

    public class LinkRow : Row
    {
        public Action Do; public Func<string> Value;
        public LinkRow(string label, Action action) { Label = label; Do = action; }
        public LinkRow(string label, Func<string> value, Action action) { Label = label; Do = action; Value = value; }
    }

    public class InfoRow : Row
    {
        public Func<string> Value;
        public InfoRow(string label, Func<string> value) { Label = label; Value = value; }
        public override bool Interactive { get { return false; } }
    }

    public class CustomRow : Row
    {
        public Action<Graphics, RectangleF, Point?> Draw;
        public Action<Point, RectangleF> MouseMove;
        public CustomRow(float height, Action<Graphics, RectangleF, Point?> draw) { Height = height; Draw = draw; Label = ""; }
        public override bool Interactive { get { return false; } }
    }
}
