namespace SpeechApp;

internal static class Ui
{
    public static readonly Color Background = ColorTranslator.FromHtml("#F7F0EA");
    public static readonly Color Text = ColorTranslator.FromHtml("#39283B");
    public static readonly Color Surface = ColorTranslator.FromHtml("#FFFBF7");
    public static readonly Color Accent = ColorTranslator.FromHtml("#783B65");
    public static readonly Color OnAccent = ColorTranslator.FromHtml("#FFFFFF");
    public static readonly Color Muted = ColorTranslator.FromHtml("#674D62");
    public static readonly Color Border = ColorTranslator.FromHtml("#E7D6DF");
    public static readonly Color Tint = ColorTranslator.FromHtml("#F1DCE9");

    public static void Window(Form form, string title, int width, int height, int minWidth, int minHeight)
    {
        form.Text = title;
        using (var resource = typeof(Ui).Assembly.GetManifestResourceStream("WindowIcon.ico"))
        {
            if (resource is not null)
            {
                var icon = new Icon(resource);
                form.Icon = icon;
                form.Disposed += (_, _) => icon.Dispose();
            }
        }
        form.Font = new("Segoe UI", 10f);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new(width, height);
        form.MinimumSize = new(minWidth, minHeight);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Background;
        form.ForeColor = Text;
    }

    public static Label Label(string value, float size = 10, bool bold = false, Color? ink = null) => new()
    {
        Text = value, AutoSize = true, Font = new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = ink ?? Text, Margin = new(0, 0, 0, 3), UseMnemonic = false
    };

    public static Button Button(string value, bool primary = false)
    {
        var button = new Button
        {
            Text = value, Height = 36, AutoSize = true, MinimumSize = new(68, 34),
            Padding = new(6, 0, 6, 0), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Surface, ForeColor = primary ? OnAccent : Text,
            Margin = new(0, 2, 8, 5), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        return button;
    }

    public static FlowLayoutPanel Bar(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = true,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        flow.Controls.AddRange(controls);
        return flow;
    }

    public static TableLayoutPanel Stack(int padding, params (Control Control, float Percent)[] rows)
    {
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1, Dock = DockStyle.Fill, Padding = new(padding), Margin = Padding.Empty,
            RowCount = rows.Length
        };
        stack.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int row = 0; row < rows.Length; row++)
        {
            var (control, percent) = rows[row];
            stack.RowStyles.Add(percent == 0 ? new(SizeType.AutoSize) : new(SizeType.Percent, percent));
            stack.Controls.Add(control, 0, row);
        }
        return stack;
    }

    public static void Input(Control control, string name)
    {
        control.AccessibleName = name;
        control.BackColor = Surface;
        control.ForeColor = Text;
    }
}
