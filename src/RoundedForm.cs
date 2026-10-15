using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mengqi
{
    /// <summary>
    /// 带圆角裁剪的窗体基类。
    /// 1) 启用 AutoScaleMode.Dpi，让控件与字体按系统 DPI 比例统一缩放，修复高分屏下文字被裁切（显示不全）；
    /// 2) AutoScaleMode 不会自动缩放 Region 裁剪，因此在窗体创建完成（已按 DPI 缩放）后，
    ///    以及 DPI 变化（跨显示器）时，重新根据当前 Width/Height 计算圆角区域，避免旧尺寸把文字裁掉。
    /// </summary>
    internal class RoundedForm : Form
    {
        protected int CornerRadius = 18;

        internal RoundedForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyRounding();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyRounding();
        }

        protected void ApplyRounding()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                int r = Math.Min(CornerRadius, Math.Min(Width, Height) / 2);
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(Width - r, 0, r, r, 270, 90);
                path.AddArc(Width - r, Height - r, r, r, 0, 90);
                path.AddArc(0, Height - r, r, r, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }
    }
}
