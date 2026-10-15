// Launcher HELIA — só Process.Start no overlay.exe + watchdog do jogo.
// Launcher puro: nenhum RPM, nenhuma injecao, nenhum driver, nenhum download/update.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ── Tema ──────────────────────────────────────────────────────────────────────
static class LUi {
    public static readonly Color Bg        = Color.FromArgb(24, 15, 20);
    public static readonly Color Panel     = Color.FromArgb(33, 23, 30);
    public static readonly Color Line      = Color.FromArgb(56, 40, 50);
    public static readonly Color InputLine = Color.FromArgb(89, 71, 83);
    public static readonly Color Txt       = Color.FromArgb(245, 241, 255);
    public static readonly Color Dim       = Color.FromArgb(189, 180, 197);
    public static readonly Color Acc       = Color.FromArgb(129, 111, 255);
    public static readonly Color Success   = Color.FromArgb(0, 208, 130);
    public static readonly Color Focus     = Color.FromArgb(101, 255, 235);
    public static readonly Color Red       = Color.FromArgb(247, 166, 179);

    public static GraphicsPath Round(Rectangle r, int rad) {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X,          r.Y,           d, d, 180, 90);
        p.AddArc(r.Right - d,  r.Y,           d, d, 270, 90);
        p.AddArc(r.Right - d,  r.Bottom - d,  d, d,   0, 90);
        p.AddArc(r.X,          r.Bottom - d,  d, d,  90, 90);
        p.CloseFigure();
        return p;
    }
}

// ── Log console ───────────────────────────────────────────────────────────────
class LogConsole : Control {
    readonly System.Collections.Generic.List<(string time, string msg)> _entries = new();
    const int MaxEntries = 120;

    public LogConsole() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = LUi.Bg;
    }

    public void AppendEntry(string msg, DateTime ts) {
        _entries.Add((ts.ToString("HH:mm:ss"), msg));
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path   = LUi.Round(r, 6);
        using var bg     = new SolidBrush(Color.FromArgb(20, 12, 18));
        using var border = new Pen(LUi.Line);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        const float lineH = 17f;
        int visible = (int)((Height - 12) / lineH);
        int start   = Math.Max(0, _entries.Count - visible);

        using var tf  = new Font("Consolas", 7.5f);
        using var mf  = new Font("Consolas", 7.5f);
        using var tb  = new SolidBrush(LUi.Acc);
        using var mb  = new SolidBrush(LUi.Dim);
        for (int i = start; i < _entries.Count; i++) {
            float y = 6 + (i - start) * lineH;
            g.DrawString(_entries[i].time, tf, tb, 8,  y);
            g.DrawString(_entries[i].msg,  mf, mb, 78, y);
        }
    }
}

// ── Card com título ───────────────────────────────────────────────────────────
class Card : Panel {
    public Card(string title) {
        Text = title; BackColor = LUi.Panel;
        Paint += (_, e) => {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pn   = new Pen(LUi.Line);
            using var path = LUi.Round(new Rectangle(0, 8, Width - 1, Height - 9), 8);
            using var tf   = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var tb   = new SolidBrush(LUi.Acc);
            g.DrawPath(pn, path);
            g.DrawString(title, tf, tb, 10, 0);
        };
    }
}

// ── InputFrame com glow pulsante ──────────────────────────────────────────────
class InputFrame : Panel {
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsFocused { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsInvalid { get; set; }

    readonly System.Windows.Forms.Timer _pulse;
    float _alpha = 35f;
    bool  _up    = true;

    public InputFrame() {
        BackColor = LUi.Panel;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        // Pulsa o brilho enquanto o campo está focado
        _pulse = new System.Windows.Forms.Timer { Interval = 28 };
        _pulse.Tick += (_, _) => {
            if (!IsFocused || IsInvalid) { _alpha = 35f; _up = true; return; }
            _alpha += _up ? 3.2f : -3.2f;
            if (_alpha >= 98f) { _alpha = 98f; _up = false; }
            if (_alpha <= 28f) { _alpha = 28f; _up = true;  }
            Invalidate();
        };
        _pulse.Start();

        Paint += (_, e) => {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (IsFocused && !IsInvalid) {
                using var glow = new Pen(Color.FromArgb((int)_alpha, LUi.Acc), 5f);
                using var gp   = LUi.Round(new Rectangle(2, 2, Width - 5, Height - 5), 6);
                g.DrawPath(glow, gp);
            }

            var col = IsInvalid ? LUi.Red : IsFocused ? LUi.Focus : LUi.InputLine;
            using var pen  = new Pen(col, IsFocused ? 1.5f : 1f);
            using var path = LUi.Round(new Rectangle(1, 1, Width - 3, Height - 3), 5);
            g.DrawPath(pen, path);
        };
    }

    protected override void Dispose(bool d) {
        if (d) _pulse?.Dispose();
        base.Dispose(d);
    }
}

// ── GlowButton — shimmer sweep no hover ──────────────────────────────────────
class GlowButton : Control, IButtonControl {
    bool _hover, _down;
    float _shimX = -1f;
    readonly System.Windows.Forms.Timer _shim;

    // IButtonControl
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }
    public void NotifyDefault(bool v) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    public GlowButton() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor  = Cursors.Hand;
        TabStop = true;

        _shim = new System.Windows.Forms.Timer { Interval = 14 };
        _shim.Tick += (_, _) => {
            _shimX += Width * 0.030f;
            if (_shimX > Width + 100) { _shimX = -1f; _shim.Stop(); }
            Invalidate();
        };

        MouseEnter += (_, _) => { _hover = true;  _shimX = -100f; _shim.Start(); Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _down  = false; Invalidate(); };
        MouseDown  += (_, e) => { if (e.Button == MouseButtons.Left) { _down = true;  Invalidate(); } };
        MouseUp    += (_, e) => { if (e.Button == MouseButtons.Left) { _down = false; Invalidate(); } };
        KeyDown    += (_, e) => {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            { e.SuppressKeyPress = true; PerformClick(); }
        };
    }

    protected override void OnSizeChanged(EventArgs e) {
        base.OnSizeChanged(e);
        if (Width > 0 && Height > 0)
            Region = new Region(LUi.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8));
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = LUi.Round(r, 8);

        // Fundo
        var bg = _down  ? Color.FromArgb(100, 84, 210)  :
                 _hover ? Color.FromArgb(148, 133, 255) : LUi.Acc;
        using var bgBrush = new SolidBrush(bg);
        g.FillPath(bgBrush, path);

        // Shimmer (varredura de luz no hover)
        if (_hover && _shimX >= -100f) {
            const float sw = 95f;
            try {
                using var sh = new LinearGradientBrush(
                    new PointF(_shimX - sw, 0), new PointF(_shimX + sw, 0),
                    Color.Transparent, Color.Transparent);
                sh.InterpolationColors = new ColorBlend(3) {
                    Colors    = new[] { Color.FromArgb(0,  Color.White),
                                        Color.FromArgb(52, Color.White),
                                        Color.FromArgb(0,  Color.White) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                var clip = g.Save();
                g.SetClip(path);
                g.FillRectangle(sh, _shimX - sw, 0, sw * 2, Height);
                g.Restore(clip);
            } catch { /* shimmerX perto das bordas pode gerar retângulo degenerado */ }
        }

        // Glow externo no hover
        if (_hover && !_down) {
            using var glow = new Pen(Color.FromArgb(65, LUi.Acc), 4f);
            using var gp   = LUi.Round(new Rectangle(2, 2, Width - 5, Height - 5), 7);
            g.DrawPath(glow, gp);
        }

        // Borda
        using var border = new Pen(Color.FromArgb(155, 139, 255), 1f);
        g.DrawPath(border, path);

        // Texto
        using var font  = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var brush = new SolidBrush(LUi.Bg);
        var sf = new StringFormat {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
        };
        g.DrawString(Text ?? "", font, brush, new RectangleF(0, 0, Width, Height), sf);
    }

    protected override void Dispose(bool d) {
        if (d) _shim?.Dispose();
        base.Dispose(d);
    }
}

// ── Barra de progresso com gradiente ─────────────────────────────────────────
class SeqBar : Control {
    int _v, _max = 4;
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value   { get => _v;   set { _v   = Math.Max(0, Math.Min(_max, value)); Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => _max; set { _max  = Math.Max(1, value); Invalidate(); } }

    public SeqBar() {
        Height = 12;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = LUi.Bg;
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 2, Width - 1, Height - 5);
        using var tb        = new SolidBrush(LUi.Panel);
        using var trackPath = LUi.Round(track, 5);
        g.FillPath(tb, trackPath);
        using var tp = new Pen(LUi.Line);
        g.DrawPath(tp, trackPath);

        float f  = _max <= 0 ? 0f : _v / (float)_max;
        int   fw = (int)((track.Width - 4) * f);
        if (fw > 0) {
            var fill = new Rectangle(track.X + 2, track.Y + 2, Math.Max(fw, 10), track.Height - 4);
            // Gradiente ciano → roxo
            using var lg       = new LinearGradientBrush(fill, LUi.Focus, LUi.Acc, LinearGradientMode.Horizontal);
            using var fillPath = LUi.Round(fill, 4);
            g.FillPath(lg, fillPath);
        }
    }
}

// ── Arte embutida (single-exe): resource → arquivo ao lado → bitmap 1x1 ──────
static class Art {
    public static Image Load(string file) {
        try {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var st = asm.GetManifestResourceStream("loader." + file);
            if (st != null) { using var s = Image.FromStream(st); return new Bitmap(s); }
        } catch { }
        try {
            var p = System.IO.Path.Combine(Application.StartupPath, file);
            if (System.IO.File.Exists(p)) { using var s = Image.FromFile(p); return new Bitmap(s); }
        } catch { }
        return null;
    }
}

// ── Banner ────────────────────────────────────────────────────────────────────
class Banner : Control {
    string _ver;
    Image  _image;

    public Banner(string ver) {
        _ver = ver;
        try { _image = Art.Load("PaladinsStartBanner.jpg"); } catch { _image = null; }
        Height = 84;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = LUi.Bg;
    }

    public void SetVer(string v) { _ver = v; Invalidate(); }

