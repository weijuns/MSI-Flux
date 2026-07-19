// This file is part of MSIFlux.
// OsdToast: 高颜值无焦点屏幕悬浮提示 (OSD Toast - 标准 WinForms 线程安全版本)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MSIFlux.GUI.Helpers;

internal static class DrawingUtils
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        Size size = new Size(diameter, diameter);
        Rectangle arc = new Rectangle(bounds.Location, size);
        GraphicsPath path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int cornerRadius)
    {
        using GraphicsPath path = RoundedRect(bounds, cornerRadius);
        graphics.FillPath(brush, path);
    }
}

public class OsdToastForm : Form
{
    private static OsdToastForm? _instance;
    private static readonly object _lock = new();

    private string _toastText = "";
    private Image? _toastIcon;
    private System.Windows.Forms.Timer _hideTimer;

    public OsdToastForm()
    {
        // 窗体样式配置
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black; // 黑色透明背景

        _hideTimer = new System.Windows.Forms.Timer
        {
            Interval = 2000
        };
        _hideTimer.Tick += HideTimer_Tick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (不夺取窗口与游戏焦点)
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (不在任务栏/Alt-Tab中显示)
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT (鼠标穿透)
            return cp;
        }
    }

    /// <summary>
    /// 静态线程安全调用入口
    /// </summary>
    public static void ShowToast(string text, Image? icon = null)
    {
        try
        {
            if (Application.OpenForms.Count > 0)
            {
                Form main = Application.OpenForms[0];
                if (main.IsHandleCreated && main.InvokeRequired)
                {
                    main.BeginInvoke(new Action(() => ShowToastInternal(text, icon)));
                    return;
                }
            }
            ShowToastInternal(text, icon);
        }
        catch { }
    }

    private static void ShowToastInternal(string text, Image? icon)
    {
        lock (_lock)
        {
            if (_instance == null || _instance.IsDisposed)
            {
                _instance = new OsdToastForm();
            }
            _instance.RunToast(text, icon);
        }
    }

    public void RunToast(string text, Image? icon)
    {
        _hideTimer.Stop();

        _toastText = text;
        _toastIcon = icon;

        Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

        int contentWidth = 140 + (_toastIcon != null ? 44 : 0) + (_toastText.Length * 20);
        Width = Math.Max(220, Math.Min(380, contentWidth));
        Height = 65;

        Location = new Point(
            (screen.Bounds.Width - Width) / 2,
            screen.Bounds.Height - 240 - Height
        );

        if (!Visible)
        {
            Show();
        }

        Invalidate();
        _hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        Rectangle rect = new Rectangle(0, 0, Width, Height);

        // 绘制圆角精致深色卡片背景 (RGB: 24, 24, 28)
        using (Brush bgBrush = new SolidBrush(Color.FromArgb(235, 24, 24, 28)))
        {
            e.Graphics.FillRoundedRectangle(bgBrush, rect, 16);
        }

        // 微亮细腻边框
        using (Pen borderPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1.2f))
        {
            using GraphicsPath path = DrawingUtils.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 16);
            e.Graphics.DrawPath(borderPen, path);
        }

        int startX = 24;

        // 如果有图标，在左侧绘制
        if (_toastIcon != null)
        {
            int iconSize = 32;
            int iconY = (Height - iconSize) / 2;
            e.Graphics.DrawImage(_toastIcon, new Rectangle(startX, iconY, iconSize, iconSize));
            startX += iconSize + 16;
        }

        // 绘制白色现代字体
        using StringFormat sf = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = _toastIcon != null ? StringAlignment.Near : StringAlignment.Center
        };

        using Font font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        using Brush textBrush = new SolidBrush(Color.FromArgb(245, 245, 248));

        float textX = _toastIcon != null ? startX : Width / 2f;
        e.Graphics.DrawString(_toastText, font, textBrush, new PointF(textX, Height / 2f), sf);
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        Hide();
    }

    protected override bool ShowWithoutActivation => true;
}
