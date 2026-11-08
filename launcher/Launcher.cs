// Hookshot Launcher (AMD fix, sharkiix version). The window players open first: update check (the same GitHub release and graphics
// patch rules as host/hos_update.c), the game's settings (hookshot_options.ini, the Insert menu's file), the AMD fix switches
// (hookshot_env.txt, read by the game at start), the saves folders, and PLAY. It sits beside hookshot.exe.
// Built with the .NET Framework's own csc (C# 5, no install needed): launcher\build.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Hookshot Launcher")]
[assembly: System.Reflection.AssemblyProduct("Hookshot - AMD fix (sharkiix version)")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

static class Fork {
    public const string Base = "amd-fix-2.0";      // (host/hos_update.c HOOKSHOT_FORK_BASE: the exe this launcher ships with)
    public const string Version = "amd-fix-2.0";   // (HOOKSHOT_FORK_VERSION)
    public const int Api = 4;                      // (gpu/vk/vk_api.h MKWVK_API_VERSION)
    public const string Api_Url = "https://api.github.com/repos/Shakoquo/Hookshot/releases/latest";
    public const string Page = "https://github.com/Shakoquo/Hookshot/releases/latest";
}

static class Theme {
    public static readonly Color Bg = Color.FromArgb(17, 14, 12);
    public static readonly Color Panel = Color.FromArgb(28, 23, 19);
    public static readonly Color Field = Color.FromArgb(38, 31, 26);
    public static readonly Color Line = Color.FromArgb(70, 56, 38);
    public static readonly Color Text = Color.FromArgb(236, 228, 214);
    public static readonly Color Dim = Color.FromArgb(150, 138, 120);
    public static readonly Color Gold = Color.FromArgb(232, 184, 76);
    public static readonly Color GoldDark = Color.FromArgb(150, 100, 30);
    public static readonly Color Red = Color.FromArgb(200, 30, 45);
    public static readonly Color Green = Color.FromArgb(110, 200, 120);

    public static GraphicsPath Round(RectangleF r, float rad) {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    public static Font Pick(float size, FontStyle style, params string[] names) {
        using (var fonts = new InstalledFontCollection()) {
            foreach (var n in names)
                foreach (var f in fonts.Families)
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return new Font(n, size, style);
        }
        return new Font("Segoe UI", size, style);
    }
}

// (a flat button drawn by hand: rounded, gold or dark)
class FlatButton : Control {
    public bool Primary;
    bool hover, down;
    public FlatButton() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 10f);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Round(r, Math.Min(10, Height / 2f - 1))) {
            if (Primary) {
                Color a = Enabled ? (down ? Theme.GoldDark : hover ? Color.FromArgb(250, 210, 110) : Theme.Gold) : Color.FromArgb(90, 80, 60);
                Color b = Enabled ? (down ? Color.FromArgb(120, 80, 20) : Theme.GoldDark) : Color.FromArgb(60, 52, 40);
                using (var br = new LinearGradientBrush(r, a, b, LinearGradientMode.Vertical)) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(255, 235, 170), 1f)) g.DrawPath(pen, path);
            } else {
                using (var br = new SolidBrush(down ? Theme.Line : hover ? Color.FromArgb(52, 42, 34) : Theme.Field)) g.FillPath(br, path);
                using (var pen = new Pen(hover ? Theme.Gold : Theme.Line, 1f)) g.DrawPath(pen, path);
            }
        }
        Color fg = Primary ? Color.FromArgb(40, 24, 6) : (Enabled ? Theme.Text : Theme.Dim);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                              TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// (a drop-down list in the dark theme)
class DarkCombo : ComboBox {
    public DarkCombo() {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.75f);
        ItemHeight = 22;
    }
    protected override void OnDrawItem(DrawItemEventArgs e) {
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(sel ? Theme.GoldDark : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                                  Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
    }
}

