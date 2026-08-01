using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeepSeekWidget {

    /// <summary>
    /// iOS 风格 G3（连续曲率）圆角的卡片容器。
    /// 普通 Border 的 CornerRadius 是圆弧圆角（曲率恒定，角部发"楞"）；
    /// 这里用超椭圆 |x|^n + |y|^n = r^n（n = 5）近似 iOS 的连续曲率圆角
    /// （squircle）：角部曲率从边缘的 0 平滑过渡到中间最大值，观感更圆润自然。
    /// </summary>
    public class SquircleBorder : Border {

        public static readonly DependencyProperty SquircleRadiusProperty =
            DependencyProperty.Register("SquircleRadius", typeof(double), typeof(SquircleBorder),
                new FrameworkPropertyMetadata(14.0,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnSquircleRadiusChanged));

        public double SquircleRadius {
            get { return (double)GetValue(SquircleRadiusProperty); }
            set { SetValue(SquircleRadiusProperty, value); }
        }

        static void OnSquircleRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((SquircleBorder)d).UpdateGeometry();
        }

        Geometry _geometry;

        protected override Size ArrangeOverride(Size arrangeSize) {
            Size size = base.ArrangeOverride(arrangeSize);
            UpdateGeometry();
            return size;
        }

        void UpdateGeometry() {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) {
                _geometry = null;
                Clip = null;
                return;
            }
            _geometry = CreateSquircleGeometry(new Rect(0, 0, w, h), SquircleRadius);
            // 裁剪子内容，让内部文字/按钮也严格落在超椭圆形状内
            Clip = _geometry;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc) {
            if (_geometry == null) UpdateGeometry();
            if (_geometry == null) return;
            Pen pen = null;
            if (BorderBrush != null && BorderThickness.Left > 0) {
                double w = Math.Min(BorderThickness.Left,
                    Math.Min(BorderThickness.Right, Math.Min(BorderThickness.Top, BorderThickness.Bottom)));
                pen = new Pen(BorderBrush, w);
                pen.Freeze();
            }
            dc.DrawGeometry(Background, pen, _geometry);
        }

        // 矩形 + 四角超椭圆（n = 5）拼接成完整轮廓
        static Geometry CreateSquircleGeometry(Rect rect, double radius) {
            double x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
            double r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width / 2, rect.Height / 2)));

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open()) {
                if (r < 0.5) {
                    // 半径过小直接画矩形
                    ctx.BeginFigure(new Point(x0, y0), true, true);
                    ctx.LineTo(new Point(x1, y0), false, false);
                    ctx.LineTo(new Point(x1, y1), false, false);
                    ctx.LineTo(new Point(x0, y1), false, false);
                    return geo;
                }
                ctx.BeginFigure(new Point(x0 + r, y0), true, true);
                ctx.LineTo(new Point(x1 - r, y0), false, false);   // 上边
                AppendCorner(ctx, x1, y0, r, true, true, true);    // 右上：(x1-r,y0)→(x1,y0+r)
                ctx.LineTo(new Point(x1, y1 - r), false, false);   // 右边
                AppendCorner(ctx, x1, y1, r, true, false, false);  // 右下：(x1,y1-r)→(x1-r,y1)
                ctx.LineTo(new Point(x0 + r, y1), false, false);   // 底边
                AppendCorner(ctx, x0, y1, r, false, false, true);  // 左下：(x0+r,y1)→(x0,y1-r)
                ctx.LineTo(new Point(x0, y0 + r), false, false);   // 左边
                AppendCorner(ctx, x0, y0, r, false, true, false);  // 左上：(x0,y0+r)→(x0+r,y0)
                return geo;
            }
        }

        // 角部超椭圆曲线段：局部坐标 u^5 + v^5 = 1，u,v ∈ [0,1]，
        // 实际点 x = cx ± u·r，y = cy ± v·r（right/top 决定角中心方位）。
        // startAtU1=true 时从 u=1（靠角内）走到 u=0（贴直边），保证与前后直线段方向一致。
        static void AppendCorner(StreamGeometryContext ctx, double cx, double cy, double r,
                                 bool right, bool top, bool startAtU1) {
            const int N = 20; // 每角采样段数，14px 半径下每段不足 1px，视觉平滑
            for (int i = 0; i <= N; i++) {
                double u = startAtU1 ? 1 - (double)i / N : (double)i / N;
                double v = Math.Pow(1 - Math.Pow(u, 5), 0.2);
                ctx.LineTo(new Point(right ? cx - u * r : cx + u * r,
                                     top ? cy + v * r : cy - v * r), false, false);
            }
        }
    }
}
