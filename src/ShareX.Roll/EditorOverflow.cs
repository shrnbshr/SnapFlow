// GPL-3.0-or-later. Keep WinForms menu commands, hosted fields and keyboard handling; change presentation only.
using System.Runtime.CompilerServices;
using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;

namespace ShareX.Roll;

internal static class EditorOverflow
{
    private sealed record ItemStyle(ToolStripItemDisplayStyle Display, Size Size, Padding Padding,
        Padding Margin, ContentAlignment ImageAlign, ContentAlignment TextAlign, TextImageRelation Relation,
        string? Text, ToolStripDropDownDirection Direction);

    private static readonly ConditionalWeakTable<ToolStrip, Presentation> installed = new();

    private sealed class EditorPopup : ToolStripDropDownMenu
    {
        internal int ContentWidth { get; set; }
        protected override Padding DefaultPadding => new((int)Math.Round(6 * DeviceDpi / 96f));
        protected override Size MaxItemSize
        {
            get
            {
                Size native = base.MaxItemSize;
                if (ContentWidth <= 0) return native;
                // Native menu sizing includes hidden items. Keep its menu behavior and trim calculation,
                // but size this editor popup from the visible rows measured below.
                Padding nativePadding = base.DefaultPadding;
                int trim = RightToLeft == RightToLeft.Yes ? nativePadding.Right : nativePadding.Left;
                return new(ContentWidth + trim, native.Height);
            }
        }
    }

    internal static void Install(ToolStrip strip, string label, CapturePalette palette, bool transparency) =>
        installed.GetValue(strip, value => new Presentation(value, palette, transparency)).Apply(label);