// (the signature: the picture in a gold frame, "sharkiix version" under it)
class Signature : Control {
    readonly Image pic;
    public Signature(Image pic) {
        this.pic = pic;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Theme.Panel);
        int side = Width - 40;
        var frame = new RectangleF(20, 24, side, side);
        // (a soft glow behind the frame)
        for (int i = 10; i > 0; i--)
            using (var p = Theme.Round(RectangleF.Inflate(frame, i * 1.6f, i * 1.6f), 16 + i))
            using (var b = new SolidBrush(Color.FromArgb(7, Theme.Gold))) g.FillPath(b, p);
        using (var clip = Theme.Round(frame, 14)) {
            if (pic != null) {
                // (the middle square of the picture)
                int s = Math.Min(pic.Width, pic.Height);
                var src = new Rectangle((pic.Width - s) / 2, (pic.Height - s) / 2, s, s);
                var state = g.Save();
                g.SetClip(clip);
                g.DrawImage(pic, Rectangle.Round(frame), src, GraphicsUnit.Pixel);
                g.Restore(state);
            }
            using (var pen = new Pen(Theme.Gold, 3f)) g.DrawPath(pen, clip);
        }
        // (the stylised writing)
        var textBox = new RectangleF(0, frame.Bottom + 14, Width, 70);
        using (var font = Theme.Pick(21f, FontStyle.Bold, "Segoe Script", "Gabriola", "Palace Script MT", "Georgia"))
        using (var path = new GraphicsPath())
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
            path.AddString("sharkiix version", font.FontFamily, (int)font.Style, g.DpiY * font.SizeInPoints / 72f, textBox, fmt);
            using (var shadow = (GraphicsPath)path.Clone()) {
                var m = new Matrix(); m.Translate(2, 3); shadow.Transform(m);
                using (var b = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillPath(b, shadow);
            }
            var bounds = path.GetBounds();
            using (var br = new LinearGradientBrush(bounds, Color.FromArgb(255, 240, 180), Theme.GoldDark, LinearGradientMode.Vertical)) {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Color.FromArgb(255, 245, 200), Theme.Gold, Color.FromArgb(170, 110, 30) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                br.InterpolationColors = blend;
                g.FillPath(br, path);
            }
            using (var pen = new Pen(Color.FromArgb(90, 50, 10), 1.2f)) g.DrawPath(pen, path);
        }
        using (var f = new Font("Segoe UI", 8.5f))
            TextRenderer.DrawText(g, "AMD fix  ·  " + Fork.Version.Replace("amd-fix-", "v"), f,
                                  new Rectangle(0, (int)textBox.Bottom - 2, Width, 20), Theme.Dim, TextFormatFlags.HorizontalCenter);
    }
}

class LauncherForm : Form {
    readonly string dir;
    readonly string optionsPath, envPath, langPath;
    readonly Dictionary<string, DarkCombo> combos = new Dictionary<string, DarkCombo>();
    readonly Dictionary<string, CheckBox> fixes = new Dictionary<string, CheckBox>();
    DarkCombo langCombo;
    Label updateText, updateDot;
    FlatButton updateButton, playButton;
    readonly Panel[] pages = new Panel[3];
    readonly FlatButton[] tabs = new FlatButton[3];
    bool loading;
    string patchDll, patchTag; long patchSize;