    protected override void Dispose(bool d) {
        if (d) { try { _image?.Dispose(); } catch { } }
        base.Dispose(d);
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var bgPath = LUi.Round(r, 10);
        if (_image != null) {
            var st = g.Save(); g.SetClip(bgPath);
            float tA = Width / (float)Height, sA = _image.Width / (float)_image.Height;
            float sx = 0, sy = 0, sw = _image.Width, sh = _image.Height;
            if (sA > tA) { sw = _image.Height * tA; sx = (_image.Width  - sw) / 2f; }
            else         { sh = _image.Width  / tA; sy = (_image.Height - sh) / 2f; }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, r, sx, sy, sw, sh, GraphicsUnit.Pixel);
            using var shade = new LinearGradientBrush(r, Color.FromArgb(225, LUi.Bg), Color.FromArgb(70, LUi.Bg), 0f);
            g.FillPath(shade, bgPath);
            g.Restore(st);
        } else {
            using var lg = new LinearGradientBrush(r, Color.FromArgb(40, 28, 38), LUi.Bg, 45f);
            g.FillPath(lg, bgPath);
        }
        using var lp = new Pen(LUi.Line); g.DrawPath(lp, bgPath);
        using var ab = new SolidBrush(LUi.Acc); g.FillRectangle(ab, 0, 10, 3, Height - 20);

        void T(string t, Font f, Brush c, float x, float y) {
            g.DrawString(t, f, Brushes.Black, x + 1, y + 1); g.DrawString(t, f, c, x, y);
        }
        using var tf1 = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var tb1 = new SolidBrush(LUi.Txt);
        using var tf2 = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var tb2 = new SolidBrush(LUi.Acc);
        using var tf3 = new Font("Segoe UI", 8.5f);
        using var tb3 = new SolidBrush(LUi.Dim);
        T("PALADINS", tf1, tb1, 14, 8); T("ESP EXTERNO", tf2, tb2, 16, 38);
        T("externo e passivo • v" + _ver, tf3, tb3, 16, 60);

        float cx = Width - 64, cy = Height / 2;
        using var acc = new Pen(LUi.Acc, 1.6f);
        g.DrawEllipse(acc, cx - 24, cy - 24, 48, 48);
        g.DrawLine(acc, cx - 32, cy, cx - 8, cy); g.DrawLine(acc, cx + 8, cy, cx + 32, cy);
        g.DrawLine(acc, cx, cy - 32, cx, cy - 8); g.DrawLine(acc, cx, cy + 8, cx, cy + 32);
        using var dot = new SolidBrush(LUi.Success); g.FillEllipse(dot, cx - 2, cy - 2, 4, 4);
        using var box = new Pen(LUi.Success, 1.6f);
        float bx = cx - 82, by = cy - 20, bw = 30, bh = 40, L = 7;
        g.DrawLine(box, bx,      by,      bx + L, by);      g.DrawLine(box, bx,      by,      bx,      by + L);
        g.DrawLine(box, bx+bw-L, by,      bx + bw, by);     g.DrawLine(box, bx + bw, by,      bx + bw, by + L);
        g.DrawLine(box, bx,      by+bh-L, bx,      by + bh); g.DrawLine(box, bx,      by + bh, bx + L,  by + bh);
        g.DrawLine(box, bx+bw-L, by+bh,   bx + bw, by + bh); g.DrawLine(box, bx + bw, by+bh-L, bx + bw, by + bh);
    }
}

// ── BrandLogo ─────────────────────────────────────────────────────────────────
class BrandLogo : Control {
    readonly Image     _image;
    readonly Rectangle _src = new Rectangle(24, 174, 720, 398);

    public BrandLogo() {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _image = Art.Load("BrandLogo.png");
        BackColor = Color.Transparent;
    }

    protected override void Dispose(bool d) { if (d) _image?.Dispose(); base.Dispose(d); }

    protected override void OnPaint(PaintEventArgs e) {
        var b = ClientRectangle;
        if (b.Width <= 0 || b.Height <= 0 || _image == null) return;
        float sc = Math.Min(b.Width / (float)_src.Width, b.Height / (float)_src.Height);
        var dst = new RectangleF((b.Width  - _src.Width  * sc) / 2f,
                                 (b.Height - _src.Height * sc) / 2f,
                                  _src.Width * sc, _src.Height * sc);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, dst, _src, GraphicsUnit.Pixel);
    }
}

// ── ProductCard ───────────────────────────────────────────────────────────────
class ProductCard : Panel {
    readonly Image _image;

    public ProductCard() {
        using var src = Art.Load("PaladinsProductCard.jpg") ?? new Bitmap(1, 1);
        int w = Math.Max(1, src.Width / 12), h = Math.Max(1, src.Height / 12);
        _image = new Bitmap(w, h);
        using var g = Graphics.FromImage(_image);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = LUi.Panel;
    }

    protected override void Dispose(bool d) { if (d) _image?.Dispose(); base.Dispose(d); }

    protected override void OnPaintBackground(PaintEventArgs e) {
        var g = e.Graphics;
        var b = ClientRectangle;
        if (b.Width <= 0 || b.Height <= 0) return;
        using var path = LUi.Round(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        var st = g.Save(); g.SetClip(path);
        float tA = b.Width / (float)b.Height, sA = _image.Width / (float)_image.Height;
        float sx = 0, sy = 0, sw = _image.Width, sh = _image.Height;
        if (sA > tA) { sw = _image.Height * tA; sx = (_image.Width  - sw) / 2f; }
        else         { sh = _image.Width  / tA; sy = (_image.Height - sh) / 2f; }
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_image, b, sx, sy, sw, sh, GraphicsUnit.Pixel);
        using var shade = new SolidBrush(Color.FromArgb(205, LUi.Bg));
        g.FillRectangle(shade, b);
        using var glass = new LinearGradientBrush(b, Color.FromArgb(28, Color.White),
                                                     Color.FromArgb(5,  Color.White),
                                                     LinearGradientMode.Vertical);
        g.FillRectangle(glass, b);
        g.Restore(st);
    }
}

// ── Formulário principal ──────────────────────────────────────────────────────
class HeliaLoader : Form {
    const string VerFallback = "3.1.0";
    // TODO: cole seu convite (ex: https://discord.gg/xxxx). Vazio = botão avisa que falta configurar.
    const string DiscordUrl  = "";
    static readonly string[] GameNames = { "Paladins", "ShippingPC-ChaosGame" };

    // Páginas
    Panel pgLogin, pgProduto, pgStart, pgRegister, pgUpdate;

    // pgLogin
    TextBox    txUser, txKey;
    Label      lbUserErr, lbKeyErr, lbKey, lkRegister;
    InputFrame userFrame, keyFrame;
    GlowButton btLogin;
    Button     btEye, btDiscord;

    // pgRegister (primeiro acesso)
    TextBox    txCode;
    Label      lbCodeErr;
    InputFrame codeFrame;
    Button     btActivate, btTrial;
    ToolTip    windowTips;

    // pgUpdate (overlay de atualização com animação)
    Label      lbUpdTitle, lbUpdVer, lbUpdStatus;
    SeqBar     updBar;
    Button     btUpdYes, btUpdNo;
    System.Windows.Forms.Timer _updTimer;
    int _updDots;
    string _updBase = "";
    System.Threading.Tasks.TaskCompletionSource<bool> _updChoice;

    // pgProduto
    ProductCard prodCard;
    Label       prodTitle;
    Label       lbAccUser, lbAccKey, lbAccExp, lbAccRenew, lbAccHwid;
    bool        prodSelected;
    Button      btContinue;

    // pgStart
    Label      lbGame, lbOverlay, lbVer, lbGameDot, lbOverlayDot, lbErr, lbSeq;
    Button     btStart;
    LogConsole log;
    SeqBar     pbSeq;
    Banner     banner;
    System.Windows.Forms.Timer wd, seqTimer;
    int    seqIdx;
    int    seqWait;
    DateTime lastStart = DateTime.MinValue;
    Process child;
    string  overlayPath;
    string  versionShown = VerFallback;
    bool    auto = true, closeWithGame = true;
    string  savedUser = "";
    // Literais sensíveis cifrados (xor i+0x38, mesmo esquema minerado).
    static class Cloak {
        public static string D(byte[] c) {
            var b = new byte[c.Length];
            for (int i = 0; i < c.Length; i++) b[i] = (byte)(c[i] ^ ((i + 0x38) & 0xFF));
            return System.Text.Encoding.UTF8.GetString(b);
        }
    }
    static readonly byte[] EncBase = new byte[] { 80, 77, 78, 75, 79, 7, 17, 16, 50, 32, 53, 109, 35, 44, 50, 47, 61, 43, 63, 56, 41, 63, 45, 32, 62, 37, 55, 61, 32, 123, 53, 56, 53, 118, 50, 47, 47, 40, 51, 62, 3, 19, 23, 14, 75, 1, 20, 8, 24, 26, 71, 30, 28, 9, 15, 27, 21, 2, 93, 30, 21, 28, 24 };
    static readonly byte[] EncGate = new byte[] { 116, 86, 89, 90, 80, 97, 118, 122, 12, 8, 3, 28, 8, 10, 7, 3, 13, 27, 21, 25, 25, 3, 0, 6, 30, 22 };
    static string UpdateBase => Cloak.D(EncBase);
    static string GateName => Cloak.D(EncGate);
    const string LoaderVersion = "1.5.3";
    const int    SessionDays = 30;
    System.Threading.Mutex gate;
    string   sessionId  = "";
    DateTime sessionExp;
    DateTime lastOk;      // última validação online (pulo do login vale 24h)
    long     keyExp;      // expiração da key no servidor (ms epoch, 0 = n/a)
    string   keyTail = ""; // últimos 4 da key (só exibição mascarada)
    // URL base do auth server (cifrada via Cloak).
    static readonly byte[] EncApi = new byte[] { 80, 77, 78, 75, 79, 7, 17, 16, 33, 49, 43, 109, 32, 55, 41, 55, 43, 33, 47, 42, 56, 62, 96, 60, 36, 62, 32, 54 };
    static string ApiBase => Cloak.D(EncApi);
    Image    bgImg;

    // ── Animações ─────────────────────────────────────────────────────────────
    System.Windows.Forms.Timer _fadeTimer;
    System.Windows.Forms.Timer _loginDotsTimer;
    double _fadeOpacity    = 0.0;
    int    _loginDotsCount = 0;

    // Partículas estáticas no fundo (sem timer = sem piscada)
    static readonly (float X, float Y, float R, int A)[] _pts = BuildPts();
    static (float, float, float, int)[] BuildPts() {
        var rng = new Random(13);
        var ps  = new (float, float, float, int)[26];
        for (int i = 0; i < ps.Length; i++)
            ps[i] = ((float)(rng.NextDouble() * 520),
                     (float)(rng.NextDouble() * 640),
                     (float)(rng.NextDouble() * 1.7 + 0.5),
                     rng.Next(12, 52));
        return ps;
    }

