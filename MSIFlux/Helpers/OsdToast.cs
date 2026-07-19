// This file is part of MSIFlux.
// OsdToast: 高颜值屏幕悬浮提示 (OSD Toast)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
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

public class OsdToastForm : OSDNativeForm
{
    private static OsdToastForm? _instance;
    private static readonly object _lock = new();

    private string _toastText = "";
    private Image? _toastIcon;
    private System.Threading.Timer? _hideTimer;

    public static void ShowToast(string text, Image? icon = null)
    {
        try
        {
            lock (_lock)
            {
                _instance ??= new OsdToastForm();
                _instance.RunToast(text, icon);
            }
        }
        catch { }
    }

    public void RunToast(string text, Image? icon)
    {
        // 停止之前的隐藏计时器
        _hideTimer?.Dispose();

        _toastText = text;
        _toastIcon = icon;

        Screen screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];

        // 居中美观计算尺寸
        int contentWidth = 140 + (_toastIcon != null ? 40 : 0) + (_toastText.Length * 20);
        Width = Math.Max(220, Math.Min(360, contentWidth));
        Height = 65;

        X = (screen.Bounds.Width - Width) / 2;
        Y = screen.Bounds.Height - 240 - Height;

        Show();

        // 使用 Threading.Timer 确保 2秒后可靠隐藏，无需依附 WinForms 消息循环
        _hideTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                Hide();
            }
            catch { }
        }, null, 2000, Timeout.Infinite);
    }

    protected override void PerformPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // 圆角 16px 精致深色玻璃卡片 (RGB: 24, 24, 28, Alpha: 235)
        using (Brush bgBrush = new SolidBrush(Color.FromArgb(235, 24, 24, 28)))
        {
            e.Graphics.FillRoundedRectangle(bgBrush, Bound, 16);
        }

        // 微亮细腻边框
        using (Pen borderPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1.2f))
        {
            using GraphicsPath path = DrawingUtils.RoundedRect(new Rectangle(0, 0, Bound.Width - 1, Bound.Height - 1), 16);
            e.Graphics.DrawPath(borderPen, path);
        }

        int startX = 24;

        // 如果有图标，在左侧绘制高清图标
        if (_toastIcon != null)
        {
            int iconSize = 32;
            int iconY = (Bound.Height - iconSize) / 2;
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

        float textX = _toastIcon != null ? startX : Bound.Width / 2f;
        e.Graphics.DrawString(_toastText, font, textBrush, new PointF(textX, Bound.Height / 2f), sf);
    }
}
