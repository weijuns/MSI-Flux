// MSIFlux OsdToast — MSI 风格屏幕悬浮提示
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MSIFlux.GUI.Helpers;

internal static class DrawingUtils
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(this Graphics g, Brush b, Rectangle r, int radius)
    {
        using var path = RoundedRect(r, radius);
        g.FillPath(b, path);
    }
}

public class OsdToastForm : Form
{
    private static OsdToastForm? _instance;
    private static readonly object _lock = new();
    private string _text = "";
    private System.Windows.Forms.Timer _hideTimer;
    private float _opacity = 0f;
    private System.Windows.Forms.Timer _fadeTimer;
    private bool _fadingIn = true;

    public OsdToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        Opacity = 0;

        _hideTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _hideTimer.Tick += (_, _) => StartFadeOut();

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _fadeTimer.Tick += FadeTick;
    }

    private void FadeTick(object? _, EventArgs e)
    {
        if (_fadingIn)
        {
            _opacity += 0.08f;
            if (_opacity >= 1f) { _opacity = 1f; _fadeTimer.Stop(); _hideTimer.Start(); }
        }
        else
        {
            _opacity -= 0.06f;
            if (_opacity <= 0) { _opacity = 0; _fadeTimer.Stop(); Hide(); }
        }
        Opacity = _opacity;
    }

    private void StartFadeOut()
    {
        _hideTimer.Stop();
        _fadingIn = false;
        _fadeTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE | TOOLWINDOW | TRANSPARENT
            return cp;
        }
    }

    public static void ShowToast(string text, Image? icon = null)
    {
        try
        {
            if (Application.OpenForms.Count > 0)
            {
                var main = Application.OpenForms[0];
                if (main.IsHandleCreated && main.InvokeRequired)
                {
                    main.BeginInvoke(() => ShowToastInternal(text));
                    return;
                }
            }
            ShowToastInternal(text);
        }
        catch { }
    }

    private static void ShowToastInternal(string text)
    {
        lock (_lock)
        {
            if (_instance == null || _instance.IsDisposed)
                _instance = new OsdToastForm();
            _instance.RunToast(text);
        }
    }

    public void RunToast(string text)
    {
        _fadeTimer.Stop();
        _hideTimer.Stop();

        _text = text;
        _opacity = 0f;
        _fadingIn = true;

        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

        // 紧凑尺寸: 自动适配文字宽度
        using var g = CreateGraphics();
        using var f = new Font("Microsoft YaHei UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        var textSize = g.MeasureString(_text, f);
        Width = Math.Max(180, Math.Min(360, (int)textSize.Width + 60));
        Height = 52;

        Location = new Point(
            (screen.Bounds.Width - Width) / 2,
            screen.Bounds.Height - 200 - Height
        );

        if (!Visible) Show();
        Invalidate();
        _fadeTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width, Height);

        // MSI 风格: 深色半透明圆角背景
        using var bg = new SolidBrush(Color.FromArgb(225, 20, 20, 24));
        e.Graphics.FillRoundedRectangle(bg, rect, 20);

        // 极淡边框
        using var border = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
        using var bp = DrawingUtils.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 20);
        e.Graphics.DrawPath(border, bp);

        // 居中文字
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center };
        using var font = new Font("Microsoft YaHei UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var tb = new SolidBrush(Color.FromArgb(240, 240, 245));
        e.Graphics.DrawString(_text, font, tb, new RectangleF(0, 2, Width, Height), sf);
    }

    protected override bool ShowWithoutActivation => true;
}