    // (key in hookshot_options.ini, label, choices shown, the value each choice writes)
    static readonly object[][] GraphicsRows = {
        new object[] { "quality_preset", "Quality preset", new[] { "Custom", "Minimum (4 GB)", "Low (6 GB)", "Balanced (8 GB)", "High (12 GB)", "Ultra (16 GB+)" }, new[] { 0, 1, 2, 3, 4, 5 } },
        new object[] { "resolution", "Resolution", new[] { "Auto", "100%", "75%", "67%", "50%" }, new[] { 0, 1, 2, 3, 4 } },
        new object[] { "upscaling", "Upscaling", new[] { "Off", "Catmull-Rom", "Lanczos" }, new[] { 0, 1, 2 } },
        new object[] { "sharpness", "Sharpness", new[] { "0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" }, new[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 } },
        new object[] { "frame_generation", "Frame generation", new[] { "Off", "2x", "3x", "4x", "5x", "6x", "7x", "8x" }, new[] { 0, 2, 3, 4, 5, 6, 7, 8 } },
        new object[] { "frame_pacing", "Frame pacing", new[] { "Off", "On" }, new[] { 0, 1 } },
        new object[] { "fullscreen", "Display", new[] { "Window", "Fullscreen" }, new[] { 0, 1 } },
        new object[] { "ultrawide", "Screen shape", new[] { "16:9", "Monitor's shape" }, new[] { 0, 1 } },
        new object[] { "handheld_profile", "Handheld profile", new[] { "Off", "On" }, new[] { 0, 1 } },
        new object[] { "fps_counter", "FPS counter", new[] { "Off", "On" }, new[] { 0, 1 } },
    };
    static readonly object[][] GameRows = {
        new object[] { "game_speed", "Game speed", new[] { "Per frame", "Real time" }, new[] { 0, 1 } },
        new object[] { "text_speed", "Text speed", new[] { "Normal", "2x", "4x", "Instant" }, new[] { 0, 1, 2, 3 } },
        new object[] { "controller_in_background", "Controller in background", new[] { "Off", "On" }, new[] { 0, 1 } },
    };
    static readonly string[] PresetKeys = { "resolution", "upscaling", "sharpness", "frame_generation", "frame_pacing", "game_speed", "handheld_profile" };
    static readonly string[][] Languages = {
        new[] { "en-US", "English (US)" }, new[] { "en-GB", "English (UK)" }, new[] { "fr", "Français (France)" }, new[] { "fr-CA", "Français (Canada)" },
        new[] { "de", "Deutsch" }, new[] { "it", "Italiano" }, new[] { "es", "Español (España)" }, new[] { "es-419", "Español (Latinoamérica)" },
        new[] { "pt-BR", "Português (Brasil)" }, new[] { "nl", "Nederlands" }, new[] { "pl", "Polski" }, new[] { "ru", "Русский" },
        new[] { "ja", "日本語" }, new[] { "ko", "한국어" }, new[] { "zh-Hans", "简体中文" }, new[] { "zh-Hant", "繁體中文" },
    };
    // (env name, value when on, label, help, value when off; "" = no line, the game's default)
    static readonly string[][] Fixes = {
        new[] { "RX_VK_WAIT_GPU", "", "No blue light flashes (recommended)", "Each frame waits for the graphics card to finish the previous one. Stops the one-frame blue light flashes (Hyrule Field). Costs a few fps. The game's default.", "-1" },
        new[] { "HOOKSHOT_OCCLUSION_OFF", "1", "Occlusion tests off", "The game no longer guesses what is hidden: everything is drawn. Steadier sun glare on AMD, a little slower.", "" },
    };

    public LauncherForm(string dir) {
        this.dir = dir;
        optionsPath = Path.Combine(dir, "hookshot_options.ini");
        envPath = Path.Combine(dir, "hookshot_env.txt");
        langPath = Path.Combine(dir, "language.txt");

        Text = "Hookshot - AMD fix (sharkiix version)";
        ClientSize = new Size(1000, 620);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.75f);
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Image pic = null;
        try {
            using (var s = typeof(LauncherForm).Assembly.GetManifestResourceStream("signature.png"))
                if (s != null) pic = Image.FromStream(s);
        } catch { }
        var sig = new Signature(pic) { Bounds = new Rectangle(0, 0, 320, 620) };
        Controls.Add(sig);