    internal static ToolStripDropDown CreateChoices(Control owner, ComboBox source, CapturePalette palette, bool transparency,
        int minimumWidth = 0)
    {
        int S(int value) => (int)Math.Round(value * owner.DeviceDpi / 96f);
        EditorPopup popup = new()
        {
            Name = "editorOptionChoiceMenu", Font = owner.Font, Padding = new Padding(S(6)),
            DropShadowEnabled = false, RightToLeft = owner.RightToLeft,
            MaximumSize = new Size(Screen.FromControl(owner).WorkingArea.Width - S(24), S(360))
        };
        // Keep the field dropdown in its existing menu chain rather than replacing the parent menu.
        for (Control? ancestor = owner.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is ToolStripDropDown parent)
            {
                popup.OwnerItem = parent.Items.OfType<ToolStripControlHost>()
                    .FirstOrDefault(host => host.Control == owner || host.Control.Contains(owner));
                break;
            }
        int width = Math.Max(Math.Max(owner.Width, minimumWidth) - popup.Padding.Horizontal, Enumerable.Range(0, source.Items.Count)
            .Select(index => TextRenderer.MeasureText(source.GetItemText(source.Items[index]), owner.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + S(66)).DefaultIfEmpty(S(128)).Max());
        popup.ContentWidth = width;
        for (int index = 0; index < source.Items.Count; index++)
        {
            int captured = index;
            ToolStripMenuItem item = new(source.GetItemText(source.Items[index]))
            {
                Checked = source.SelectedIndex == index, AutoSize = false, Size = new(width, S(36)),
                Margin = new Padding(S(6), S(1), S(6), S(1)), Padding = new Padding(S(13), 0, S(13), 0)
            };
            item.Click += (_, _) => { source.SelectedIndex = captured; owner.Focus(); };
            popup.Items.Add(item);
        }
        popup.Renderer = new EditorToolStripRenderer(popup, palette);
        popup.SizeChanged += (_, _) =>
        {
            if (popup.IsDisposed || popup.Width <= 0 || popup.Height <= 0) return;
            using var outline = CaptureDrawing.Rounded(popup.ClientRectangle, S(10));
            Region? previous = popup.Region;
            popup.Region = new Region(outline);
            previous?.Dispose();
        };
        _ = new EditorMenuBackdrop(popup, palette, transparency);
        return popup;
    }

    private static int Group(ToolStripItem item) => item.Tag is ShapeType type ? type switch
    {
        ShapeType.DrawingSmartEraser or ShapeType.EffectBlur or ShapeType.EffectPixelate or
        ShapeType.EffectHighlight or ShapeType.ToolSpotlight => 1,
        _ => 0
    } : 2;

    private static string Label(ToolStripItem item)
    {
        string text = item.Text ?? string.Empty;
        if (item.Tag is ShapeType)
        {
            int prefix = text.IndexOfAny([':', '：']);
            if (prefix >= 0) return text[(prefix + 1)..].TrimStart();
        }
        return text;
    }

    private static void NameEditCommands(ToolStripDropDown menu)
    {
        if (menu.OwnerItem?.Name != "editorEditMenu") return;
        string[] names = ["editorUndo", "editorRedo", "editorPaste", "editorDuplicate", "editorDelete",
            "editorDeleteAll", "editorMoveTop", "editorMoveUp", "editorMoveDown", "editorMoveBottom"];
        ToolStripMenuItem[] commands = menu.Items.OfType<ToolStripMenuItem>().ToArray();
        for (int i = 0; i < commands.Length; i++)
        {
            ToolStripMenuItem item = commands[i];
            if (!string.IsNullOrEmpty(item.Name)) continue;
            string shortcut = (item.ShortcutKeyDisplayString ?? string.Empty).Replace(" ", "").ToUpperInvariant();
            string? name = shortcut switch
            {
                "CTRL+Z" => "editorUndo", "CTRL+Y" => "editorRedo", "CTRL+V" => "editorPaste",
                "CTRL+D" => "editorDuplicate", "DEL" => "editorDelete", "SHIFT+DEL" => "editorDeleteAll",
                "HOME" => "editorMoveTop", "PAGEUP" => "editorMoveUp", "PAGEDOWN" => "editorMoveDown", "END" => "editorMoveBottom",
                _ => commands.Length == names.Length ? names[i] : null
            };
            if (name != null) item.Name = name;
        }
    }

    private sealed class Presentation
    {
        private readonly ToolStrip strip;
        private readonly ToolStripDropDown overflow;
        private readonly CapturePalette palette;
        private readonly bool transparency;
        private readonly Dictionary<ToolStripItem, ItemStyle> original = new();
        private readonly HashSet<ToolStripDropDown> menus = new();
        private readonly HashSet<EditorPopup> ownedMenus = new();

        internal Presentation(ToolStrip strip, CapturePalette palette, bool transparency)
        {
            this.strip = strip;
            this.palette = palette;
            this.transparency = transparency;
            overflow = strip.OverflowButton.DropDown;
            Watch(overflow);
            strip.ItemAdded += (_, e) => Watch(e.Item);
            strip.SizeChanged += (_, _) => { if (overflow.Visible) overflow.Close(); };
            strip.Disposed += (_, _) =>
            {
                Restore();
                // Explicit DropDown assignments are not auto-generated menus: the strip does not own
                // their disposal. Release the popup and its native material/system-event subscription.
                foreach (EditorPopup menu in ownedMenus.ToArray()) menu.Dispose();
                ownedMenus.Clear();
            };
        }

        private int S(int value) => (int)Math.Round(value * strip.DeviceDpi / 96f);

        internal void Apply(string label)
        {
            ToolStripOverflowButton button = strip.OverflowButton;
            button.AutoSize = false;
            // Native overflow layout adds a 16px arrow slot to horizontal padding.
            button.Padding = new Padding(S(16), 0, S(16), 0);
            button.Margin = Padding.Empty;
            button.Size = new(S(48), S(48));
            button.ToolTipText = button.AccessibleName = label;
            overflow.AccessibleName = label;
            FlowLayoutSettings flow = (FlowLayoutSettings)overflow.LayoutSettings!;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            foreach (ToolStripItem item in strip.Items) Watch(item);
            foreach (ToolStripDropDown menu in menus.ToArray()) StyleMenu(menu);
        }

        private void Watch(ToolStripItem? item)
        {
            if (item is not ToolStripDropDownItem dropdown) return;
            if (dropdown.DropDown is ToolStripDropDownMenu originalMenu and not EditorPopup)
            {
                ToolStripItem[] items = originalMenu.Items.Cast<ToolStripItem>().ToArray();
                EditorPopup popup = new();
                ownedMenus.Add(popup);
                popup.Items.AddRange(items); // Move original commands and hosted controls, retaining their callbacks.
                dropdown.DropDown = popup;
                originalMenu.Dispose();
            }
            Watch(dropdown.DropDown);
        }

        private void Watch(ToolStripDropDown menu)
        {
            if (!menus.Add(menu)) return;
            menu.Opening += (_, _) =>
            {
                StyleMenu(menu);
                if (ReferenceEquals(menu, overflow)) ExpandOverflow();
                else StyleRows(menu);
            };
            menu.ItemAdded += (_, e) => Watch(e.Item);
            menu.SizeChanged += (_, _) => Round(menu);
            menu.Closed += (_, _) => { if (ReferenceEquals(menu, overflow)) Restore(); };
            menu.Disposed += (_, _) =>
            {
                menus.Remove(menu);
                if (ReferenceEquals(menu, overflow)) Restore();
            };
            NameEditCommands(menu);
            foreach (ToolStripItem item in menu.Items) Watch(item);
            StyleMenu(menu);
            _ = new EditorMenuBackdrop(menu, palette, transparency);
        }

        private void StyleMenu(ToolStripDropDown menu)
        {
            if (menu.IsDisposed) return;
            menu.Font = strip.Font;
            menu.Renderer = strip.Renderer;
            menu.ImageScalingSize = strip.ImageScalingSize;
            menu.Padding = new Padding(S(6));
            // The editor owns a soft shadow outside the rounded native menu region.
            menu.DropShadowEnabled = false;
            NameEditCommands(menu);
            Round(menu);
        }

        private void Round(ToolStripDropDown menu)
        {
            if (menu.IsDisposed || menu.Width <= 0 || menu.Height <= 0) return;
            using var outline = CaptureDrawing.Rounded(new RectangleF(0, 0, menu.Width, menu.Height), S(10));
            Region? previous = menu.Region;
            menu.Region = new Region(outline);
            previous?.Dispose();
        }

        private int MenuWidth(ToolStripDropDown menu, IEnumerable<ToolStripItem> items)
        {
            int width = S(128);
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripControlHost host)
                {
                    width = Math.Max(width, host.Width + S(26));
                    continue;
                }
                int text = TextRenderer.MeasureText(Label(item), menu.Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                int shortcut = EditorMenuLayout.ShortcutWidth(item, menu.Font, strip.DeviceDpi);
                int accessory = EditorMenuLayout.AccessoryWidth(item, strip.DeviceDpi);
                width = Math.Max(width, text + S(2 + 12 + 20 + 8 + 12) + accessory + (shortcut > 0 ? shortcut + S(24) : 0));
            }
            return Math.Min(width, Math.Max(S(80), Screen.FromControl(strip).WorkingArea.Width - S(24)));
        }

        private void StyleRows(ToolStripDropDown menu)
        {
            NameEditCommands(menu);
            ToolStripItem[] items = menu.Items.Cast<ToolStripItem>().Where(item => item.Available).ToArray();
            foreach (ToolStripControlHost host in items.OfType<ToolStripControlHost>())
            {
                host.Font = menu.Font;
                EditorOptionFields.Apply(host, palette, transparency, strip.DeviceDpi);
            }
            int width = MenuWidth(menu, items);
            menu.SuspendLayout();
            try
            {
                if (menu is EditorPopup popup) popup.ContentWidth = width;
                foreach (ToolStripItem item in items)
                {
                    if (item is ToolStripSeparator)
                    {
                        item.AutoSize = false;
                        item.Size = new(width, S(4));
                        item.Margin = item.Padding = Padding.Empty;
                        continue;
                    }
                    if (item is ToolStripControlHost host)
                    {
                        // Keep the original input bindings inside the padded editor-only row.
                        item.AutoSize = false;
                        item.Font = menu.Font;
                        EditorOptionFields.ArrangeWidth(host, width - S(26));
                        item.Margin = new Padding(menu.Padding.Left + S(13), S(2), menu.Padding.Right + S(13), S(2));
                        continue;
                    }
                    item.AutoSize = false;
                    item.Size = new(width, Math.Max(S(36), menu.Font.Height + S(8)));
                    item.Padding = new Padding(S(13), 0, S(13), 0);
                    // Native menu layout applies row margins without the menu's horizontal padding.
                    // Include that inset so highlights and their content have equal space on both sides.
                    item.Margin = new Padding(menu.Padding.Left, S(1), menu.Padding.Right, S(1));
                    item.ImageAlign = item.TextAlign = ContentAlignment.MiddleLeft;
                    item.TextImageRelation = TextImageRelation.ImageBeforeText;
                    if (item is ToolStripDropDownItem dropdown)
                        dropdown.DropDownDirection = strip.RightToLeft == RightToLeft.Yes ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right;
                }
            }
            finally { menu.ResumeLayout(true); }
        }

        private void ExpandOverflow()
        {
            Restore();
            ToolStripItem[] items = strip.Items.Cast<ToolStripItem>()
                .Where(item => item.Available && item.Placement == ToolStripItemPlacement.Overflow).ToArray();
            if (items.Length == 0) return;
            // Wider menu labels must not change which buttons fit in the parent toolbar.
            strip.SuspendLayout();
            int width = MenuWidth(overflow, items);
            int height = Math.Max(S(36), overflow.Font.Height + S(8));
            int previousGroup = -1;
            foreach (ToolStripItem item in items)
            {
                original[item] = new(item.DisplayStyle, item.Size, item.Padding, item.Margin,
                    item.ImageAlign, item.TextAlign, item.TextImageRelation, item.Text,
                    item is ToolStripDropDownItem dropdown ? dropdown.DropDownDirection : ToolStripDropDownDirection.Default);
                item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                item.Size = new(width, height);
                item.Padding = new Padding(S(13), 0, S(13), 0);
                int group = Group(item);
                item.Margin = new Padding(0, S(previousGroup >= 0 && previousGroup != group ? 5 : 1), 0, S(1));
                previousGroup = group;
                item.ImageAlign = item.TextAlign = ContentAlignment.MiddleLeft;
                item.TextImageRelation = TextImageRelation.ImageBeforeText;
                item.Text = Label(item);
                if (item is ToolStripDropDownItem submenu)
                    submenu.DropDownDirection = strip.RightToLeft == RightToLeft.Yes ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right;
            }
            overflow.PerformLayout();
        }

        private void Restore()
        {
            if (original.Count == 0) return;
            foreach ((ToolStripItem item, ItemStyle style) in original)
            {
                if (item.IsDisposed) continue;
                item.DisplayStyle = style.Display;
                item.Size = style.Size;
                item.Padding = style.Padding;
                item.Margin = style.Margin;
                item.ImageAlign = style.ImageAlign;
                item.TextAlign = style.TextAlign;
                item.TextImageRelation = style.Relation;
                item.Text = style.Text;
                if (item is ToolStripDropDownItem dropdown) dropdown.DropDownDirection = style.Direction;
            }
            original.Clear();
            if (!strip.IsDisposed) strip.ResumeLayout(true);
        }
    }
}