    public HeliaLoader() {
        Text = "Display Launcher Utility";
        Width = 520; Height = 640;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor = LUi.Bg;
        Font      = new Font("Segoe UI", 9f);
        using var rg = LUi.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        Region = new Region(rg);
        DoubleBuffered = true;
        Opacity = 0.0;  // começa invisível → fade-in

        try {
            foreach (var n in new[] { "BgBackdrop.jpg", "PaladinsStartBanner.jpg" }) {
                var img = Art.Load(n);
                if (img != null) { bgImg = new Bitmap(img); img.Dispose(); break; }
            }
        } catch { bgImg = null; }

        // Arrastar janela pela barra superior
        bool drag = false; Point dp = default;
        void BeginDrag(Point p) { drag = true; dp = p; }
        MouseDown += (_, e) => { if (e.Y < 84) BeginDrag(e.Location); };
        MouseUp   += (_, _) => drag = false;
        MouseMove += (_, e) => { if (drag) Location = new Point(Left + e.X - dp.X, Top + e.Y - dp.Y); };

        var head = new BrandLogo() {
            Left = 18, Top = 16, Width = 76, Height = 36, AccessibleName = "Drops Cheats",
        };
        head.MouseDown += (_, e) => BeginDrag(e.Location);
        head.MouseUp   += (_, _) => drag = false;
        head.MouseMove += (_, e) => { if (drag) Location = new Point(Left + e.X - dp.X, Top + e.Y - dp.Y); };
        Controls.Add(head);

        windowTips = new ToolTip { ShowAlways = true, InitialDelay = 450 };
        var btMin = MkWinBtn("\u2212", 432, "Minimizar janela");
        btMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        Controls.Add(btMin);
        var btX = MkWinBtn("\u00D7", 472, "Fechar janela");
        btX.MouseEnter += (_, _) => btX.ForeColor = Color.White;
        btX.Click += (_, _) => Close();
        Controls.Add(btX);

        BuildLoginPage();
        BuildProductPage();
        BuildStartPage();
        BuildRegisterPage();
        BuildUpdatePage();
        Controls.Add(pgLogin);
        Controls.Add(pgProduto);
        Controls.Add(pgStart);
        Controls.Add(pgRegister);
        Controls.Add(pgUpdate);

        LoadCfg();
        txUser.Text    = savedUser;
        overlayPath    = ResolveOverlay();
        versionShown   = ResolveVersion(overlayPath);
        prodTitle.Text = "Paladins \u00B7 overlay externo  \u2022  v" + versionShown;
        try { banner?.SetVer(versionShown); } catch { }

        seqTimer = new System.Windows.Forms.Timer { Interval = 300 };
        seqTimer.Tick += (_, _) => SeqTick();
        wd = new System.Windows.Forms.Timer { Interval = 1000 };
        wd.Tick += (_, _) => Watchdog();
        wd.Start();

        // ── Fade-in suave ao abrir ────────────────────────────────────────────
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _fadeTimer.Tick += (_, _) => {
            _fadeOpacity = Math.Min(1.0, _fadeOpacity + 0.07);
            Opacity = _fadeOpacity;
            if (_fadeOpacity >= 1.0) _fadeTimer.Stop();
        };
        _fadeTimer.Start();

        // ── Pontos de carregamento no botão ENTRAR ────────────────────────────
        _loginDotsTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _loginDotsTimer.Tick += (_, _) => {
            _loginDotsCount = (_loginDotsCount % 3) + 1;
            btLogin.Text = "AGUARDE" + new string('.', _loginDotsCount);
        };

        // ── Animação da tela de update (pontinhos + barra pulsante) ──────────
        _updTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _updTimer.Tick += (_, _) => {
            try {
                _updDots = (_updDots % 3) + 1;
                if (!string.IsNullOrEmpty(_updBase)) lbUpdStatus.Text = _updBase + new string('.', _updDots);
                updBar.Value = (updBar.Value + 9) % 100;
            } catch { }
        };

        FormClosing += (_, _) => {
            wd.Stop(); _fadeTimer.Stop(); _loginDotsTimer.Stop();
            try { _updTimer.Stop(); } catch { }
            try { seqTimer.Stop(); } catch { }
            windowTips?.Dispose();
            try { bgImg?.Dispose(); } catch { }
            try { gate?.ReleaseMutex(); } catch { }
            try { gate?.Dispose(); }     catch { }
            SaveCfg();
        };

        Log(overlayPath != null ? "overlay localizado." : "procurando overlay…");
        Dbg("startup overlayPath=" + (overlayPath ?? "<null>"));
        HoldGate();

        if (ValidSession() && FreshLogin()) {
            try { sessionExp = DateTime.UtcNow.AddDays(SessionDays); SaveCfg(); } catch { }
            Log("sessão válida, login pulado.");
            ShowPage(1);
        } else {
            sessionId = ""; sessionExp = DateTime.MinValue;
            ShowPage(0);
        }
        RefreshStatus();
        try { CheckUpdate(); } catch { }
    }