        int x0 = 350, w = 620;
        Controls.Add(new Label { Text = "HOOKSHOT", Font = Theme.Pick(30f, FontStyle.Bold, "Segoe UI Black", "Segoe UI"), ForeColor = Theme.Gold,
                                 AutoSize = true, Location = new Point(x0 - 4, 18), BackColor = Color.Transparent });
        Controls.Add(new Label { Text = "The Legend of Zelda: Ocarina of Time  ·  PC recompile  ·  AMD fix", ForeColor = Theme.Dim, AutoSize = true,
                                 Location = new Point(x0, 76), BackColor = Color.Transparent });

        // (the update card)
        var card = new Panel { Bounds = new Rectangle(x0, 108, w, 58), BackColor = Theme.Panel };
        card.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
        updateDot = new Label { Text = "●", ForeColor = Theme.Dim, Font = new Font("Segoe UI", 14f), AutoSize = true, Location = new Point(12, 13) };
        updateText = new Label { Text = "Checking for updates…", ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(40, 8, 400, 42),
                                 TextAlign = ContentAlignment.MiddleLeft };
        updateButton = new FlatButton { Text = "Check again", Bounds = new Rectangle(w - 172, 12, 160, 34) };
        updateButton.Click += OnUpdateButton;
        card.Controls.Add(updateDot); card.Controls.Add(updateText); card.Controls.Add(updateButton);
        Controls.Add(card);