// One content grid for every editor popup; coordinates are local to the original native menu item.
internal static class EditorMenuLayout
{
    private static int S(int value, int dpi) => (int)Math.Round(value * dpi / 96f);
    internal static string ShortcutText(ToolStripItem item) => item is ToolStripMenuItem { ShowShortcutKeys: true } command
        ? !string.IsNullOrEmpty(command.ShortcutKeyDisplayString) ? command.ShortcutKeyDisplayString :
            command.ShortcutKeys == Keys.None ? string.Empty : new KeysConverter().ConvertToString(command.ShortcutKeys) ?? string.Empty
        : string.Empty;
    internal static int ShortcutWidth(ToolStripItem item, Font font, int dpi)
    {
        string text = ShortcutText(item);
        return text.Length == 0 ? 0 : TextRenderer.MeasureText(text, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
    }
    internal static int AccessoryWidth(ToolStripItem item, int dpi) =>
        item is ToolStripDropDownItem { HasDropDownItems: true } || item is ToolStripMenuItem { Tag: Color } ? S(20, dpi) : 0;
    private static Rectangle Mirror(ToolStripItem item, Rectangle rect) => item.RightToLeft == RightToLeft.Yes
        ? new Rectangle(item.Width - rect.Right, rect.Y, rect.Width, rect.Height) : rect;
    internal static Rectangle IconBounds(ToolStripItem item, int dpi)
    {
        int size = S(20, dpi);
        return Mirror(item, new Rectangle(S(13, dpi), (item.Height - size) / 2, size, size));
    }
    internal static Rectangle AccessoryBounds(ToolStripItem item, int dpi)
    {
        int size = S(12, dpi);
        return Mirror(item, new Rectangle(item.Width - S(13, dpi) - size, (item.Height - size) / 2, size, size));
    }
    internal static Rectangle TextBounds(ToolStripItem item, Font font, int dpi, bool shortcut)
    {
        int end = item.Width - S(13, dpi) - AccessoryWidth(item, dpi);
        int width = ShortcutWidth(item, font, dpi);
        int start = shortcut ? end - width : S(13 + 20 + 8, dpi);
        if (!shortcut && width > 0) end -= width + S(24, dpi);
        return Mirror(item, new Rectangle(start, 0, Math.Max(0, end - start), item.Height));
    }
}
