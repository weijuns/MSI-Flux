// This file is part of MSIFlux.
// OSDBase: 无焦点半透明原生置顶窗口基类 (移植自 G-Helper)

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MSIFlux.GUI.Helpers;

public class OSDNativeForm : NativeWindow, IDisposable
{
    private bool _disposed;
    private byte _alpha = 230;
    private Size _size = new(350, 60);
    private Point _location = new(50, 50);

    protected virtual void PerformPaint(PaintEventArgs e) { }

    protected internal void Invalidate()
    {
        UpdateLayeredWindow();
    }

    private void UpdateLayeredWindow()
    {
        if (Handle == IntPtr.Zero) return;
        if (Size.Width <= 0 || Size.Height <= 0) return;

        using Bitmap bitmap = new(Size.Width, Size.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            Rectangle rect = new(0, 0, Size.Width, Size.Height);
            PerformPaint(new PaintEventArgs(g, rect));
        }

        IntPtr hdcScreen = User32.GetDC(IntPtr.Zero);
        IntPtr hdcMem = Gdi32.CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr hOldBitmap = Gdi32.SelectObject(hdcMem, hBitmap);

        SIZE size;
        size.cx = Size.Width;
        size.cy = Size.Height;

        POINT ptDst;
        ptDst.x = Location.X;
        ptDst.y = Location.Y;

        POINT ptSrc;
        ptSrc.x = 0;
        ptSrc.y = 0;

        BLENDFUNCTION blend = new()
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = _alpha,
            AlphaFormat = 1 // AC_SRC_ALPHA
        };

        User32.UpdateLayeredWindow(Handle, hdcScreen, ref ptDst, ref size, hdcMem, ref ptSrc, 0, ref blend, 2); // 2 = ULW_ALPHA

        Gdi32.SelectObject(hdcMem, hOldBitmap);
        User32.ReleaseDC(IntPtr.Zero, hdcScreen);
        Gdi32.DeleteObject(hBitmap);
        Gdi32.DeleteDC(hdcMem);
    }

    public virtual void Show()
    {
        if (Handle == IntPtr.Zero)
            CreateWindowOnly();

        User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);
        UpdateLayeredWindow();
    }

    public virtual void Hide()
    {
        if (Handle == IntPtr.Zero) return;
        User32.ShowWindow(Handle, User32.SW_HIDE);
        DestroyHandle();
    }

    public virtual void Close()
    {
        Hide();
        Dispose();
    }

    private void CreateWindowOnly()
    {
        CreateParams p = new CreateParams();
        p.Caption = "MSIFlux_OSD";

        int nX = _location.X;
        int nY = _location.Y;

        Screen screen = Screen.FromHandle(Handle);
        if (nX + _size.Width > screen.Bounds.Width)
            nX = screen.Bounds.Width - _size.Width;
        if (nY + _size.Height > screen.Bounds.Height)
            nY = screen.Bounds.Height - _size.Height;

        _location = new Point(nX, nY);

        p.X = _location.X;
        p.Y = _location.Y;
        p.Width = _size.Width;
        p.Height = _size.Height;
        p.Parent = IntPtr.Zero;

        p.Style = unchecked((int)User32.WS_POPUP);
        p.ExStyle = User32.WS_EX_TOPMOST | User32.WS_EX_TOOLWINDOW | User32.WS_EX_LAYERED | User32.WS_EX_NOACTIVATE | User32.WS_EX_TRANSPARENT;

        CreateHandle(p);
    }

    public virtual Point Location
    {
        get => _location;
        set
        {
            _location = value;
            if (Handle != IntPtr.Zero)
            {
                User32.SetWindowPos(Handle, IntPtr.Zero, value.X, value.Y, _size.Width, _size.Height, 0x0015); // SWP_NOZORDER|SWP_NOSIZE|SWP_NOACTIVATE
                UpdateLayeredWindow();
            }
        }
    }

    public virtual Size Size
    {
        get => _size;
        set
        {
            _size = value;
            if (Handle != IntPtr.Zero)
            {
                User32.SetWindowPos(Handle, IntPtr.Zero, _location.X, _location.Y, value.Width, value.Height, 0x0016); // SWP_NOZORDER|SWP_NOMOVE|SWP_NOACTIVATE
                UpdateLayeredWindow();
            }
        }
    }

    public int Width
    {
        get => _size.Width;
        set => Size = new Size(value, _size.Height);
    }

    public int Height
    {
        get => _size.Height;
        set => Size = new Size(_size.Width, value);
    }

    public int X
    {
        get => _location.X;
        set => Location = new Point(value, _location.Y);
    }

    public int Y
    {
        get => _location.Y;
        set => Location = new Point(_location.X, value);
    }

    public Rectangle Bound => new(0, 0, _size.Width, _size.Height);

    public byte Alpha
    {
        get => _alpha;
        set
        {
            if (_alpha == value) return;
            _alpha = value;
            UpdateLayeredWindow();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            DestroyHandle();
            _disposed = true;
        }
    }

    #region Win32 P/Invoke
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    internal static class User32
    {
        public const uint WS_POPUP = 0x80000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_HIDE = 0;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int X, int Y, int Width, int Height, uint flags);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    }

    internal static class Gdi32
    {
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")]
        public static extern IntPtr DeleteObject(IntPtr hObject);
    }
    #endregion
}