    Label MkWinBtn(string sym, int left, string tip) {
        var lb = new Label {
            Text = sym, Left = left, Top = 8, Width = 36, Height = 32,
            Font = new Font("Segoe UI", 13f), ForeColor = LUi.Dim,
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        lb.MouseEnter += (_, _) => lb.ForeColor = LUi.Txt;
        lb.MouseLeave += (_, _) => lb.ForeColor = LUi.Dim;
        windowTips.SetToolTip(lb, tip);
        return lb;
    }

    // ── Fundo com partículas estáticas ────────────────────────────────────────
    protected override void OnPaintBackground(PaintEventArgs e) {
        var g = e.Graphics;
        g.Clear(LUi.Bg);
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        if (bgImg != null) {
            float tA = r.Width / (float)r.Height;
            float sw  = bgImg.Width / 1.06f, sh = sw / tA;
            if (sh > bgImg.Height) { sh = bgImg.Height / 1.06f; sw = sh * tA; }
            float sx = Math.Max(0, (bgImg.Width  - sw) / 2f);
            float sy = Math.Max(0, (bgImg.Height - sh) / 2f);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bgImg, r, sx, sy, sw, sh, GraphicsUnit.Pixel);
        }
        using var tint = new SolidBrush(Color.FromArgb(212, LUi.Bg));
        g.FillRectangle(tint, r);

        // Partículas roxas discretas espalhadas
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var (X, Y, R, A) in _pts) {
            using var pb = new SolidBrush(Color.FromArgb(A, LUi.Acc));
            g.FillEllipse(pb, X - R, Y - R, R * 2, R * 2);
        }
    }

    // ── Navegação entre páginas ───────────────────────────────────────────────
    void ShowPage(int n) {
        if (n != 2) {
            try { seqTimer.Stop(); } catch { }
            try { if (btStart != null) btStart.Enabled = true; } catch { }
        }
        pgLogin.Visible   = n == 0;
        pgProduto.Visible = n == 1;
        pgStart.Visible   = n == 2;
        pgRegister.Visible = n == 3;
        if (n == 0) ActiveControl = txUser;
        else if (n == 1) { ActiveControl = btContinue; RefreshAccount(); }
    }

    // ── pgUpdate (overlay de atualização com animação) ──────────────────────
    void BuildUpdatePage() {
        pgUpdate = new Panel { Left = 0, Top = 88, Width = 520, Height = 514, BackColor = LUi.Bg, Visible = false };

        lbUpdTitle = new Label {
            Text = "Atualização disponível", Left = 22, Top = 12, Width = 476, Height = 28,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        lbUpdVer = new Label {
            Text = "", Left = 22, Top = 43, Width = 476, Height = 24,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = LUi.Acc, BackColor = Color.Transparent,
        };
        lbUpdStatus = new Label {
            Text = "", Left = 22, Top = 84, Width = 476, Height = 22,
            Font = new Font("Segoe UI", 9f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
        };
        updBar = new SeqBar { Left = 22, Top = 112, Width = 476, Maximum = 100, Value = 0 };

        btUpdYes = new Button {
            Text = "ATUALIZAR", Left = 22, Top = 150, Width = 476, Height = 44,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = LUi.Acc, ForeColor = LUi.Bg, FlatStyle = FlatStyle.Flat,
        };
        btUpdYes.FlatAppearance.BorderSize = 0;
        btUpdYes.Cursor = Cursors.Hand;
        btUpdYes.Region = new Region(LUi.Round(new Rectangle(0, 0, btUpdYes.Width - 1, btUpdYes.Height - 1), 8));
        btUpdYes.Click += (_, _) => {
            try { btUpdYes.Enabled = false; btUpdNo.Enabled = false; } catch { }
            try { _updChoice?.TrySetResult(true); } catch { }
        };

        btUpdNo = new Button {
            Text = "DEPOIS", Left = 22, Top = 202, Width = 476, Height = 36,
            Font = new Font("Segoe UI", 9f), BackColor = Color.FromArgb(30, 30, 36),
            ForeColor = LUi.Txt, FlatStyle = FlatStyle.Flat,
        };
        btUpdNo.FlatAppearance.BorderSize = 0;
        btUpdNo.Cursor = Cursors.Hand;
        btUpdNo.Click += (_, _) => {
            try { pgUpdate.Visible = false; } catch { }
            try { _updChoice?.TrySetResult(false); } catch { }
        };

        pgUpdate.Controls.AddRange(new Control[] {
            lbUpdTitle, lbUpdVer, lbUpdStatus, updBar, btUpdYes, btUpdNo,
        });
    }

    System.Threading.Tasks.Task<bool> AskUpdateAsync(string ver, long size) {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        _updChoice = tcs;
        try {
            try { _updTimer.Stop(); } catch { }
            lbUpdVer.Text = "v" + ver + (size > 0 ? "  •  " + (size / 1024).ToString("N0") + " KB" : "");
            lbUpdStatus.ForeColor = LUi.Dim;
            lbUpdStatus.Text = "Uma nova versão do loader está disponível.";
            updBar.Value = 0;
            btUpdYes.Enabled = true;
            btUpdNo.Text = "DEPOIS"; btUpdNo.Enabled = true;
            pgUpdate.Visible = true; pgUpdate.BringToFront();
            Dbg("oferta de update exibida (" + ver + ")");
        } catch (Exception ex) { Dbg("AskUpdate: " + ex.Message); tcs.TrySetResult(false); }
        return tcs.Task;
    }

    void UpdStatus(string msg, int pct = -1) {
        try {
            lbUpdStatus.ForeColor = LUi.Dim;
            if (pct >= 0) {
                try { _updTimer.Stop(); } catch { }
                lbUpdStatus.Text = msg;
                updBar.Value = Math.Max(0, Math.Min(100, pct));
            } else {
                _updBase = msg;
                _updDots = 0;
                _updTimer.Start();
            }
        } catch { }
    }

    void UpdFail(string msg) {
        try { _updTimer.Stop(); } catch { }
        try {
            lbUpdStatus.ForeColor = LUi.Red;
            lbUpdStatus.Text = msg;
            updBar.Value = 0;
            btUpdNo.Text = "FECHAR"; btUpdNo.Enabled = true;
            Dbg("update loader: " + msg);
        } catch { }
    }

    // ── pgLogin ───────────────────────────────────────────────────────────────
    void BuildLoginPage() {
        pgLogin = new Panel { Left = 0, Top = 88, Width = 520, Height = 514, BackColor = Color.Transparent };

        var title = new Label {
            Text = "Bem-vindo de volta", Left = 22, Top = 12, Width = 476, Height = 28,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        var intro = new Label {
            Text = "Entre com suas credenciais para continuar.",
            Left = 22, Top = 43, Width = 476, Height = 24,
            Font = new Font("Segoe UI", 9f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
            AccessibleName = "Sobre o acesso", AccessibleDescription = "Informe usuário e key para continuar.",
        };

        var lbUser = new Label {
            Text = "Usuário", Left = 22, Top = 84, Width = 476, Height = 18,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        userFrame = new InputFrame { Left = 22, Top = 107, Width = 476, Height = 42, AccessibleName = "Campo Usuário" };
        txUser = new TextBox {
            Left = 10, Top = 8, Width = 456, Height = 24,
            Font = new Font("Segoe UI", 10f), BackColor = LUi.Panel, ForeColor = LUi.Txt,
            BorderStyle = BorderStyle.None,
            AccessibleName = "Usuário", AccessibleDescription = "Mínimo de 3 caracteres.", TabIndex = 0,
        };
        userFrame.Controls.Add(txUser);
        lbUserErr = CreateLoginError("Informe pelo menos 3 caracteres.", "Erro no campo Usuário");
        lbUserErr.Left = 22; lbUserErr.Top = 153; lbUserErr.Width = 476;

        lbKey = new Label {
            Text = "Key", Left = 22, Top = 169, Width = 476, Height = 18,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        keyFrame = new InputFrame { Left = 22, Top = 192, Width = 476, Height = 42, AccessibleName = "Campo Key" };
        txKey = new TextBox {
            Left = 10, Top = 8, Width = 390, Height = 24,
            Font = new Font("Segoe UI", 10f), BackColor = LUi.Panel, ForeColor = LUi.Txt,
            BorderStyle = BorderStyle.None, UseSystemPasswordChar = true,
            AccessibleName = "Key", AccessibleDescription = "Mínimo de 4 caracteres. A entrada fica mascarada.",
            TabIndex = 1,
        };
        btEye = new Button {
            Text = "ver", Left = 408, Top = 7, Width = 58, Height = 28,
            Font = new Font("Segoe UI", 8f), BackColor = LUi.Panel, ForeColor = LUi.Dim,
            FlatStyle = FlatStyle.Flat, TabStop = false, AccessibleName = "Mostrar ou ocultar key",
        };
        btEye.FlatAppearance.BorderSize = 0;
        btEye.Cursor = Cursors.Hand;
        btEye.MouseEnter += (_, _) => btEye.ForeColor = LUi.Txt;
        btEye.MouseLeave += (_, _) => btEye.ForeColor = LUi.Dim;
        btEye.Click += (_, _) => {
            txKey.UseSystemPasswordChar = !txKey.UseSystemPasswordChar;
            btEye.Text = txKey.UseSystemPasswordChar ? "ver" : "ocultar";
        };
        keyFrame.Controls.Add(txKey);
        keyFrame.Controls.Add(btEye);
        lbKeyErr = CreateLoginError("Informe pelo menos 4 caracteres.", "Erro no campo Key");
        lbKeyErr.Left = 22; lbKeyErr.Top = 238; lbKeyErr.Width = 476;

        // GlowButton substitui o Button padrão
        btLogin = new GlowButton {
            Text = "ENTRAR", Left = 22, Top = 303, Width = 476, Height = 44, TabIndex = 2,
            AccessibleName = "Entrar", AccessibleDescription = "Avança para seleção do produto.",
        };
        btLogin.Click += (_, _) => DoLogin();

        btDiscord = new Button {
            Text = "DISCORD", Left = 22, Top = 360, Width = 476, Height = 42,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(88, 101, 242), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = "Abrir Discord",
            AccessibleDescription = "Abre o Discord para comprar key ou falar com suporte.",
            TabIndex = 3,
        };
        btDiscord.FlatAppearance.BorderSize = 0;
        btDiscord.FlatAppearance.MouseOverBackColor = Color.FromArgb(71, 82, 196);
        btDiscord.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 69, 165);
        btDiscord.Cursor = Cursors.Hand;
        btDiscord.Region = new Region(LUi.Round(new Rectangle(0, 0, btDiscord.Width - 1, btDiscord.Height - 1), 8));
        btDiscord.Click += (_, _) => OpenDiscord();

        lkRegister = new Label {
            Text = "Primeiro acesso? Ative seu código aqui", Left = 22, Top = 410, Width = 476, Height = 20,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Underline), BackColor = Color.Transparent, ForeColor = LUi.Dim,
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            AccessibleName = "Ir para primeiro acesso",
        };
        lkRegister.MouseEnter += (_, _) => lkRegister.ForeColor = LUi.Acc;
        lkRegister.MouseLeave += (_, _) => lkRegister.ForeColor = LUi.Dim;
        lkRegister.Click += (_, _) => ShowPage(3);

        txKey.KeyDown  += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); } };
        txUser.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); } };
        txUser.TextChanged += (_, _) => { if (txUser.Text.Trim().Length >= 3) ClearLoginError(true);  };
        txKey.TextChanged  += (_, _) => { if (txKey.Text.Trim().Length >= 4)  ClearLoginError(false); };
        txUser.Enter += (_, _) => UpdateInputFrames();
        txUser.Leave += (_, _) => UpdateInputFrames();
        txKey.Enter  += (_, _) => UpdateInputFrames();
        txKey.Leave  += (_, _) => UpdateInputFrames();

        pgLogin.Controls.AddRange(new Control[] {
            title, intro, lbUser, userFrame, lbUserErr,
            lbKey, keyFrame, lbKeyErr, btLogin, btDiscord, lkRegister,
        });
        AcceptButton = btLogin;
        ReflowLoginPage();
        UpdateInputFrames();
    }

    // ── pgRegister (primeiro acesso) ──────────────────────────────────────────
    void BuildRegisterPage() {
        pgRegister = new Panel { Left = 0, Top = 88, Width = 520, Height = 514, BackColor = Color.Transparent };

        var title = new Label {
            Text = "Primeiro acesso", Left = 22, Top = 12, Width = 476, Height = 28,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        var intro = new Label {
            Text = "Ative seu código ou teste grátis.",
            Left = 22, Top = 43, Width = 476, Height = 24,
            Font = new Font("Segoe UI", 9f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
        };
        var lbCode = new Label {
            Text = "Código de ativação", Left = 22, Top = 84, Width = 476, Height = 18,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        codeFrame = new InputFrame { Left = 22, Top = 107, Width = 476, Height = 42, AccessibleName = "Código" };
        txCode = new TextBox {
            Left = 10, Top = 8, Width = 456, Height = 24,
            Font = new Font("Segoe UI", 10f), BackColor = LUi.Panel, ForeColor = LUi.Txt,
            BorderStyle = BorderStyle.None, CharacterCasing = CharacterCasing.Upper,
            AccessibleName = "Código de ativação", TabIndex = 0,
        };
        codeFrame.Controls.Add(txCode);
        lbCodeErr = CreateLoginError("", "Erro na ativação");
        lbCodeErr.Left = 22; lbCodeErr.Top = 153; lbCodeErr.Width = 476;

        btActivate = new Button {
            Text = "ATIVAR", Left = 22, Top = 180, Width = 476, Height = 44,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = LUi.Acc, ForeColor = LUi.Bg, FlatStyle = FlatStyle.Flat, TabIndex = 1,
        };
        btActivate.FlatAppearance.BorderSize = 0;
        btActivate.Cursor = Cursors.Hand;
        btActivate.Region = new Region(LUi.Round(new Rectangle(0, 0, btActivate.Width - 1, btActivate.Height - 1), 8));
        btActivate.Click += (_, _) => DoActivate();

        btTrial = new Button {
            Text = "TESTAR 24H GRÁTIS", Left = 22, Top = 232, Width = 476, Height = 42,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(88, 101, 242), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, TabIndex = 2,
        };
        btTrial.FlatAppearance.BorderSize = 0;
        btTrial.FlatAppearance.MouseOverBackColor = Color.FromArgb(71, 82, 196);
        btTrial.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 69, 165);
        btTrial.Cursor = Cursors.Hand;
        btTrial.Region = new Region(LUi.Round(new Rectangle(0, 0, btTrial.Width - 1, btTrial.Height - 1), 8));
        btTrial.Click += (_, _) => DoTrial();

        var btRegBack = new Button {
            Text = "VOLTAR", Left = 22, Top = 282, Width = 476, Height = 36,
            Font = new Font("Segoe UI", 9f), BackColor = Color.FromArgb(30, 30, 36),
            ForeColor = LUi.Txt, FlatStyle = FlatStyle.Flat,
        };
        btRegBack.FlatAppearance.BorderSize = 0;
        btRegBack.Click += (_, _) => ShowPage(0);

        txCode.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoActivate(); } };
        txCode.Enter += (_, _) => UpdateRegFrame();
        txCode.Leave += (_, _) => UpdateRegFrame();

        pgRegister.Controls.AddRange(new Control[] {
            title, intro, lbCode, codeFrame, lbCodeErr, btActivate, btTrial, btRegBack,
        });
        UpdateRegFrame();
    }

    void UpdateRegFrame() {
        if (codeFrame == null) return;
        codeFrame.IsInvalid = lbCodeErr?.Visible == true;
        codeFrame.IsFocused = txCode?.Focused == true;
        codeFrame.Invalidate();
    }

    void RegBusy(bool busy, string err = "") {
        try {
            btActivate.Enabled = !busy; btTrial.Enabled = !busy;
            lbCodeErr.Visible = err.Length > 0;
            if (err.Length > 0) lbCodeErr.Text = err;
            UpdateRegFrame();
        } catch { }
    }

    async void DoActivate() {
        var c = (txCode.Text ?? "").Trim().ToUpperInvariant();
        if (c.Length < 4) { RegBusy(false, "Informe o código recebido no Discord."); return; }
        RegBusy(true);
        try {
            var (ok, key, exp, msg) = await ActivateAsync(c);
            if (!ok || string.IsNullOrEmpty(key)) {
                RegBusy(false, string.IsNullOrEmpty(msg) ? "Código recusado." : msg);
                return;
            }
            RegBusy(false);
            txKey.Text = key;
            Log("código ativado.");
            ShowPage(0);
        } catch { RegBusy(false, "Falha de conexão."); }
    }

    async void DoTrial() {
        RegBusy(true);
        try {
            var (ok, key, exp, msg) = await TrialAsync();
            if (!ok || string.IsNullOrEmpty(key)) {
                RegBusy(false, string.IsNullOrEmpty(msg) ? "Trial indisponível." : msg);
                return;
            }
            RegBusy(false);
            txKey.Text = key;
            Log("trial gerado.");
            ShowPage(0);
        } catch { RegBusy(false, "Falha de conexão."); }
    }

    static Label CreateLoginError(string message, string accessibleName) => new Label {
        Text = message, Visible = false, Height = 18,
        Font = new Font("Segoe UI", 8.5f), ForeColor = LUi.Red, BackColor = Color.Transparent,
        AccessibleName = accessibleName, AccessibleRole = AccessibleRole.StaticText,
    };

    void ReflowLoginPage() {
        if (pgLogin == null || userFrame == null || keyFrame == null) return;
        int ue = lbUserErr.Visible ? 20 : 0, ke = lbKeyErr.Visible ? 20 : 0;
        lbKey.Top    = 169 + ue;
        keyFrame.Top = 192 + ue;
        lbKeyErr.Top = 238 + ue;
        int off = ue + ke;
        btLogin.Top   = 303 + off;
        btDiscord.Top = 360 + off;
        lkRegister.Top = 410 + off;
        UpdateInputFrames();
    }

    void UpdateInputFrames() {
        if (userFrame == null || keyFrame == null) return;
        userFrame.IsInvalid = lbUserErr?.Visible == true;
        userFrame.IsFocused = txUser?.Focused    == true;
        keyFrame.IsInvalid  = lbKeyErr?.Visible  == true;
        keyFrame.IsFocused  = txKey?.Focused     == true;
        userFrame.Invalidate();
        keyFrame.Invalidate();
        CenterInputs();
    }

    void CenterInputs() {
        try {
            if (userFrame != null && txUser != null)
                txUser.Top = Math.Max(0, (userFrame.Height - txUser.Height) / 2 + 1);
            if (keyFrame  != null && txKey  != null)
                txKey.Top  = Math.Max(0, (keyFrame.Height  - txKey.Height)  / 2 + 1);
            if (keyFrame  != null && btEye  != null)
                btEye.Top  = Math.Max(0, (keyFrame.Height  - btEye.Height)  / 2);
        } catch { }
    }

    void ClearLoginError(bool user) {
        var err = user ? lbUserErr : lbKeyErr;
        if (!err.Visible) return;
        err.Visible = false;
        if (user) txUser.AccessibleDescription = "Mínimo de 3 caracteres.";
        else      txKey.AccessibleDescription  = "Mínimo de 4 caracteres. A entrada fica mascarada e não será salva.";
        ReflowLoginPage();
    }

    // HWID estável (placa+CPU+disco via hash). Enviado no /auth; servidor trava a key no 1º.
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformation(string root, System.Text.StringBuilder volName, uint volNameSize, out uint serial, out uint maxLen, out uint flags, System.Text.StringBuilder fs, uint fsSize);

    static string Hwid() {
        try {
            string vol = "";
            try {
                uint serial = 0, maxLen = 0, flags = 0;
                if (GetVolumeInformation(@"C:\", new System.Text.StringBuilder(256), 256, out serial, out maxLen, out flags, null, 0))
                    vol = serial.ToString("X8");
            } catch { }
            string raw = Environment.MachineName + "|" + vol + "|" + Environment.ProcessorCount + "|" + Environment.OSVersion.Version;
            using var s = System.Security.Cryptography.SHA256.Create();
            var h = s.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant()[..16];
        } catch { return ""; }
    }

    static string HwidShort() {
        try { var h = Hwid(); return h.Length >= 8 ? h.Substring(0, 8).ToUpperInvariant() : "—"; }
        catch { return "—"; }
    }

    // STUB OFFLINE documentado: valida localmente (user>=3, key>=4), sem rede/servidor.
    // Persiste somente user= em loader.cfg; a key nunca é gravada.
    // Auth servidor: POST {key} -> {ok, message, expires_at}. Fail-closed (sem rede = sem login).
    async System.Threading.Tasks.Task<(bool ok, string msg, long exp)> AuthAsync(string key) {
        var r = await PostAuth("/auth", "{\"key\":\"" + JsonEsc(key) + "\",\"hwid\":\"" + Hwid() + "\"}");
        return (r.ok, r.msg, r.exp);
    }

    async System.Threading.Tasks.Task<(bool ok, string key, long exp, string msg)> ActivateAsync(string code) {
        var r = await PostAuth("/activate", "{\"code\":\"" + JsonEsc(code) + "\"}");
        return (r.ok, r.key, r.exp, r.msg);
    }

    async System.Threading.Tasks.Task<(bool ok, string key, long exp, string msg)> TrialAsync() {
        var r = await PostAuth("/trial", "{}");
        return (r.ok, r.key, r.exp, r.msg);
    }

    static string JsonEsc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    async System.Threading.Tasks.Task<(bool ok, string key, long exp, string msg)> PostAuth(string path, string json) {
        try {
            if (string.IsNullOrWhiteSpace(ApiBase)) return (false, null, 0, "Servidor de auth não configurado.");
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            var body = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var r = await http.PostAsync(ApiBase.TrimEnd('/') + path, body);
            var t = await r.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(t);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
            string msg = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "") : "";
            string key = root.TryGetProperty("key", out var k) ? (k.GetString() ?? "") : null;
            long exp = 0;
            if (root.TryGetProperty("expires_at", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number) exp = e.GetInt64();
            return (ok, key, exp, msg);
        } catch (Exception ex) {
            Dbg("auth falhou: " + ex.Message);
            return (false, null, 0, "Sem conexão com o servidor.");
        }
    }

    void DoLogin() {
        var u = (txUser.Text ?? "").Trim();
        var k = (txKey.Text  ?? "").Trim();
        lbUserErr.Visible = u.Length < 3;
        lbKeyErr.Visible  = k.Length < 4;
        txUser.AccessibleDescription = lbUserErr.Visible ? "Erro: informe pelo menos 3 caracteres." : "Mínimo de 3 caracteres.";
        txKey.AccessibleDescription  = lbKeyErr.Visible  ? "Erro: informe pelo menos 4 caracteres." : "Mínimo de 4 caracteres. A entrada fica mascarada e não será salva.";
        ReflowLoginPage();
        if (lbUserErr.Visible || lbKeyErr.Visible) {
            if (lbUserErr.Visible) txUser.Focus(); else txKey.Focus();
            UpdateInputFrames();
            return;
        }

        // ── Animação de carregamento antes de avançar ─────────────────────────
        btLogin.Enabled   = false;
        btDiscord.Enabled = false;
        _loginDotsCount   = 0;
        _loginDotsTimer.Start();

        var loginTimer = new System.Windows.Forms.Timer { Interval = 560 };
        loginTimer.Tick += async (_, _) => {
            loginTimer.Stop(); loginTimer.Dispose();
            var (ok, msg, exp) = await AuthAsync(k);
            _loginDotsTimer.Stop();
            btLogin.Text      = "ENTRAR";
            btLogin.Enabled   = true;
            btDiscord.Enabled = true;
            if (!ok) {
                try { MessageBox.Show(this, msg, "DROPS", MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
                Log("login negado.");
                return;
            }
            savedUser  = u;
            sessionId  = Guid.NewGuid().ToString("N");
            sessionExp = DateTime.UtcNow.AddDays(SessionDays);
            lastOk     = DateTime.UtcNow;
            keyExp     = exp;
            keyTail    = k.Length >= 4 ? k.Substring(k.Length - 4) : "";
            SaveCfg();
            txKey.Clear();
            Log("login efetuado.");
            Dbg("login ok via servidor");
            ShowPage(1);
        };
        loginTimer.Start();
    }

    // ── pgProduto ─────────────────────────────────────────────────────────────
    void BuildProductPage() {
        pgProduto = new Panel { Left = 0, Top = 88, Width = 520, Height = 532, BackColor = Color.Transparent };
        prodCard  = new ProductCard() {
            Left = 16, Top = 8, Width = 488, Height = 120, Cursor = Cursors.Hand,
        };
        prodCard.Paint += (_, e) => {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var bd = prodSelected ? LUi.Acc : LUi.Line;
            using var pn   = new Pen(bd, prodSelected ? 2f : 1f);
            using var path = LUi.Round(new Rectangle(0, 0, prodCard.Width - 1, prodCard.Height - 1), 8);
            g.DrawPath(pn, path);
            using var shine     = new Pen(Color.FromArgb(75, Color.White));
            using var shinePath = LUi.Round(new Rectangle(2, 2, prodCard.Width - 5, prodCard.Height - 5), 7);
            g.DrawPath(shine, shinePath);
        };
        prodTitle = new Label {
            Text = "Paladins · overlay externo · v" + VerFallback,
            Left = 14, Top = 14, Width = 460, Height = 26,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        var s = new Label {
            Text = "Disponível", Left = 14, Top = 44, Width = 460, Height = 22,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Success, BackColor = Color.Transparent,
        };
        var d = new Label {
            Text = "Overlay externo + watchdog do jogo. Clique para selecionar.",
            Left = 14, Top = 70, Width = 460, Height = 40,
            Font = new Font("Segoe UI", 9f), ForeColor = LUi.Txt, BackColor = Color.Transparent,
        };
        foreach (var c in new Control[] { prodTitle, s, d }) { c.Click += (_, _) => ToggleProduct(); c.Cursor = Cursors.Hand; }
        prodCard.Controls.AddRange(new Control[] { prodTitle, s, d });
        prodCard.Click += (_, _) => ToggleProduct();
        pgProduto.Controls.Add(prodCard);

        btContinue = new Button {
            Text = "CONTINUAR", Left = 16, Top = 142, Width = 488, Height = 48,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            BackColor = Color.FromArgb(60, 60, 68), ForeColor = LUi.Txt,
            FlatStyle = FlatStyle.Flat, Enabled = false,
        };
        btContinue.FlatAppearance.BorderSize = 0;
        btContinue.Click += (_, _) => { if (prodSelected) ShowPage(2); };

        var btBack = new Button {
            Text = "VOLTAR", Left = 16, Top = 198, Width = 488, Height = 36,
            Font = new Font("Segoe UI", 9f), BackColor = Color.FromArgb(30, 30, 36),
            ForeColor = LUi.Txt, FlatStyle = FlatStyle.Flat,
        };
        btBack.FlatAppearance.BorderSize = 0;
        btBack.Click += (_, _) => ShowPage(0);

        var btLogout = new Label {
            Text = "Sair da conta", Left = 16, Top = 242, Width = 488, Height = 30,
            Font = new Font("Segoe UI", 8.5f), BackColor = Color.Transparent, ForeColor = LUi.Dim,
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, AccessibleName = "Sair da conta",
        };
        btLogout.MouseEnter += (_, _) => btLogout.ForeColor = LUi.Txt;
        btLogout.MouseLeave += (_, _) => btLogout.ForeColor = LUi.Dim;
        btLogout.Click += (_, _) => {
            sessionId = ""; sessionExp = DateTime.MinValue; lastOk = DateTime.MinValue; keyExp = 0; keyTail = ""; SaveCfg();
            txKey.Clear(); ShowPage(0);
        };
        pgProduto.Controls.Add(btContinue);
        pgProduto.Controls.Add(btBack);
        pgProduto.Controls.Add(btLogout);

        lbAccUser = MkAccLabel(280);
        lbAccKey = MkAccLabel(300);
        lbAccExp = MkAccLabel(320);
        lbAccHwid = MkAccLabel(364);
        lbAccRenew = new Label {
            Text = "Renovar no Discord", Left = 16, Top = 342, Width = 488, Height = 22,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Underline), BackColor = Color.Transparent, ForeColor = LUi.Dim,
            TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
        };
        lbAccRenew.MouseEnter += (_, _) => lbAccRenew.ForeColor = LUi.Acc;
        lbAccRenew.MouseLeave += (_, _) => lbAccRenew.ForeColor = LUi.Dim;
        lbAccRenew.Click += (_, _) => OpenDiscord();
        pgProduto.Controls.Add(lbAccUser);
        pgProduto.Controls.Add(lbAccKey);
        pgProduto.Controls.Add(lbAccExp);
        pgProduto.Controls.Add(lbAccRenew);
        pgProduto.Controls.Add(lbAccHwid);
    }

    static Label MkAccLabel(int y) => new Label {
        Text = "—", Left = 20, Top = y, Width = 480, Height = 20,
        Font = new Font("Segoe UI", 8.5f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
    };

    void RefreshAccount() {
        try {
            lbAccUser.Text = "Conta: " + (string.IsNullOrEmpty(savedUser) ? "—" : savedUser);
            lbAccKey.Text = "Key: ••••-" + (string.IsNullOrEmpty(keyTail) ? "••••" : keyTail);
            if (keyExp > 0) {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(keyExp).ToLocalTime();
                int days = (int)Math.Ceiling((dt - DateTimeOffset.Now).TotalDays);
                lbAccExp.Text = days < 0 ? "Expirada — renove no Discord."
                    : $"Válida até {dt:dd/MM/yyyy} ({days} dias)";
                lbAccExp.ForeColor = days < 0 ? LUi.Red : LUi.Dim;
            } else { lbAccExp.Text = "Validade: —"; lbAccExp.ForeColor = LUi.Dim; }
            lbAccHwid.Text = "PC: " + HwidShort();
        } catch { }
    }

    void OpenDiscord() {
        try {
            if (string.IsNullOrWhiteSpace(DiscordUrl)) {
                MessageBox.Show(this, "Convite do Discord ainda não configurado.\nCole o link em DiscordUrl no código.",
                    "Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = DiscordUrl, UseShellExecute = true });
        } catch (Exception ex) { Dbg("ERRO ao abrir Discord: " + ex); }
    }

    void ToggleProduct() {
        prodSelected = !prodSelected;
        btContinue.Enabled   = prodSelected;
        btContinue.BackColor = prodSelected ? LUi.Acc : Color.FromArgb(60, 60, 68);
        btContinue.ForeColor = prodSelected ? LUi.Bg  : LUi.Txt;
        try { prodCard.Invalidate(); } catch { }
    }

    // ── pgStart ───────────────────────────────────────────────────────────────
    void BuildStartPage() {
        pgStart = new Panel { Left = 0, Top = 88, Width = 520, Height = 532, BackColor = Color.Transparent };
        banner  = new Banner(VerFallback) { Left = 16, Top = 0, Width = 488 };
        pgStart.Controls.Add(banner);

        var card = new Card("STATUS") { Left = 16, Top = 90, Width = 488, Height = 100 };
        var lbGameTitle    = MkStatusTitle("JOGO",    20);
        var lbOverlayTitle = MkStatusTitle("OVERLAY", 44);
        var lbVerTitle     = MkStatusTitle("VERSÃO",  68);
        lbGameDot    = MkStatusDot(21);
        lbOverlayDot = MkStatusDot(45);
        lbGame    = MkStatusValue(20);
        lbOverlay = MkStatusValue(44);
        lbVer     = MkStatusValue(68);
        card.Controls.AddRange(new Control[] {
            lbGameTitle, lbGameDot, lbGame,
            lbOverlayTitle, lbOverlayDot, lbOverlay,
            lbVerTitle, lbVer,
        });
        pgStart.Controls.Add(card);

        btStart = new Button {
            Text = "INICIAR", Left = 16, Top = 196, Width = 488, Height = 48,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            BackColor = LUi.Acc, ForeColor = LUi.Bg, FlatStyle = FlatStyle.Flat,
        };
        btStart.FlatAppearance.BorderSize = 0;
        btStart.Click += (_, _) => ToggleChild();
        pgStart.Controls.Add(btStart);

        pbSeq = new SeqBar { Left = 16, Top = 250, Width = 488, Height = 12, Maximum = 4, Value = 0 };
        lbSeq = new Label {
            Text = "", Left = 20, Top = 264, Width = 480, Height = 18,
            Font = new Font("Segoe UI", 8.5f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
        };
        pgStart.Controls.Add(pbSeq);
        pgStart.Controls.Add(lbSeq);

        lbErr = new Label {
            Text = "", Left = 20, Top = 284, Width = 480, Height = 22,
            Font = new Font("Segoe UI", 8.5f), ForeColor = LUi.Red, BackColor = Color.Transparent,
        };
        pgStart.Controls.Add(lbErr);

        var btBack = new Button {
            Text = "VOLTAR", Left = 16, Top = 312, Width = 488, Height = 30,
            Font = new Font("Segoe UI", 9f), BackColor = Color.FromArgb(30, 30, 36),
            ForeColor = LUi.Txt, FlatStyle = FlatStyle.Flat,
        };
        btBack.FlatAppearance.BorderSize = 0;
        btBack.Click += (_, _) => ShowPage(1);
        pgStart.Controls.Add(btBack);

        var logTitle = new Label {
            Text = "LOG", Left = 20, Top = 346, Width = 100, Height = 18,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = LUi.Acc,
            BackColor = Color.Transparent, AccessibleName = "Log do launcher",
        };
        pgStart.Controls.Add(logTitle);

        log = new LogConsole { Left = 16, Top = 366, Width = 488, Height = 164 };
        pgStart.Controls.Add(log);
    }

    // ── Helpers de status ─────────────────────────────────────────────────────
    static Label MkStatusTitle(string text, int y) => new Label {
        Text = text, Left = 14, Top = y, Width = 70, Height = 20,
        Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = LUi.Dim, BackColor = Color.Transparent,
    };
    static Label MkStatusDot(int y) => new Label {
        Text = "\u25CF", Left = 84, Top = y, Width = 16, Height = 20,
        Font = new Font("Segoe UI", 8f), ForeColor = LUi.Dim, BackColor = Color.Transparent,
    };
    static Label MkStatusValue(int y) => new Label {
        Text = "\u2014", Left = 104, Top = y, Width = 360, Height = 20,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Txt, BackColor = Color.Transparent,
    };
    static CheckBox MkCheck(string text, int y, bool v) => new CheckBox {
        Text = text, Left = 22, Top = y, Width = 470, Height = 22,
        Font = new Font("Segoe UI", 9f), ForeColor = LUi.Txt, BackColor = Color.Transparent, Checked = v,
    };

    static string Base() => Application.StartupPath;

    // ── Resolução de overlay e versão ─────────────────────────────────────────
    static string ResolveOverlay() {
        try {
            var dir = new DirectoryInfo(Base());
            string[] rels = {
                "overlay.exe",
                Path.Combine("esp", "ext", "publish", "overlay.exe"),
                Path.Combine("esp", "dist", "helia-3.1.0", "overlay.exe"),
            };
            for (int i = 0; i < 6 && dir != null; i++) {
                foreach (var rel in rels) {
                    try { var full = Path.Combine(dir.FullName, rel); if (File.Exists(full)) return full; } catch { }
                }
                dir = dir.Parent;
            }
        } catch { }
        return null;
    }

    static string ResolveVersion(string ovPath) {
        string[] cands = {
            ovPath != null ? Path.Combine(Path.GetDirectoryName(ovPath) ?? "", "version.txt") : null,
            Path.Combine(Base(), "version.txt"),
        };
        foreach (var c in cands) {
            try {
                if (c != null && File.Exists(c)) {
                    var t = File.ReadAllText(c).Trim();
                    if (t.Length > 0 && t.Length < 32) return t;
                }
            } catch { }
        }
        return VerFallback;
    }

    static Process FindGame(out string name, out int pid) {
        foreach (var n in GameNames) {
            try {
                var ps = Process.GetProcessesByName(n);
                if (ps.Length > 0) { name = ps[0].ProcessName; pid = ps[0].Id; return ps[0]; }
            } catch { }
        }
        name = null; pid = 0; return null;
    }

    bool ChildAlive() { try { return child != null && !child.HasExited; } catch { return false; } }

    // ── Controle do overlay ───────────────────────────────────────────────────
    void ToggleChild() {
        if (ChildAlive()) { StopChild("manual"); return; }
        lbErr.Text = "";
        seqIdx = 0; seqWait = 0; pbSeq.Value = 0;
        btStart.Enabled = false;
        seqTimer.Start();
        SeqTick();
    }

    void SeqTick() {
        try {
            if (seqIdx == 0) {
                lbSeq.Text = "Validando arquivos…"; pbSeq.Value = 1;
                overlayPath = ResolveOverlay();
                if (overlayPath == null) {
                    seqTimer.Stop(); btStart.Enabled = true;
                    lbErr.Text  = "overlay.exe nao encontrado ao lado do launcher.";
                    lbSeq.Text  = "Falha na validação.";
                    Log("overlay nao localizado."); Dbg("overlay null nos caminhos padrao"); return;
                }
                versionShown   = ResolveVersion(overlayPath);
                prodTitle.Text = "Paladins \u00B7 overlay externo  \u2022  v" + versionShown;
                try { banner?.SetVer(versionShown); } catch { }
                Log("arquivos validados."); Dbg("validacao ok: " + overlayPath);
                seqIdx = 1;
            } else if (seqIdx == 1) {
                var g = FindGame(out var gname, out var gpid);
                bool on = g != null;
                try { g?.Dispose(); } catch { }
                if (!on) {
                    // Sem jogo: espera (até 60s) em vez de subir overlay fadado a morrer.
                    seqWait++;
                    if (seqWait == 1) Log("aguardando Paladins.exe…");
                    lbSeq.Text = $"ESPERANDO PALADINS.EXE ({seqWait * 300 / 1000}s)";
                    pbSeq.Value = 2;
                    if (seqWait > 200) {
                        seqTimer.Stop(); btStart.Enabled = true;
                        lbErr.Text = "Tempo esgotado: abra o Paladins e clique INICIAR.";
                        lbSeq.Text = "ESPERANDO PALADINS.EXE";
                    }
                    RefreshStatus();
                    return;
                }
                lbSeq.Text = "Jogo detectado. Iniciando…"; pbSeq.Value = 2;
                Log("jogo detectado.");
                Dbg($"jogo detectado ({gname} PID {gpid})");
                RefreshStatus(); seqIdx = 2;
            } else if (seqIdx == 2) {
                lbSeq.Text = "Iniciando overlay…"; pbSeq.Value = 3;
                StartChild("manual"); seqIdx = 3;
            } else {
                seqTimer.Stop(); btStart.Enabled = true; pbSeq.Value = 4;
                lbSeq.Text = ChildAlive() ? "Pronto." : "Falha ao iniciar (ver log).";
            }
        } catch (Exception ex) {
            try { seqTimer.Stop(); } catch { }
            btStart.Enabled = true;
            lbErr.Text = "Falha na sequência de início.";
            Log("falha na sequência (detalhe em loader_debug.txt).");
            Dbg("ERRO na sequência: " + ex);
        }
    }

    void StartChild(string why) {
        lbErr.Text = "";
        if (ChildAlive()) { Dbg("start ignorado: filho já vivo"); return; }
        if ((DateTime.UtcNow - lastStart).TotalSeconds < 15) { Dbg("start ignorado: rate-limit 15s"); return; }
        if (!ValidSession()) RefreshSession();
        bool started = false;
        try {
            var psi = new ProcessStartInfo {
                FileName         = overlayPath,
                Arguments        = "--session " + sessionId,
                WorkingDirectory = Path.GetDirectoryName(overlayPath) ?? Base(),
                UseShellExecute  = true,
            };
            var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("Process.Start retornou nulo.");
            int startPid = 0; try { startPid = p.Id; } catch { }
            child = p;
            child.EnableRaisingEvents = true;
            child.Exited += (_, _) => BeginInvoke(new Action(() => {
                Log("overlay encerrado."); Dbg("overlay filho encerrou (PID " + startPid + ")");
                RefreshStatus();
            }));
            Log("overlay iniciado.");
            Dbg((why == "auto" ? "auto: jogo detectado, iniciando overlay (PID " : "overlay iniciado (PID ") + startPid + ").");
            started = true;
            lastStart = DateTime.UtcNow;
        } catch (Exception ex) {
            lbErr.Text = "Falha ao iniciar overlay.";
            Log("falha ao iniciar (detalhe em loader_debug.txt)."); Dbg("ERRO ao iniciar overlay: " + ex);
        }
        RefreshStatus();
        if (started) ByeAndExit();
    }

    // Despedida curta e fecha: loader é descartável (overlay se vira sozinho).
    // Não-modal + Environment.Exit: Application.Exit após ShowDialog se mostrou frágil.
    void ByeAndExit() {
        Dbg("bye: exibindo despedida e encerrando loader");
        try {
            var f = new Form {
                Text = "DROPS", Width = 320, Height = 92,
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterScreen,
                BackColor = LUi.Bg, TopMost = true, ShowInTaskbar = false,
            };
            using var rg = LUi.Round(new Rectangle(0, 0, f.Width - 1, f.Height - 1), 10);
            f.Region = new Region(rg);
            var bar = new Label { Text = "DROPS", Left = 16, Top = 10, Width = 288, Height = 20,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = LUi.Acc, BackColor = Color.Transparent };
            var msg = new Label { Text = "Overlay ativo — bom jogo!", Left = 16, Top = 34, Width = 288, Height = 44,
                Font = new Font("Segoe UI", 10f), ForeColor = LUi.Txt, BackColor = Color.Transparent };
            f.Controls.Add(bar); f.Controls.Add(msg);
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (_, _) => {
                try { t.Stop(); } catch { }
                try { f.Close(); } catch { }
                try { Dbg("bye: Environment.Exit"); } catch { }
                try { Environment.Exit(0); } catch { }
            };
            t.Start();
            f.Show();
        } catch { try { Environment.Exit(0); } catch { } }
    }

    void StopChild(string why) {
        try {
            if (!ChildAlive()) { child = null; RefreshStatus(); return; }
            int pid = 0; try { pid = child.Id; } catch { }
            try { child.Kill(); } catch { }
            try { child.WaitForExit(3000); } catch { }
            Log(why == "jogo-fechou" ? "jogo fechou, overlay encerrado." : "overlay encerrado.");
            Dbg($"overlay filho encerrado (PID {pid}, why={why})");
        } catch (Exception ex) {
            lbErr.Text = "Falha ao parar overlay.";
            Log("falha ao parar (detalhe em loader_debug.txt)."); Dbg("ERRO ao parar overlay: " + ex);
        } finally {
            try { child?.Dispose(); } catch { }
            child = null; RefreshStatus();
        }
    }

    void Watchdog() {
        try {
            var g = FindGame(out var gname, out var gpid);
            bool gameOn = g != null;
            try { g?.Dispose(); } catch { }
            bool ovOn = ChildAlive();
            if (gameOn && !ovOn && auto && !seqTimer.Enabled && ValidSession()) {
                overlayPath = ResolveOverlay();
                if (overlayPath != null) StartChild("auto");
                else lbErr.Text = "overlay.exe nao encontrado ao lado do launcher.";
            } else if (!gameOn && ovOn && closeWithGame) {
                StopChild("jogo-fechou"); return;
            }
            RefreshStatus(gameOn, gname, gpid, ovOn);
        } catch { }
    }

    void RefreshStatus() {
        var g = FindGame(out var gname, out var gpid);
        bool gameOn = g != null;
        try { g?.Dispose(); } catch { }
        RefreshStatus(gameOn, gname, gpid, ChildAlive());
    }

    void RefreshStatus(bool gameOn, string gname, int gpid, bool ovOn) {
        if (gameOn) {
            lbGame.Text = $"{gname}  \u00B7  PID {gpid}";
            lbGame.ForeColor = LUi.Success; lbGameDot.ForeColor = LUi.Success;
        } else {
            lbGame.Text = "ESPERANDO PALADINS.EXE";
            lbGame.ForeColor = LUi.Acc; lbGameDot.ForeColor = LUi.Acc;
        }
        if (ovOn) {
            int pid = 0; try { pid = child.Id; } catch { }
            lbOverlay.Text = $"Em execução  \u00B7  PID {pid}";
            lbOverlay.ForeColor = LUi.Success; lbOverlayDot.ForeColor = LUi.Success;
        } else {
            lbOverlay.Text = "Parado";
            lbOverlay.ForeColor = LUi.Dim; lbOverlayDot.ForeColor = LUi.Dim;
        }
        lbVer.Text = versionShown; lbVer.ForeColor = LUi.Txt;
        if (!seqTimer.Enabled && !ovOn && !gameOn &&
            (lbSeq.Text.StartsWith("ESPERANDO") || lbSeq.Text.StartsWith("Aguardando"))) {
            lbSeq.Text = "";
            try { pbSeq.Value = 0; } catch { }
        }
        btStart.Text      = ovOn ? "PARAR" : "INICIAR";
        btStart.BackColor = ovOn ? Color.FromArgb(60, 60, 68) : LUi.Acc;
        btStart.ForeColor = ovOn ? LUi.Txt : Color.FromArgb(10, 14, 12);
    }

    void Log(string msg) {
        try { if (log == null) return; log.AppendEntry(msg, DateTime.Now); } catch { }
    }

    static string DbgPath() => Path.Combine(Base(), "loader_debug.txt");
    static void Dbg(string msg) {
        try {
            string p = DbgPath();
            try {
                var fi = new FileInfo(p);
                if (fi.Exists && fi.Length > 512 * 1024) {
                    string old = p + ".old";
                    try { File.Delete(old); } catch { }
                    try { File.Move(p, old); } catch { }
                }
            } catch { }
            File.AppendAllText(p, $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        } catch { }
    }

    static string CfgPath() => Path.Combine(Base(), "loader.cfg");

    bool FreshLogin() {
        try { return lastOk != DateTime.MinValue && (DateTime.UtcNow - lastOk.ToUniversalTime()).TotalHours < 24; }
        catch { return false; }
    }

    void LoadCfg() {
        auto = true; closeWithGame = true; savedUser = ""; sessionId = ""; sessionExp = DateTime.MinValue;
        lastOk = DateTime.MinValue; keyExp = 0; keyTail = "";
        try {
            if (!File.Exists(CfgPath())) return;
            foreach (var ln in File.ReadAllLines(CfgPath())) {
                int eq = ln.IndexOf('='); if (eq <= 0) continue;
                var k = ln[..eq].Trim(); var v = ln[(eq + 1)..].Trim();
                if (k == "user") savedUser = v;
                else if (k == "session" && Guid.TryParse(v, out _)) sessionId = v;
                else if (k == "expires" && long.TryParse(v, out var t)) {
                    try { sessionExp = new DateTime(t, DateTimeKind.Utc); } catch { }
                }
                else if (k == "last_ok" && long.TryParse(v, out var lo)) {
                    try { lastOk = new DateTime(lo, DateTimeKind.Utc).ToLocalTime(); } catch { }
                }
                else if (k == "key_exp" && long.TryParse(v, out var ke)) keyExp = ke;
            }
        } catch { }
    }

    void SaveCfg() {
        try {
            File.WriteAllLines(CfgPath(), new[] {
                $"user={savedUser}",
                $"auto={(auto ? 1 : 0)}",
                $"closeWithGame={(closeWithGame ? 1 : 0)}",
                $"session={sessionId}",
                $"expires={sessionExp.ToUniversalTime().Ticks}",
                $"last_ok={lastOk.ToUniversalTime().Ticks}",
                $"key_exp={keyExp}",
            });
        } catch { }
    }

    // Mutex vivo = loader aberto (single-instance de verdade; 2ª cópia sai sozinha).
    static string ShaOf(byte[] d) {
        using var s = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(s.ComputeHash(d)).Replace("-", "").ToLowerInvariant();
    }
    static string ShaOf(string path) {
        try { return ShaOf(System.IO.File.ReadAllBytes(path)); } catch { return ""; }
    }

    // Updater GitHub: manifest → compara → baixa mudados com sha.
    // Versão local do loader persiste em loader.version (igual version.txt do overlay).
    static string LoaderVersionFile() => System.IO.Path.Combine(Base(), "loader.version");

    static string LocalLoaderVersion() {
        try {
            string vf = LoaderVersionFile();
            if (System.IO.File.Exists(vf)) {
                var t = System.IO.File.ReadAllText(vf).Trim();
                if (t.Length > 0 && t.Length < 32) return t;
            }
        } catch { }
        return LoaderVersion;
    }

    async void CheckUpdate() {
        try {
            // Higiene do updater: limpa restos de updates anteriores.
            try {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drops-upd");
                if (System.IO.Directory.Exists(tmp))
                    foreach (var f in System.IO.Directory.GetFiles(tmp, "*.new"))
                        try { System.IO.File.Delete(f); } catch { }
                string bak = System.IO.Path.Combine(Base(), "loader.exe.bak");
                if (System.IO.File.Exists(bak)) try { System.IO.File.Delete(bak); } catch { }
            } catch { }
            string ovDir = null;
            try { if (!string.IsNullOrEmpty(overlayPath)) ovDir = System.IO.Path.GetDirectoryName(overlayPath); } catch { }
            string localOvVer = versionShown;
            try {
                string vt = System.IO.Path.Combine(ovDir ?? Base(), "version.txt");
                if (System.IO.File.Exists(vt)) {
                    var t = System.IO.File.ReadAllText(vt).Trim();
                    if (t.Length > 0) localOvVer = t;
                }
            } catch { }
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            string man = await http.GetStringAsync(UpdateBase + "/manifest.json");
            // Copia tudo pra memória antes de descartar o doc (o Offer sobrevive ao using).
            string ovVer, ldVer;
            var loaderFiles = new System.Collections.Generic.List<(string rel, string sha, long size)>();
            var overlayFiles = new System.Collections.Generic.List<(string rel, string sub, string sha)>();
            using (var doc = System.Text.Json.JsonDocument.Parse(man)) {
                var root = doc.RootElement;
                ovVer = root.TryGetProperty("overlay_version", out var o) ? o.GetString() : null;
                ldVer = root.TryGetProperty("loader_version", out var l) ? l.GetString() : null;
                foreach (var f in root.GetProperty("files").EnumerateArray()) {
                    string rel = f.GetProperty("path").GetString();
                    string sha = f.GetProperty("sha256").GetString();
                    long size = 0;
                    try {
                        if (f.TryGetProperty("size", out var sz) && sz.ValueKind == System.Text.Json.JsonValueKind.Number)
                            size = sz.GetInt64();
                    } catch { }
                    if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(sha)) continue;
                    if (rel.StartsWith("loader/")) loaderFiles.Add((rel, sha, size));
                    else if (rel.StartsWith("overlay/")) overlayFiles.Add((rel, rel.Substring("overlay/".Length), sha));
                }
            }
            if (!string.IsNullOrEmpty(ovVer) && ovVer != localOvVer && ovDir != null)
                await UpdateFilesAsync(http, overlayFiles, ovDir);
            else
                Dbg("overlay já atualizado (" + localOvVer + ")");
            if (!string.IsNullOrEmpty(ldVer) && ldVer != LocalLoaderVersion())
                await OfferLoaderUpdate(http, loaderFiles, ldVer);
            else
                Dbg("loader já atualizado (" + LocalLoaderVersion() + ")");
        } catch (Exception ex) { Dbg("update check falhou: " + ex.GetType().Name + ": " + ex.Message); }
    }

    async System.Threading.Tasks.Task UpdateFilesAsync(System.Net.Http.HttpClient http, System.Collections.Generic.List<(string rel, string sub, string sha)> files, string dir) {
        try {
            foreach (var (rel, sub, want) in files) {
                string dest = System.IO.Path.Combine(dir, sub.Replace('/', System.IO.Path.DirectorySeparatorChar));
                try {
                    if (System.IO.File.Exists(dest) && ShaOf(dest) == want) continue;
                    byte[] data = await http.GetByteArrayAsync(UpdateBase + "/" + rel);
                    if (ShaOf(data) != want) { Dbg("sha mismatch: " + rel); continue; }
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    System.IO.File.WriteAllBytes(dest, data);
                    Log("atualizado: " + sub);
                } catch (Exception ex) { Dbg("update arq falhou " + rel + ": " + ex.GetType().Name + ": " + ex.Message); }
            }
        } catch (Exception ex) { Dbg("UpdateFiles: " + ex.Message); }
    }

    async System.Threading.Tasks.Task OfferLoaderUpdate(System.Net.Http.HttpClient http, System.Collections.Generic.List<(string rel, string sha, long size)> files, string ver) {
        try {
            long size = files.Count > 0 ? files[0].size : 0;
            bool go = await AskUpdateAsync(ver, size);
            if (!go) { Dbg("update loader recusado (" + ver + ")"); return; }
            if (files.Count == 0) { UpdFail("Nada para baixar."); return; }
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drops-upd");
            System.IO.Directory.CreateDirectory(tmp);
            var staged = new System.Collections.Generic.List<(string tmpPath, string sub)>();
            foreach (var (rel, want, sz) in files) {
                string sub = rel.Substring("loader/".Length);
                try {
                    UpdStatus("Conectando...");
                    using var resp = await http.GetAsync(UpdateBase + "/" + rel, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    long total = sz > 0 ? sz : (resp.Content.Headers.ContentLength ?? 0);
                    using var src = await resp.Content.ReadAsStreamAsync();
                    using var ms = new System.IO.MemoryStream();
                    var buf = new byte[8192];
                    long got = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0) {
                        ms.Write(buf, 0, n); got += n;
                        if (total > 0) {
                            int pct = (int)(got * 100 / total);
                            if (pct > 100) pct = 100;
                            UpdStatus($"Baixando {sub}... {pct}%", pct);
                        }
                    }
                    byte[] data = ms.ToArray();
                    UpdStatus("Verificando integridade...");
                    if (ShaOf(data) != want) { UpdFail("Falha na verificação. Tente mais tarde."); return; }
                    string t = System.IO.Path.Combine(tmp, sub + ".new");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(t));
                    System.IO.File.WriteAllBytes(t, data);
                    staged.Add((t, sub));
                    UpdStatus("Baixado.", 100);
                    Dbg("baixado ok: " + rel);
                } catch (Exception ex) { UpdFail("Falha no download (" + ex.GetType().Name + ")."); return; }
            }
            // O exe em uso fica travado: o lote tenta renomear até conseguir (só consegue
            // depois que este processo sai), aí troca os arquivos, grava loader.version e abre o novo.
            var lines = new System.Collections.Generic.List<string> { "@echo off", ":wait", "timeout /t 1 /nobreak >nul" };
            lines.Add($"ren \"{System.IO.Path.Combine(Base(), "loader.exe")}\" \"loader.tmp\" 2>nul");
            lines.Add("if errorlevel 1 goto wait");
            lines.Add($"del \"{System.IO.Path.Combine(Base(), "loader.tmp")}\"");
            foreach (var (t, sub) in staged)
                lines.Add($"move /y \"{t}\" \"{System.IO.Path.Combine(Base(), sub)}\"");
            lines.Add($"echo {ver}>\"{LoaderVersionFile()}\"");
            lines.Add($"start \"\" \"{System.IO.Path.Combine(Base(), "loader.exe")}\"");
            lines.Add("del \"%~f0\"");
            string bat = System.IO.Path.Combine(tmp, "upd.bat");
            System.IO.File.WriteAllLines(bat, lines);
            UpdStatus("Aplicando e reiniciando...");
            await System.Threading.Tasks.Task.Delay(700);
            Dbg("aplicando update " + ver + ", reiniciando...");
            Process.Start(new ProcessStartInfo { FileName = bat, WorkingDirectory = tmp, UseShellExecute = true });
            try { Application.Exit(); } catch { }
        } catch (Exception ex) { Dbg("OfferLoaderUpdate: " + ex.GetType().Name + ": " + ex.Message); }
    }

    void HoldGate() {
        try {
            gate = new System.Threading.Mutex(true, GateName, out bool fresh);
            if (!fresh) {
                try { gate.Dispose(); } catch { }
                gate = null;
                MessageBox.Show("O loader já está aberto.", "DROPS",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.Exit(0);
            }
        } catch (System.Threading.AbandonedMutexException) { }
        catch { gate = null; }
    }

    bool ValidSession() {
        try {
            if (string.IsNullOrEmpty(sessionId) || !Guid.TryParse(sessionId, out _)) return false;
            return DateTime.UtcNow < sessionExp;
        } catch { return false; }
    }

    void RefreshSession() {
        sessionId  = Guid.NewGuid().ToString("N");
        sessionExp = DateTime.UtcNow.AddDays(SessionDays);
        SaveCfg();
    }

    [STAThread]
    static void Main() {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => {
            try { Dbg("FATAL-UI: " + e.Exception.GetType().Name + ": " + e.Exception.Message); } catch { }
            try { MessageBox.Show("Algo inesperado aconteceu. O loader vai fechar.\nDetalhe em loader_debug.txt.", "DROPS",
                MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            try { Dbg("FATAL: " + (e.ExceptionObject?.ToString() ?? "?")); } catch { }
        };
        Application.Run(new HeliaLoader());
    }
}
