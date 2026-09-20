using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.UI.ViewModels;

namespace CertificateAutomation.UI.Views;

/// <summary>
/// نمای آمار و نمودارها.
/// نمودارها مستقیماً با اشکال WPF رسم می‌شوند (بدون کتابخانهٔ بیرونی) تا روی سیستم‌های
/// آفلاین و در حالت تک‌فایل بدون هیچ وابستگی کار کنند.
/// </summary>
public partial class StatisticsView : UserControl
{
    private StatisticsViewModel? _vm;

    public StatisticsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.RedrawRequested -= OnRedrawRequested;
        _vm = DataContext as StatisticsViewModel;
        if (_vm is not null)
        {
            _vm.RedrawRequested += OnRedrawRequested;
            _vm.CaptureChartImage = CaptureChartAsPng;
        }
    }

    /// <summary>
    /// رندر کردن بوم نمودار فعلی به یک تصویر PNG، برای جاسازی در خروجی Excel/PDF.
    /// اگر بوم خالی باشد (هنوز چیزی رسم نشده)، null برمی‌گرداند.
    /// </summary>
    private byte[]? CaptureChartAsPng()
    {
        if (ChartCanvas.Children.Count == 0) return null;
        if (ChartCanvas.ActualWidth < 1 || ChartCanvas.ActualHeight < 1) return null;

        try
        {
            var dpi = 120.0; // کیفیت بالاتر از صفحه‌نمایش معمولی، برای وضوح خوب در چاپ
            var scale = dpi / 96.0;
            var width = (int)(ChartCanvas.ActualWidth * scale);
            var height = (int)(ChartCanvas.ActualHeight * scale);

            var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);

            // پس‌زمینهٔ سفید (وگرنه در Excel/PDF شفاف یا سیاه دیده می‌شود)
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null,
                    new Rect(0, 0, ChartCanvas.ActualWidth, ChartCanvas.ActualHeight));
            }
            rtb.Render(visual);
            rtb.Render(ChartCanvas);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch (Exception)
        {
            // اگر رندر تصویر شکست خورد، خروجی بدون تصویر (فقط جدول) ادامه می‌یابد.
            return null;
        }
    }

    private void OnRedrawRequested(object? sender, EventArgs e) => Redraw();

    /// <summary>پاک کردن بوم و رسم دوبارهٔ نمودار متناسب با نوع انتخاب‌شده.</summary>
    private void Redraw()
    {
        ChartCanvas.Children.Clear();
        if (_vm?.Result is null) return;

        // اندازهٔ بوم را برابر فضای واقعی در دسترس می‌گیریم تا نمودار بزرگ و خوانا باشد.
        var w = ChartScroll.ViewportWidth;
        var h = ChartScroll.ViewportHeight;
        if (w < 50) w = Math.Max(600, ActualWidth - 90);
        if (h < 50) h = Math.Max(420, ActualHeight - 230);

        // کمی حاشیه تا نوار پیمایش روی نمودار نیفتد
        w = Math.Max(520, w - 6);
        h = Math.Max(380, h - 6);

        ChartCanvas.Width = w;
        ChartCanvas.Height = h;

        try
        {
            switch (_vm.SelectedChartType)
            {
                case "دایره‌ای": DrawPie(_vm.Result, w, h); break;
                case "افقی": DrawHorizontalBars(_vm.Result, w, h); break;
                case "خطی": DrawLine(_vm.Result, w, h); break;
                case "پراکندگی": DrawScatter(_vm.Result, w, h); break;
                default: DrawBars(_vm.Result, w, h); break;
            }
        }
        catch (Exception ex)
        {
            // رسم هرگز نباید باعث بسته شدن برنامه شود؛ اما خطا را پنهان هم نمی‌کنیم
            // تا اگر نموداری رسم نشد، علتش مشخص باشد.
            ChartCanvas.Children.Clear();
            AddText("رسم نمودار ممکن نشد.", 20, 24, 13, true, SubtleBrush);
            AddText(ex.Message, 20, 50, 11.5, false, SubtleBrush, Math.Max(200, w - 40));
            AddText("نوع نمودار دیگری را امتحان کنید یا متغیر را عوض کنید.",
                    20, 76, 11.5, false, SubtleBrush);
        }
    }

    // ---------- پالت رنگ ----------
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x1E, 0x3A, 0x5F), Color.FromRgb(0x0E, 0x7C, 0x86),
        Color.FromRgb(0xE8, 0x8B, 0x2F), Color.FromRgb(0x6A, 0x4C, 0x93),
        Color.FromRgb(0x2A, 0x9D, 0x8F), Color.FromRgb(0xE7, 0x6F, 0x51),
        Color.FromRgb(0x45, 0x7B, 0x9D), Color.FromRgb(0x8A, 0xB1, 0x7D),
        Color.FromRgb(0xC1, 0x44, 0x53), Color.FromRgb(0xF4, 0xA2, 0x61),
        Color.FromRgb(0x3D, 0x5A, 0x80), Color.FromRgb(0xEE, 0x6C, 0x4D)
    };

    private static Brush BrushAt(int i)
    {
        var c = Palette[i % Palette.Length];
        // گرادیان ملایم برای ظاهر بهتر میله‌ها
        return new LinearGradientBrush(
            Color.FromRgb((byte)Math.Min(255, c.R + 28), (byte)Math.Min(255, c.G + 28), (byte)Math.Min(255, c.B + 28)),
            c, 90);
    }

    private static Brush SolidAt(int i) => new SolidColorBrush(Palette[i % Palette.Length]);

    private Brush TextBrush => TryFindResource("TextPrimary") as Brush ?? Brushes.Black;
    private Brush SubtleBrush => TryFindResource("TextSecondary") as Brush ?? Brushes.Gray;
    private Brush GridBrush => new SolidColorBrush(Color.FromArgb(46, 128, 140, 155));

    private FontFamily AppFont => TryFindResource("AppFont") as FontFamily ?? new FontFamily("Tahoma");

    /// <summary>افزودن یک متن به بوم و برگرداندن آن (برای تنظیمات بیشتر).</summary>
    private TextBlock AddText(string text, double left, double top, double size = 12,
                              bool bold = false, Brush? brush = null, double? maxWidth = null,
                              TextAlignment align = TextAlignment.Right)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = AppFont,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = brush ?? TextBrush,
            TextAlignment = align,
            TextWrapping = TextWrapping.NoWrap,
            // بوم چپ‌به‌راست است تا مختصات آینه نشود؛ اما خود متن فارسی راست‌چین می‌ماند.
            FlowDirection = FlowDirection.RightToLeft
        };
        if (maxWidth is double mw)
        {
            tb.MaxWidth = mw;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        ChartCanvas.Children.Add(tb);
        return tb;
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush,
                         double thickness = 1, DoubleCollection? dash = null)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = thickness
        };
        if (dash is not null) line.StrokeDashArray = dash;
        ChartCanvas.Children.Add(line);
    }

    private static string Fa(double v)
    {
        var s = Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.#", CultureInfo.InvariantCulture);
        return CertificateAutomation.Domain.Common.PersianDate
            .ToPersianDigits(s).Replace('.', '\u066B');
    }

    /// <summary>تخمین عرض متن، برای تصمیم‌گیری دربارهٔ چرخاندن برچسب‌ها.</summary>
    private double MeasureText(string text, double fontSize)
    {
        var ft = new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.RightToLeft,
            new Typeface(AppFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft.Width;
    }

    /// <summary>محاسبهٔ گام مناسب برای خطوط راهنمای محور عمودی (اعداد گرد و خوانا).</summary>
    private static double NiceStep(double max, int targetTicks)
    {
        if (max <= 0) return 1;
        var raw = max / targetTicks;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / mag;
        var nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
        return nice * mag;
    }

    // ---------- نمودار میله‌ای عمودی ----------
    private void DrawBars(AnalysisResult r, double w, double h)
    {
        var items = r.Categories.Take(20).ToList();
        if (items.Count == 0) return;

        var max = items.Max(i => i.Count);
        if (max <= 0) max = 1;

        // اگر برچسب‌ها بلندند، فضای بیشتری زیر نمودار می‌گذاریم و می‌چرخانیمشان.
        var longest = items.Max(i => MeasureText(i.Label, 12));
        var slotWidth = (w - 70) / items.Count;
        var rotate = longest > slotWidth - 6;

        var marginLeft = 62.0;
        var marginTop = 24.0;
        var marginBottom = rotate ? Math.Min(150, 42 + longest * 0.62) : 52;

        var plotW = w - marginLeft - 28;
        var plotH = h - marginBottom - marginTop;
        if (plotH < 80) { plotH = 80; }

        var step = NiceStep(max, 5);
        var axisMax = Math.Ceiling(max / step) * step;
        if (axisMax <= 0) axisMax = step;

        // خطوط راهنما و مقادیر محور عمودی
        for (var v = 0.0; v <= axisMax + 1e-9; v += step)
        {
            var y = marginTop + plotH - plotH * v / axisMax;
            AddLine(marginLeft, y, marginLeft + plotW, y, GridBrush, 1);
            AddText(Fa(v), 6, y - 10, 11, false, SubtleBrush, marginLeft - 14);
        }

        // محور افقی پررنگ‌تر
        AddLine(marginLeft, marginTop + plotH, marginLeft + plotW, marginTop + plotH, SubtleBrush, 1.4);

        var slot = plotW / items.Count;
        var barW = Math.Max(18, Math.Min(64, slot * 0.6));

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var barH = plotH * it.Count / axisMax;
            var cx = marginLeft + slot * i + slot / 2;

            var rect = new Rectangle
            {
                Width = barW,
                Height = Math.Max(2, barH),
                Fill = BrushAt(i),
                RadiusX = 5, RadiusY = 5,
                ToolTip = $"{it.Label}: {Fa(it.Count)}",
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black, Opacity = 0.16,
                    BlurRadius = 7, ShadowDepth = 1.5, Direction = 270
                }
            };
            Canvas.SetLeft(rect, cx - barW / 2);
            Canvas.SetTop(rect, marginTop + plotH - barH);
            ChartCanvas.Children.Add(rect);

            // مقدار بالای میله
            var valText = Fa(it.Count);
            var vw = MeasureText(valText, 12);
            AddText(valText, cx - vw / 2, marginTop + plotH - barH - 20, 12, true, TextBrush,
                    null, TextAlignment.Center);

            // برچسب زیر میله
            if (rotate)
            {
                var label = new TextBlock
                {
                    Text = it.Label,
                    FontSize = 11.5,
                    FontFamily = AppFont,
                    Foreground = TextBrush,
                    MaxWidth = marginBottom - 18,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FlowDirection = FlowDirection.RightToLeft,
                    RenderTransform = new RotateTransform(-42),
                    RenderTransformOrigin = new Point(1, 0.5)
                };
                Canvas.SetLeft(label, cx - (marginBottom - 18));
                Canvas.SetTop(label, marginTop + plotH + 10);
                ChartCanvas.Children.Add(label);
            }
            else
            {
                var lw = Math.Min(slot - 4, MeasureText(it.Label, 12));
                AddText(it.Label, cx - lw / 2, marginTop + plotH + 10, 12, false, TextBrush,
                        slot - 4, TextAlignment.Center);
            }
        }
    }

    // ---------- نمودار میله‌ای افقی (بهترین گزینه برای برچسب‌های فارسی بلند) ----------
    private void DrawHorizontalBars(AnalysisResult r, double w, double h)
    {
        var items = r.Categories.Take(20).ToList();
        if (items.Count == 0) return;

        var max = items.Max(i => i.Count);
        if (max <= 0) max = 1;

        // ارتفاع هر ردیف؛ اگر تعداد زیاد بود، بوم بلندتر می‌شود و اسکرول می‌خورد.
        var rowH = Math.Max(30.0, Math.Min(52.0, (h - 30) / items.Count));
        var needed = items.Count * rowH + 30;
        if (needed > h) ChartCanvas.Height = needed;

        // عرض ستون برچسب بر اساس بلندترین برچسب (با سقف معقول)
        var labelW = Math.Min(w * 0.38, Math.Max(110, items.Max(i => MeasureText(i.Label, 12.5)) + 16));
        var valueW = 56.0;
        var plotW = w - labelW - valueW - 24;

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var y = 14 + i * rowH;
            var barH = rowH * 0.58;
            var barW = plotW * it.Count / max;

            // برچسب سمت راست (چون RTL)
            AddText(it.Label, w - labelW - 8, y + (rowH - 18) / 2, 12.5, false, TextBrush, labelW);

            var rect = new Rectangle
            {
                Width = Math.Max(2, barW),
                Height = barH,
                Fill = BrushAt(i),
                RadiusX = 4, RadiusY = 4,
                ToolTip = $"{it.Label}: {Fa(it.Count)}" +
                          (it.Percent > 0 ? $" ({Fa(Math.Round(it.Percent, 1))}٪)" : ""),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black, Opacity = 0.14,
                    BlurRadius = 6, ShadowDepth = 1, Direction = 270
                }
            };
            Canvas.SetLeft(rect, w - labelW - 16 - barW);
            Canvas.SetTop(rect, y + (rowH - barH) / 2);
            ChartCanvas.Children.Add(rect);

            // مقدار انتهای میله
            var valText = it.Percent > 0
                ? $"{Fa(it.Count)}  ({Fa(Math.Round(it.Percent, 1))}٪)"
                : Fa(it.Count);
            AddText(valText, w - labelW - 22 - barW - valueW, y + (rowH - 18) / 2,
                    11.5, true, SubtleBrush, valueW + 40, TextAlignment.Left);
        }
    }

    // ---------- نمودار دایره‌ای ----------
    private void DrawPie(AnalysisResult r, double w, double h)
    {
        var items = r.Categories.Take(12).ToList();
        if (items.Count == 0) return;

        var total = items.Sum(i => (double)i.Count);
        if (total <= 0) return;

        // راهنما سمت چپ، دایره سمت راست
        var legendW = Math.Min(300, Math.Max(170, items.Max(i => MeasureText(i.Label, 12)) + 96));
        var available = w - legendW - 40;
        var radius = Math.Max(70, Math.Min(available, h - 60) / 2);
        var cx = w - radius - 40;
        var cy = h / 2;

        double startAngle = -90;
        for (var i = 0; i < items.Count; i++)
        {
            var sweep = items[i].Count / total * 360.0;
            if (sweep <= 0) continue;

            var pct = items[i].Count / total * 100;
            var slice = CreateSlice(cx, cy, radius, startAngle, sweep, BrushAt(i));
            slice.ToolTip = $"{items[i].Label}: {Fa(items[i].Count)} ({Fa(Math.Round(pct, 1))}٪)";
            ChartCanvas.Children.Add(slice);

            // درصد روی قاچ، فقط اگر جا باشد
            if (pct >= 6)
            {
                var mid = startAngle + sweep / 2;
                var lp = DegToPoint(cx, cy, radius * 0.66, mid);
                var t = AddText($"{Fa(Math.Round(pct))}٪", lp.X - 18, lp.Y - 10, 12.5, true,
                                Brushes.White, null, TextAlignment.Center);
                t.Width = 36;
            }

            startAngle += sweep;
        }

        // دایرهٔ سفید وسط (ظاهر دونات، تمیزتر)
        var hole = new Ellipse
        {
            Width = radius * 0.9, Height = radius * 0.9,
            Fill = TryFindResource("CardBackground") as Brush ?? Brushes.White
        };
        Canvas.SetLeft(hole, cx - radius * 0.45);
        Canvas.SetTop(hole, cy - radius * 0.45);
        ChartCanvas.Children.Add(hole);

        var totalText = AddText(Fa(total), cx - 40, cy - 22, 21, true, TextBrush, 80, TextAlignment.Center);
        totalText.Width = 80;
        var lbl = AddText("مجموع", cx - 40, cy + 4, 12, false, SubtleBrush, 80, TextAlignment.Center);
        lbl.Width = 80;

        // راهنما
        var legendTop = Math.Max(16, cy - items.Count * 13);
        for (var i = 0; i < items.Count; i++)
        {
            var y = legendTop + i * 26;
            var swatch = new Rectangle
            {
                Width = 14, Height = 14, Fill = SolidAt(i), RadiusX = 3, RadiusY = 3
            };
            Canvas.SetLeft(swatch, legendW - 22);
            Canvas.SetTop(swatch, y + 2);
            ChartCanvas.Children.Add(swatch);

            var pct = Math.Round(items[i].Count / total * 100, 1);
            AddText($"{items[i].Label} — {Fa(items[i].Count)} ({Fa(pct)}٪)",
                    8, y, 12, false, TextBrush, legendW - 32);
        }
    }

    /// <summary>ساخت یک قاچ دایره با مسیر کمانی.</summary>
    private static Path CreateSlice(double cx, double cy, double radius,
                                    double startAngle, double sweepAngle, Brush fill)
    {
        if (sweepAngle >= 359.99)
        {
            return new Path
            {
                Fill = fill,
                Data = new EllipseGeometry(new Point(cx, cy), radius, radius)
            };
        }

        var start = DegToPoint(cx, cy, radius, startAngle);
        var end = DegToPoint(cx, cy, radius, startAngle + sweepAngle);

        var figure = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
        figure.Segments.Add(new LineSegment(start, true));
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
            sweepAngle > 180, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path
        {
            Fill = fill,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Data = geometry
        };
    }

    private static Point DegToPoint(double cx, double cy, double r, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    // ---------- نمودار خطی ----------
    private void DrawLine(AnalysisResult r, double w, double h)
    {
        var items = r.Categories.Take(40).ToList();
        if (items.Count < 2) { DrawBars(r, w, h); return; }

        var max = items.Max(i => i.Count);
        if (max <= 0) max = 1;

        var longest = items.Max(i => MeasureText(i.Label, 11));
        var marginLeft = 62.0;
        var marginTop = 24.0;
        var marginBottom = Math.Min(140, 46 + longest * 0.55);
        var plotW = w - marginLeft - 28;
        var plotH = h - marginBottom - marginTop;
        if (plotH < 80) plotH = 80;

        var step = NiceStep(max, 5);
        var axisMax = Math.Ceiling(max / step) * step;
        if (axisMax <= 0) axisMax = step;

        for (var v = 0.0; v <= axisMax + 1e-9; v += step)
        {
            var y = marginTop + plotH - plotH * v / axisMax;
            AddLine(marginLeft, y, marginLeft + plotW, y, GridBrush);
            AddText(Fa(v), 6, y - 10, 11, false, SubtleBrush, marginLeft - 14);
        }
        AddLine(marginLeft, marginTop + plotH, marginLeft + plotW, marginTop + plotH, SubtleBrush, 1.4);

        var dx = plotW / Math.Max(1, items.Count - 1);
        var pts = new PointCollection();
        for (var i = 0; i < items.Count; i++)
        {
            var x = marginLeft + dx * i;
            var y = marginTop + plotH - plotH * items[i].Count / axisMax;
            pts.Add(new Point(x, y));
        }

        // ناحیهٔ زیر خط (ظاهر بهتر)
        var areaFigure = new PathFigure { StartPoint = new Point(pts[0].X, marginTop + plotH) };
        foreach (var p in pts) areaFigure.Segments.Add(new LineSegment(p, true));
        areaFigure.Segments.Add(new LineSegment(new Point(pts[^1].X, marginTop + plotH), true));
        areaFigure.IsClosed = true;
        var areaGeo = new PathGeometry();
        areaGeo.Figures.Add(areaFigure);
        ChartCanvas.Children.Add(new Path
        {
            Data = areaGeo,
            Fill = new LinearGradientBrush(
                Color.FromArgb(80, 0x0E, 0x7C, 0x86),
                Color.FromArgb(6, 0x0E, 0x7C, 0x86), 90)
        });

        ChartCanvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = SolidAt(1),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        });

        var labelEvery = Math.Max(1, (int)Math.Ceiling(items.Count / 10.0));
        for (var i = 0; i < items.Count; i++)
        {
            var p = pts[i];
            var dot = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = TryFindResource("CardBackground") as Brush ?? Brushes.White,
                Stroke = SolidAt(1), StrokeThickness = 2.5,
                ToolTip = $"{items[i].Label}: {Fa(items[i].Count)}"
            };
            Canvas.SetLeft(dot, p.X - 5);
            Canvas.SetTop(dot, p.Y - 5);
            ChartCanvas.Children.Add(dot);

            if (i % labelEvery == 0)
            {
                var label = new TextBlock
                {
                    Text = items[i].Label,
                    FontSize = 11,
                    FontFamily = AppFont,
                    Foreground = TextBrush,
                    MaxWidth = marginBottom - 16,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FlowDirection = FlowDirection.RightToLeft,
                    RenderTransform = new RotateTransform(-42),
                    RenderTransformOrigin = new Point(1, 0.5)
                };
                Canvas.SetLeft(label, p.X - (marginBottom - 16));
                Canvas.SetTop(label, marginTop + plotH + 10);
                ChartCanvas.Children.Add(label);
            }
        }
    }

    // ---------- نمودار پراکندگی + خط رگرسیون ----------
    private void DrawScatter(AnalysisResult r, double w, double h)
    {
        if (r.Points.Count == 0)
        {
            AddText("برای نمودار پراکندگی باید هر دو متغیر عددی باشند.",
                    20, 24, 13, true, SubtleBrush);
            AddText("مثلاً «سن فرد» به‌همراه «مدت آموزش».",
                    20, 50, 12, false, SubtleBrush);
            return;
        }

        const double marginLeft = 62, marginBottom = 52, marginTop = 24, marginRight = 28;
        var plotW = w - marginLeft - marginRight;
        var plotH = h - marginBottom - marginTop;

        var minX = r.Points.Min(p => p.X); var maxX = r.Points.Max(p => p.X);
        var minY = r.Points.Min(p => p.Y); var maxY = r.Points.Max(p => p.Y);
        if (Math.Abs(maxX - minX) < 1e-9) maxX = minX + 1;
        if (Math.Abs(maxY - minY) < 1e-9) maxY = minY + 1;

        // کمی حاشیه تا نقاط روی محور نچسبند
        var padX = (maxX - minX) * 0.06; minX -= padX; maxX += padX;
        var padY = (maxY - minY) * 0.08; minY -= padY; maxY += padY;

        double MapX(double x) => marginLeft + (x - minX) / (maxX - minX) * plotW;
        double MapY(double y) => marginTop + plotH - (y - minY) / (maxY - minY) * plotH;

        for (var g = 0; g <= 5; g++)
        {
            var y = marginTop + plotH - plotH * g / 5.0;
            AddLine(marginLeft, y, marginLeft + plotW, y, GridBrush);
            AddText(Fa(minY + (maxY - minY) * g / 5.0), 6, y - 10, 11, false, SubtleBrush, marginLeft - 14);

            var x = marginLeft + plotW * g / 5.0;
            AddLine(x, marginTop, x, marginTop + plotH, GridBrush);
            var xt = Fa(minX + (maxX - minX) * g / 5.0);
            var tw = MeasureText(xt, 11);
            AddText(xt, x - tw / 2, marginTop + plotH + 10, 11, false, SubtleBrush, null, TextAlignment.Center);
        }

        AddLine(marginLeft, marginTop + plotH, marginLeft + plotW, marginTop + plotH, SubtleBrush, 1.4);
        AddLine(marginLeft, marginTop, marginLeft, marginTop + plotH, SubtleBrush, 1.4);

        // خط رگرسیون (زیر نقاط رسم می‌شود تا نقاط دیده شوند)
        if (r.RegressionSlope is double slope && r.RegressionIntercept is double intercept)
        {
            var y1 = slope * minX + intercept;
            var y2 = slope * maxX + intercept;
            ChartCanvas.Children.Add(new Line
            {
                X1 = MapX(minX), Y1 = MapY(Math.Clamp(y1, minY, maxY)),
                X2 = MapX(maxX), Y2 = MapY(Math.Clamp(y2, minY, maxY)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xC1, 0x44, 0x53)),
                StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                ToolTip = "خط روند (رگرسیون خطی)"
            });
        }

        foreach (var p in r.Points)
        {
            var dot = new Ellipse
            {
                Width = 12, Height = 12,
                Fill = new SolidColorBrush(Color.FromArgb(200, 0x0E, 0x7C, 0x86)),
                Stroke = Brushes.White, StrokeThickness = 1.5,
                ToolTip = $"({Fa(p.X)} ، {Fa(p.Y)})"
            };
            Canvas.SetLeft(dot, MapX(p.X) - 6);
            Canvas.SetTop(dot, MapY(p.Y) - 6);
            ChartCanvas.Children.Add(dot);
        }
    }
}
