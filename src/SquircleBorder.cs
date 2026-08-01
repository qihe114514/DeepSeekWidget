using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeepSeekWidget {

    /// <summary>
    /// iOS 风格 G2.5（连续曲率）圆角的卡片容器。
    /// 每个圆角由 3 段三次贝塞尔组成（算法移植自 Kyant 的
    /// ContinuousCurvatureRoundedRectangleCornerBuilder）：控制点由「曲率连续 +
    /// 曲率导数连续」的条件解析求解，首尾两段带 2/3·r 的延长段贴在直边上，
    /// 使圆角与直线连接处曲率=0 平滑过渡（G2），三段贝塞尔之间曲率及其导数
    /// 均连续（G2.5）。这是 iOS 圆角的真实结构，观感最自然。
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

        // 矩形 + 四角 G2.5 三段贝塞尔圆角拼接成完整轮廓（铺设方式对应 RoundedRectangleOutline）
        static Geometry CreateSquircleGeometry(Rect rect, double radius) {
            double x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
            // 曲线首尾各向直边外延长 2/3·r，需 2r + 4/3·r ≤ 短边；3.6 略大于 10/3 留余量
            double r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 3.6));

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open()) {
                if (r < 0.5) {
                    ctx.BeginFigure(new Point(x0, y0), true, true);
                    ctx.LineTo(new Point(x1, y0), true, false);
                    ctx.LineTo(new Point(x1, y1), true, false);
                    ctx.LineTo(new Point(x0, y1), true, false);
                    return geo;
                }
                double[] p = BuildCornerBezierPoints();

                // 右上角：从 (x1-r-2/3r, y0)（上边）经三段贝塞尔到 (x1, y0+5/3r)（右边）
                double bx = x1 - r, by = y0;
                ctx.BeginFigure(new Point(bx + p[0] * r, by + p[1] * r), true, true);
                BezierCorner(ctx, bx, by, r, p, true, 1.0, 1.0);

                // 右边直线 → 右下角（反向）→ 底边直线
                bx = x1 - r; by = y1;
                ctx.LineTo(new Point(bx + p[18] * r, by - p[19] * r), true, false);
                BezierCorner(ctx, bx, by, r, p, false, 1.0, -1.0);

                // 底边直线 → 左下角（正向）→ 左边直线
                bx = x0 + r; by = y1;
                ctx.LineTo(new Point(bx - p[0] * r, by - p[1] * r), true, false);
                BezierCorner(ctx, bx, by, r, p, true, -1.0, -1.0);

                // 左边直线 → 左上角（反向），close() 自动连回起点（上边直线段）
                bx = x0 + r; by = y0;
                ctx.LineTo(new Point(bx - p[18] * r, by + p[19] * r), true, false);
                BezierCorner(ctx, bx, by, r, p, false, -1.0, 1.0);

                return geo;
            }
        }

        // 铺一个角的三段贝塞尔。forward=true 时控制点按 p[2..19] 顺序（正向），
        // forward=false 时按 p[16..0] 倒序（反向）。sx/sy 为坐标方向符号：
        // 右排角 sx=+1、左排角 sx=-1；下排角 sy=-1、上排角 sy=+1。
        static void BezierCorner(StreamGeometryContext ctx, double bx, double by, double r,
                                 double[] p, bool forward, double sx, double sy) {
            if (forward) {
                ctx.BezierTo(new Point(bx + sx * p[2] * r, by + sy * p[3] * r),
                             new Point(bx + sx * p[4] * r, by + sy * p[5] * r),
                             new Point(bx + sx * p[6] * r, by + sy * p[7] * r), true, false);
                ctx.BezierTo(new Point(bx + sx * p[8] * r, by + sy * p[9] * r),
                             new Point(bx + sx * p[10] * r, by + sy * p[11] * r),
                             new Point(bx + sx * p[12] * r, by + sy * p[13] * r), true, false);
                ctx.BezierTo(new Point(bx + sx * p[14] * r, by + sy * p[15] * r),
                             new Point(bx + sx * p[16] * r, by + sy * p[17] * r),
                             new Point(bx + sx * p[18] * r, by + sy * p[19] * r), true, false);
            } else {
                ctx.BezierTo(new Point(bx + sx * p[16] * r, by + sy * p[17] * r),
                             new Point(bx + sx * p[14] * r, by + sy * p[15] * r),
                             new Point(bx + sx * p[12] * r, by + sy * p[13] * r), true, false);
                ctx.BezierTo(new Point(bx + sx * p[10] * r, by + sy * p[11] * r),
                             new Point(bx + sx * p[8] * r, by + sy * p[9] * r),
                             new Point(bx + sx * p[6] * r, by + sy * p[7] * r), true, false);
                ctx.BezierTo(new Point(bx + sx * p[4] * r, by + sy * p[5] * r),
                             new Point(bx + sx * p[2] * r, by + sy * p[3] * r),
                             new Point(bx + sx * p[0] * r, by + sy * p[1] * r), true, false);
            }
        }

        // G2.5 三段贝塞尔角曲线（归一化坐标，×r 使用）。
        // 返回 20 个 double = 10 个点：(x0,0) (x1,0) (x2,0) (x3,y3) (x4,y4) (x5,y5) (x6,y6) (1,y7) (1,y8) (1,y9)
        // 默认参数：extendedFraction=2/3（延长段长度），arcFraction=0.5（中间段占比）
        static double[] BuildCornerBezierPoints() {
            const double SQRT_2 = 1.4142135623730951;
            const double FRAC_PI_4 = 0.7853981633974483;
            const double FRAC_1_SQRT_2 = 0.7071067811865476;
            const double extendedFraction = 2.0 / 3.0;
            const double arcFraction = 0.5;

            double theta = (1.0 - arcFraction) * FRAC_PI_4;
            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);
            double cot = 1.0 / Math.Tan(theta);
            double cos2 = cos * cos;
            double sin2 = sin * sin;
            double cos3 = cos2 * cos;
            double sin3 = sin2 * sin;

            double k0 = 27.0 * (SQRT_2 - 6.0 * cos + 6.0 * SQRT_2 * cos2 - 4.0 * cos3) * cot +
                2.0 * sin * (-9.0 + 2.0 * (SQRT_2 - 2.0 * sin) * sin3 + 2.0 * SQRT_2 * cos * (9.0 + sin2) - 2.0 * cos2 * (9.0 + 2.0 * sin2));
            double k1 = -81.0 * (-2.0 + SQRT_2 + 4.0 * (-1.0 + SQRT_2) * cos + 2.0 * (-2.0 + SQRT_2) * cos2) * cot -
                4.0 * sin * (-9.0 + 9.0 * SQRT_2 + SQRT_2 * sin3 + (-2.0 + SQRT_2) * cos * (9.0 + sin2));
            double k2 = 9.0 * (9.0 * (-4.0 + 3.0 * SQRT_2 + (-6.0 + 4.0 * SQRT_2) * cos) * cot + (-6.0 + 4.0 * SQRT_2) * sin);
            double k3 = 27.0 * (10.0 - 7.0 * SQRT_2) * cot;

            double k = extendedFraction; // 卡片足够大，无需按 tW/tH 缩放延长段

            // 解三次方程求曲率标量 κ（由曲率导数连续条件导出）
            double kappa = SolveCubicSingle(k3, k2, k1 + 8.0 * (-k) * sin3 * sin, k0);

            double x3 = FRAC_1_SQRT_2 + (-FRAC_1_SQRT_2 + sin) / kappa;
            double y3 = 1.0 - FRAC_1_SQRT_2 + (FRAC_1_SQRT_2 - cos) / kappa;
            double x2 = x3 - y3 * cot;
            double x1 = x2 - 1.5 * kappa * y3 * y3 / sin3;
            double x0 = -k;

            double x6 = 1.0 - y3;
            double y6 = 1.0 - x3;
            double y7 = 1.0 - x2;
            double y8 = 1.0 - x1;
            double y9 = 1.0 - x0;

            // 中间段控制点由曲率连续条件解出
            double a = 1.5 * kappa;
            double g = cos2 - sin2;
            double x36 = x6 - x3;
            double y36 = y6 - y3;
            double c = -(cos * y36 - sin * x36);
            double lambda = (-g + Math.Sqrt(g * g - 4.0 * a * c)) / (2.0 * a);
            double x4 = x3 + lambda * cos;
            double y4 = y3 + lambda * sin;
            double x5 = x6 - lambda * sin;
            double y5 = y6 - lambda * cos;

            return new double[] { x0, 0.0, x1, 0.0, x2, 0.0, x3, y3, x4, y4, x5, y5, x6, y6, 1.0, y7, 1.0, y8, 1.0, y9 };
        }

        // 三次方程 a·x³ + b·x² + c·x + d = 0 的实根（Cardano 公式）
        static double SolveCubicSingle(double a, double b, double c, double d) {
            double f = ((3.0 * c / a) - (b * b) / (a * a)) / 3.0;
            double g = ((2.0 * b * b * b) / (a * a * a) - (9.0 * b * c) / (a * a) + (27.0 * d) / a) / 27.0;
            double h = g * g / 4.0 + f * f * f / 27.0;
            double sqrtH = Math.Sqrt(h);
            return Cbrt(-g / 2.0 + sqrtH) + Cbrt(-g / 2.0 - sqrtH) - b / (3.0 * a);
        }

        static double Cbrt(double x) {
            return x < 0 ? -Math.Pow(-x, 1.0 / 3.0) : Math.Pow(x, 1.0 / 3.0);
        }
    }
}