        // (tabs and their pages)
        string[] names = { "Graphics", "Game", "AMD fixes" };
        for (int i = 0; i < 3; i++) {
            int k = i;
            tabs[i] = new FlatButton { Text = names[i], Bounds = new Rectangle(x0 + i * 128, 184, 120, 32) };
            tabs[i].Click += (s, e) => ShowPage(k);
            Controls.Add(tabs[i]);
            pages[i] = new Panel { Bounds = new Rectangle(x0, 224, w, 296), BackColor = Theme.Panel, Visible = i == 0 };
            var page = pages[i];
            page.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, page.Width - 1, page.Height - 1); };
            Controls.Add(pages[i]);
        }
        AddRows(pages[0], GraphicsRows);
        AddRows(pages[1], GameRows);
        langCombo = new DarkCombo { Bounds = new Rectangle(320, 16 + 54 + 20, 270, 26) };
        foreach (var l in Languages) langCombo.Items.Add(l[1]);
        pages[1].Controls.Add(new Label { Text = "Language", ForeColor = Theme.Dim, AutoSize = true, Location = new Point(320, 16 + 54) });
        pages[1].Controls.Add(langCombo);
        langCombo.SelectedIndexChanged += (s, e) => { if (!loading) SaveLanguage(); };
        pages[1].Controls.Add(new Label { Text = "Controls are set in game: press Insert for the menu.", ForeColor = Theme.Dim, AutoSize = true,
                                          Location = new Point(20, 260) });
        for (int i = 0; i < Fixes.Length; i++) {
            var cb = new CheckBox { Text = Fixes[i][2], ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 10.5f), AutoSize = true,
                                    Location = new Point(20, 18 + i * 76), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            cb.FlatAppearance.BorderColor = Theme.Gold;
            cb.FlatAppearance.CheckedBackColor = Theme.GoldDark;
            cb.CheckedChanged += (s, e) => { if (!loading) SaveEnv(); };
            fixes[Fixes[i][0]] = cb;
            pages[2].Controls.Add(cb);
            pages[2].Controls.Add(new Label { Text = Fixes[i][3], ForeColor = Theme.Dim, AutoSize = false, Bounds = new Rectangle(40, 44 + i * 76, w - 60, 40) });
        }
        pages[2].Controls.Add(new Label { Text = "These are read by the game when it starts (hookshot_env.txt).", ForeColor = Theme.Dim, AutoSize = true,
                                          Location = new Point(20, 260) });

        // (bottom row)
        var saves = new FlatButton { Text = "Saves folder", Bounds = new Rectangle(x0, 548, 132, 44) };
        saves.Click += (s, e) => OpenFolder(Path.Combine(dir, "user", "save"));
        var import = new FlatButton { Text = "Import saves", Bounds = new Rectangle(x0 + 140, 548, 132, 44) };
        import.Click += (s, e) => {
            OpenFolder(Path.Combine(dir, "SAVE FOLDER"));
            MessageBox.Show(this, "Put saves from another emulator here (the folder holding slot_00, slot_01… or a folder above it).\n" +
                            "They are copied into the game at the next start, and the saves you had are kept in a backup folder.",
                            "Import saves", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        var folder = new FlatButton { Text = "Game folder", Bounds = new Rectangle(x0 + 280, 548, 120, 44) };
        folder.Click += (s, e) => OpenFolder(dir);
        playButton = new FlatButton { Text = "PLAY", Primary = true, Font = new Font("Segoe UI Black", 15f), Bounds = new Rectangle(x0 + w - 200, 540, 200, 58) };
        playButton.Click += OnPlay;
        Controls.Add(saves); Controls.Add(import); Controls.Add(folder); Controls.Add(playButton);
        AcceptButton = null;

        LoadAll();
        int first;
        string[] cmd = Environment.GetCommandLineArgs();
        ShowPage(cmd.Length > 2 && int.TryParse(cmd[2], out first) && first >= 0 && first < 3 ? first : 0);
        CheckUpdate();
    }

    void AddRows(Panel page, object[][] rows) {
        for (int i = 0; i < rows.Length; i++) {
            var row = rows[i];
            int col = i % 2, line = i / 2;
            int x = 20 + col * 300, y = 16 + line * 54;
            page.Controls.Add(new Label { Text = (string)row[1], ForeColor = Theme.Dim, AutoSize = true, Location = new Point(x, y) });
            var c = new DarkCombo { Bounds = new Rectangle(x, y + 20, 270, 26), Tag = row };
            foreach (var t in (string[])row[2]) c.Items.Add(t);
            string key = (string)row[0];
            c.SelectedIndexChanged += (s, e) => OnSetting(key);
            combos[key] = c;
            page.Controls.Add(c);
        }
    }

    void ShowPage(int k) {
        for (int i = 0; i < 3; i++) { pages[i].Visible = i == k; tabs[i].Primary = i == k; tabs[i].Invalidate(); }
    }

    // ---- settings ----
    Dictionary<string, int> ReadOptions() {
        var d = new Dictionary<string, int>();
        if (!File.Exists(optionsPath)) return d;
        foreach (var raw in File.ReadAllLines(optionsPath)) {
            int eq = raw.IndexOf('=');
            int v;
            if (eq > 0 && int.TryParse(raw.Substring(eq + 1).Trim(), out v)) d[raw.Substring(0, eq).Trim()] = v;
        }
        return d;
    }

    void SetCombo(string key, int value) {
        DarkCombo c;
        if (!combos.TryGetValue(key, out c)) return;
        var values = (int[])((object[])c.Tag)[3];
        if (key == "frame_generation" && value == 1) value = 2;
        int best = 0;
        for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
        c.SelectedIndex = best;
    }

    int ComboValue(string key) {
        var c = combos[key];
        var values = (int[])((object[])c.Tag)[3];
        return values[Math.Max(0, c.SelectedIndex)];
    }

    void LoadAll() {
        loading = true;
        var o = ReadOptions();
        // (the game's own defaults where the file has nothing yet: options.ini is written by the game's Insert menu)
        var defaults = new Dictionary<string, int> { { "quality_preset", 0 }, { "resolution", 0 }, { "upscaling", 2 }, { "sharpness", 30 },
            { "frame_generation", 2 }, { "frame_pacing", 1 }, { "fullscreen", 1 }, { "ultrawide", 0 }, { "handheld_profile", 0 },
            { "fps_counter", 0 }, { "game_speed", 1 }, { "text_speed", 0 }, { "controller_in_background", 0 } };
        foreach (var kv in defaults) SetCombo(kv.Key, o.ContainsKey(kv.Key) ? o[kv.Key] : kv.Value);

        var env = ReadEnv();
        foreach (var f in Fixes) {
            string v;
            fixes[f[0]].Checked = env.TryGetValue(f[0], out v) ? v == f[1] : f[1].Length == 0;
        }
        string lang = ReadLanguage();
        langCombo.SelectedIndex = 0;
        for (int i = 0; i < Languages.Length; i++) if (string.Equals(Languages[i][0], lang, StringComparison.OrdinalIgnoreCase)) langCombo.SelectedIndex = i;
        loading = false;
    }

    void OnSetting(string key) {
        if (loading) return;
        loading = true;
        if (key == "quality_preset") ApplyPreset(ComboValue("quality_preset"));
        else if (Array.IndexOf(PresetKeys, key) >= 0) SetCombo("quality_preset", 0);   // (changed by hand: Custom)
        loading = false;
        SaveOptions();
    }

    // (the Insert menu's presets, gpu/vk/vk_menu.cpp apply_preset)
    void ApplyPreset(int p) {
        int[][] t = {
            null,
            new[] { 4, 2, 50, 2, 1, 1, 1 },
            new[] { 2, 2, 35, 2, 1, 1, 1 },
            new[] { 1, 1, 25, 2, 1, 1, 0 },
            new[] { 1, 2, 30, 2, 1, 1, 0 },
            new[] { 1, 2, 40, 3, 1, 1, 0 },
        };
        if (p <= 0 || p >= t.Length) return;
        for (int i = 0; i < PresetKeys.Length; i++) SetCombo(PresetKeys[i], t[p][i]);
    }

    void SaveOptions() {
        try {
            var lines = File.Exists(optionsPath) ? new List<string>(File.ReadAllLines(optionsPath)) : new List<string>();
            var done = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++) {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                string k = lines[i].Substring(0, eq).Trim();
                if (combos.ContainsKey(k)) { lines[i] = k + "=" + ComboValue(k); done.Add(k); }
            }
            // (new keys go before the controls, as the game writes them)
            int at = lines.FindIndex(l => l.StartsWith("options_version=") || l.StartsWith("pad_"));
            if (at < 0) at = lines.Count;
            foreach (var k in combos.Keys)
                if (!done.Contains(k)) lines.Insert(at++, k + "=" + ComboValue(k));
            if (!lines.Exists(l => l.StartsWith("options_version="))) lines.Insert(at, "options_version=2");
            File.WriteAllLines(optionsPath, lines.ToArray(), new UTF8Encoding(false));
        } catch (Exception ex) {
            MessageBox.Show(this, "The settings could not be saved:\n" + ex.Message, "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    Dictionary<string, string> ReadEnv() {
        var d = new Dictionary<string, string>();
        if (!File.Exists(envPath)) return d;
        foreach (var raw in File.ReadAllLines(envPath)) {
            string l = raw.Trim();
            if (l.StartsWith("#")) continue;
            int eq = l.IndexOf('=');
            if (eq > 0) d[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
        }
        return d;
    }

    void SaveEnv() {
        try {
            var lines = File.Exists(envPath) ? new List<string>(File.ReadAllLines(envPath)) : new List<string>();
            foreach (var f in Fixes) {
                lines.RemoveAll(l => { var t = l.Trim(); return !t.StartsWith("#") && t.StartsWith(f[0] + "="); });
                string v = fixes[f[0]].Checked ? f[1] : f[4];
                if (v.Length > 0) lines.Add(f[0] + "=" + v);
            }
            if (lines.Count == 0 && File.Exists(envPath)) File.Delete(envPath);
            else if (lines.Count > 0) File.WriteAllLines(envPath, lines.ToArray(), new UTF8Encoding(false));
        } catch (Exception ex) {
            MessageBox.Show(this, "The AMD fix switches could not be saved:\n" + ex.Message, "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    string ReadLanguage() {
        if (!File.Exists(langPath)) return "en-US";
        foreach (var raw in File.ReadAllLines(langPath)) {
            string l = raw.Trim();
            if (l.Length > 0 && !l.StartsWith("#")) return l;
        }
        return "en-US";
    }

    void SaveLanguage() {
        string code = Languages[Math.Max(0, langCombo.SelectedIndex)][0];
        try {
            if (!File.Exists(langPath)) { File.WriteAllText(langPath, code + "\n", new UTF8Encoding(false)); return; }
            var lines = new List<string>(File.ReadAllLines(langPath));
            int i = lines.FindIndex(l => l.Trim().Length > 0 && !l.Trim().StartsWith("#"));
            if (i >= 0) lines[i] = code; else lines.Insert(Math.Min(2, lines.Count), code);
            File.WriteAllLines(langPath, lines.ToArray(), new UTF8Encoding(false));
        } catch (Exception ex) {
            MessageBox.Show(this, "The language could not be saved:\n" + ex.Message, "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---- update ----
    static string ValueIn(string text, string name) {
        if (text == null) return "";
        foreach (var raw in text.Split('\n')) {
            string l = raw.Trim().TrimStart('﻿');
            if (l.StartsWith(name + "=")) return l.Substring(name.Length + 1).Trim();
        }
        return "";
    }

    // (an older release than this folder, e.g. v1.0-amd-fix-sharkiix against amd-fix-2.0: the first number found in each tag)
    static bool Older(string tag, string have) {
        var a = Regex.Match(tag, @"\d+(\.\d+)*"); var b = Regex.Match(have, @"\d+(\.\d+)*");
        if (!a.Success || !b.Success) return false;
        string[] x = a.Value.Split('.'), y = b.Value.Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++) {
            int u = i < x.Length ? int.Parse(x[i]) : 0, v = i < y.Length ? int.Parse(y[i]) : 0;
            if (u != v) return u < v;
        }
        return true;   // (the same number)
    }

    string Installed() {
        string p = Path.Combine(dir, "user", "hookshot_vk_version.txt");
        string v = File.Exists(p) ? ValueIn(File.ReadAllText(p), "version") : "";
        return v.Length > 0 ? v : Fork.Version;
    }

    static WebClient Client() {
        var c = new WebClient();
        c.Headers[HttpRequestHeader.UserAgent] = "Hookshot-AMD-fix-launcher";
        c.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        return c;
    }

    void SetUpdate(Color dot, string text, string button, bool enabled) {
        updateDot.ForeColor = dot;
        updateText.Text = text;
        updateButton.Text = button;
        updateButton.Primary = button == "Install update";
        updateButton.Enabled = enabled;
        updateButton.Invalidate();
    }

    void CheckUpdate() {
        patchDll = null;
        SetUpdate(Theme.Dim, "Checking for updates…", "Check again", false);
        string have = Installed();
        var t = new Thread(() => {
            string tag = "", status = "offline", dll = null; long size = 0;
            try {
                string json;
                using (var c = Client()) json = c.DownloadString(Fork.Api_Url);
                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                tag = m.Success ? m.Groups[1].Value : "";
                if (tag.Length == 0 || tag == have || Older(tag, have)) status = "current";
                else {
                    string manifest = null;
                    foreach (Match a in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) {
                        string u = a.Groups[1].Value;
                        if (u.EndsWith("/hookshot_vk_update.txt")) manifest = u;
                        if (u.EndsWith("/hookshot_vk.dll")) dll = u;
                    }
                    status = "rebuild";
                    if (manifest != null && dll != null) {
                        string mt;
                        using (var c = Client()) mt = c.DownloadString(manifest);
                        int api;
                        long.TryParse(ValueIn(mt, "size"), out size);
                        if (ValueIn(mt, "base") == Fork.Base && int.TryParse(ValueIn(mt, "api"), out api) && api == Fork.Api) status = "patch";
                    }
                }
            } catch { status = "offline"; }
            try {
                BeginInvoke((Action)(() => {
                    if (status == "current") SetUpdate(Theme.Green, "Up to date  ·  " + have, "Check again", true);
                    else if (status == "patch") {
                        patchDll = dll; patchTag = tag; patchSize = size;
                        SetUpdate(Theme.Gold, "Graphics update available: " + tag + "\nSmall download, no rebuild needed (you have " + have + ").", "Install update", true);
                    } else if (status == "rebuild")
                        SetUpdate(Theme.Red, "New version available: " + tag + "\nIt needs a rebuild with the Hookshot Builder (saves and settings stay).", "Open release page", true);
                    else SetUpdate(Theme.Dim, "Could not check for updates (offline?)  ·  " + have, "Check again", true);
                }));
            } catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    void OnUpdateButton(object sender, EventArgs e) {
        if (updateButton.Text == "Open release page") { Process.Start(Fork.Page); return; }
        if (updateButton.Text != "Install update" || patchDll == null) { CheckUpdate(); return; }
        if (GameRunning()) { MessageBox.Show(this, "Close the game first.", "Hookshot update", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        SetUpdate(Theme.Gold, "Downloading " + patchTag + "…", "Install update", false);
        string url = patchDll, tag = patchTag; long size = patchSize;
        var t = new Thread(() => {
            string error = InstallPatch(url, tag, size);
            try {
                BeginInvoke((Action)(() => {
                    if (error == null) {
                        SetUpdate(Theme.Green, "Updated to " + tag + ". The first start prepares the shaders again (a few minutes).", "Check again", true);
                    } else {
                        SetUpdate(Theme.Red, "The update could not be installed: " + error, "Check again", true);
                    }
                }));
            } catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    // (host/hos_update.c install_patch: downloaded, checked, the old dll kept as hookshot_vk.prev.dll)
    string InstallPatch(string url, string tag, long size) {
        try {
            byte[] data;
            using (var c = Client()) { c.Headers[HttpRequestHeader.Accept] = "application/octet-stream"; data = c.DownloadData(url); }
            if (data.Length < 4096 || data[0] != 'M' || data[1] != 'Z' || (size > 0 && data.Length != size)) return "the download is not right";
            string part = Path.Combine(dir, "hookshot_vk.dll.part"), cur = Path.Combine(dir, "hookshot_vk.dll"), prev = Path.Combine(dir, "hookshot_vk.prev.dll");
            File.WriteAllBytes(part, data);
            if (File.Exists(cur)) {
                if (File.Exists(prev)) File.Delete(prev);
                File.Move(cur, prev);
            }
            try { File.Move(part, cur); } catch { if (File.Exists(prev)) File.Move(prev, cur); throw; }
            Directory.CreateDirectory(Path.Combine(dir, "user"));
            File.WriteAllText(Path.Combine(dir, "user", "hookshot_vk_version.txt"), "version=" + tag + "\n");
            return null;
        } catch (Exception ex) { return ex.Message; }
    }

    // ---- play and folders ----
    static bool GameRunning() { return Process.GetProcessesByName("hookshot").Length > 0; }

    void OpenFolder(string path) {
        try {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void OnPlay(object sender, EventArgs e) {
        string exe = Path.Combine(dir, "hookshot.exe");
        if (!File.Exists(exe)) {
            MessageBox.Show(this, "hookshot.exe is not in this folder:\n" + dir + "\n\nThe launcher must sit beside the game.", "Hookshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (GameRunning()) { MessageBox.Show(this, "The game is already running.", "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = false };
            psi.EnvironmentVariables["HOOKSHOT_NO_UPDATE_CHECK"] = "1";   // (the launcher did it)
            Process.Start(psi);
            Close();
        } catch (Exception ex) { MessageBox.Show(this, "The game could not start:\n" + ex.Message, "Hookshot", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    [STAThread]
    static void Main(string[] args) {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // (TLS 1.2: GitHub)
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string dir = args.Length > 0 && Directory.Exists(args[0]) ? Path.GetFullPath(args[0]) : AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        Application.Run(new LauncherForm(dir));
    }
}
