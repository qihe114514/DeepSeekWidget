using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeepSeekWidget {

    /// <summary>
    /// iOS 风格 G3（连续曲率）圆角的卡片容器。
    /// 普通 Border 的 CornerRadius 是圆弧圆角（曲率恒定，过渡偏"楞"）；
    /// n=5 超椭圆在小半径下曲率过度集中（峰值达圆弧的 ~2.9 倍且挤在一小段），
    /// 视觉上反而像"倒角"。
    /// 本控件用曲率沿弧长按正弦分布（端点 0 → 中间峰值 ≈ 1.49/r）的曲线
    /// 近似 iOS 连续曲率圆角：从直边平滑起弯、中间均匀转向，观感圆润自然。
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
            // 裁剪子内容，让内部文字/按钮也严格落在连续曲率形状内
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

        // 矩形 + 四角连续曲率曲线拼接成完整轮廓
        static Geometry CreateSquircleGeometry(Rect rect, double radius) {
            double x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
            double r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width / 2, rect.Height / 2)));

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open()) {
                if (r < 0.5) {
                    ctx.BeginFigure(new Point(x0, y0), true, true);
                    ctx.LineTo(new Point(x1, y0), false, false);
                    ctx.LineTo(new Point(x1, y1), false, false);
                    ctx.LineTo(new Point(x0, y1), false, false);
                    return geo;
                }
                Point[] corner = CornerCurve(r, 100);
                ctx.BeginFigure(new Point(x0 + r, y0), true, true);
                ctx.LineTo(new Point(x1 - r, y0), false, false);   // 上边
                AppendCorner(ctx, corner, x1 - r, y0, true, true); // 右上
                ctx.LineTo(new Point(x1, y1 - r), false, false);   // 右边
                AppendCorner(ctx, corner, x1, y1 - r, true, false);// 右下
                ctx.LineTo(new Point(x0 + r, y1), false, false);   // 底边
                AppendCorner(ctx, corner, x0 + r, y1, false, false);// 左下
                ctx.LineTo(new Point(x0, y0 + r), false, false);   // 左边
                AppendCorner(ctx, corner, x0, y0 + r, false, true);// 左上
                return geo;
            }
        }

        // 曲率正弦分布的连续曲率圆角曲线。
        // 局部坐标系：起点 (0,0)（切线沿 +x，即直边方向），终点 (r,r)（切线沿 +y）。
        // κ(s) = κm·sin(πs/L)，总转角 ∫κ ds = π/2 → κm = π²/(4L)；
        // 缩放后峰值曲率 ≈ 1.49/r（比圆弧更"鼓"一点，但曲率从 0 平滑过渡，不产生倒角感）。
        static Point[] CornerCurve(double r, int N) {
            double L = 1.0;
            double kappa = Math.PI * Math.PI / (4 * L);
            var pts = new Point[N + 1];
            double x = 0, y = 0, theta = 0;
            pts[0] = new Point(0, 0);
            double ds = L / N;
            for (int i = 1; i <= N; i++) {
                double s = i * ds;
                theta += kappa * Math.Sin(Math.PI * s / L) * ds;
                x += Math.Cos(theta) * ds;
                y += Math.Sin(theta) * ds;
                pts[i] = new Point(x, y);
            }
            double k = r / x; // 曲线关于 45° 对称，终点 x == y
            for (int i = 0; i <= N; i++) pts[i] = new Point(pts[i].X * k, pts[i].Y * k);
            return pts;
        }

        // 将局部曲线点映射到对应角。corner 为 (0,0)→(r,r) 的曲线；
        // 平移锚点 bx,by 为角部"起点"（贴直边端点），right/top 决定角中心方位。
        static void AppendCorner(StreamGeometryContext ctx, Point[] corner, double bx, double by,
                                 bool right, bool top) {
            for (int i = 0; i < corner.Length; i++) {
                double px = corner[i].X;
                double py = corner[i].Y;
                double x, y;
                if (right && top) { x = bx + px; y = by + py; }        // 右上
                else if (right) { x = bx - py; y = by + px; }          // 右下
                else if (top) { x = bx + py; y = by - px; }            // 左上
                else { x = bx - px; y = by - py; }                     // 左下
                ctx.LineTo(new Point(x, y), false, false);
            }
        }
    }
}
