using System.Globalization;
using System.Text.Json;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;

namespace CertificateAutomation.Infrastructure.Services;

/// <summary>
/// تحلیل داده‌های پرسنل. تمام ستون‌های موجود (چه ستون‌های ثابت جدول و چه ستون‌های
/// اضافی فایل Excel که در ExtraDataJson ذخیره شده‌اند) قابل تحلیل‌اند.
///
/// نوع هر ستون به‌صورت خودکار تشخیص داده می‌شود: اگر بیشتر مقادیر عددی باشند،
/// ستون عددی در نظر گرفته می‌شود (میانگین، هیستوگرام، پراکندگی)؛ در غیر این صورت
/// دسته‌ای است (توزیع فراوانی، نمودار میله‌ای/دایره‌ای).
/// </summary>
public class StatisticsService : IStatisticsService
{
    private readonly IEmployeeRepository _employees;
    private readonly IColumnMappingRepository _mappings;
    private readonly IAppLogger _logger;

    public StatisticsService(
        IEmployeeRepository employees,
        IColumnMappingRepository mappings,
        IAppLogger logger)
    {
        _employees = employees;
        _mappings = mappings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<(string Key, string DisplayName)>> GetAvailableFieldsAsync(CancellationToken ct = default)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // ستون‌های ثابت
        foreach (var (key, name) in CoreFields)
            if (seen.Add(key)) result.Add((key, name));

        // ستون‌های ثبت‌شده از Excel
        try
        {
            var mappings = await _mappings.GetAllAsync(ct);
            foreach (var m in mappings.OrderBy(m => m.SortOrder))
                if (seen.Add(m.PlaceholderKey))
                    result.Add((m.PlaceholderKey, string.IsNullOrWhiteSpace(m.DisplayName) ? m.PlaceholderKey : m.DisplayName));
        }
        catch (Exception ex)
        {
            _logger.Warn($"خواندن نگاشت ستون‌ها ناموفق بود: {ex.Message}");
        }

        // ستون‌هایی که در داده هست ولی در نگاشت ثبت نشده (اطمینان از پوشش کامل)
        try
        {
            var rows = await GetRowsAsync(ct);
            foreach (var row in rows)
                foreach (var key in row.Values.Keys)
                    if (seen.Add(key)) result.Add((key, key.Replace('_', ' ')));
        }
        catch { /* بی‌اثر */ }

        return result;
    }

    /// <summary>تبدیل هر شخص به یک ردیف تحلیلی شامل تمام فیلدهایش.</summary>
    public async Task<IReadOnlyList<AnalyticsRow>> GetRowsAsync(CancellationToken ct = default)
    {
        var employees = await _employees.GetAllAsync(onlyActive: true, ct);
        var rows = new List<AnalyticsRow>(employees.Count);

        foreach (var e in employees)
        {
            var row = new AnalyticsRow { EmployeeId = e.Id };

            void Put(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) row.Values[key] = value.Trim();
            }

            Put("کد_پرسنلی", e.PersonnelCode);
            Put("شماره_عضویت", e.MembershipNo);
            Put("نام", e.FirstName);
            Put("نام_خانوادگی", e.LastName);
            Put("نام_پدر", e.FatherName);
            Put("کد_ملی", e.NationalId);
            Put("سمت", e.Position);
            Put("واحد", e.Unit);
            Put("شماره_تماس", e.Phone);
            Put("تاریخ_استخدام", e.HireDate);

            // ستون‌های اضافی Excel
            if (!string.IsNullOrWhiteSpace(e.ExtraDataJson))
            {
                try
                {
                    var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(e.ExtraDataJson);
                    if (extra is not null)
                        foreach (var kv in extra) Put(kv.Key, kv.Value);
                }
                catch (JsonException) { /* داده خراب: نادیده */ }
            }

            rows.Add(row);
        }

        return rows;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        string fieldX, string? fieldY = null, int histogramBins = 8,
        string? filterField = null, string? filterValue = null, CancellationToken ct = default)
    {
        var rows = ApplyFilter(await GetRowsAsync(ct), filterField, filterValue);
        var result = new AnalysisResult();

        if (string.IsNullOrWhiteSpace(fieldX))
        {
            result.Title = "فیلدی انتخاب نشده است.";
            return result;
        }

        var xLabel = Pretty(fieldX);

        // --- حالت دو متغیره ---
        if (!string.IsNullOrWhiteSpace(fieldY))
        {
            var yLabel = Pretty(fieldY!);
            var xNumeric = IsNumericField(rows, fieldX);
            var yNumeric = IsNumericField(rows, fieldY!);

            if (xNumeric && yNumeric)
            {
                // پراکندگی + همبستگی + رگرسیون
                foreach (var row in rows)
                {
                    if (TryNum(row, fieldX, out var x) && TryNum(row, fieldY!, out var y))
                        result.Points.Add(new ScatterPoint { X = x, Y = y });
                    else result.MissingCount++;
                }

                result.ValidCount = result.Points.Count;
                result.Title = $"ارتباط «{xLabel}» با «{yLabel}»";

                if (result.Points.Count >= 2)
                {
                    var xs = result.Points.Select(p => p.X).ToArray();
                    var ys = result.Points.Select(p => p.Y).ToArray();
                    result.Correlation = Correlation(xs, ys);
                    var (slope, intercept) = LinearRegression(xs, ys);
                    result.RegressionSlope = slope;
                    result.RegressionIntercept = intercept;
                }

                return result;
            }

            // یکی دسته‌ای است: میانگین متغیر عددی در هر دسته، یا جدول متقاطع
            var categoryField = xNumeric ? fieldY! : fieldX;
            var valueField = xNumeric ? fieldX : fieldY!;
            var valueIsNumeric = xNumeric || yNumeric;

            if (valueIsNumeric)
            {
                var groups = rows
                    .Where(r => r.Values.ContainsKey(categoryField) && TryNum(r, valueField, out _))
                    .GroupBy(r => r.Values[categoryField])
                    .Select(g => new
                    {
                        Label = g.Key,
                        Avg = g.Average(r => { TryNum(r, valueField, out var v); return v; }),
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Avg)
                    .Take(25)
                    .ToList();

                foreach (var g in groups)
                    result.Categories.Add(new CategoryCount
                    {
                        Label = $"{g.Label} ({ToFa(g.Count)})",
                        Count = (int)Math.Round(g.Avg)
                    });

                result.ValidCount = groups.Sum(g => g.Count);
                result.Title = $"میانگین «{Pretty(valueField)}» به تفکیک «{Pretty(categoryField)}»";
                return result;
            }

            // هر دو دسته‌ای: ترکیب دو مقدار
            var pairs = rows
                .Where(r => r.Values.ContainsKey(fieldX) && r.Values.ContainsKey(fieldY!))
                .GroupBy(r => $"{r.Values[fieldX]} — {r.Values[fieldY!]}")
                .OrderByDescending(g => g.Count())
                .Take(25)
                .ToList();

            var totalPairs = pairs.Sum(g => g.Count());
            foreach (var g in pairs)
                result.Categories.Add(new CategoryCount
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Percent = totalPairs == 0 ? 0 : g.Count() * 100.0 / totalPairs
                });

            result.ValidCount = totalPairs;
            result.MissingCount = rows.Count - totalPairs;
            result.Title = $"ترکیب «{xLabel}» و «{yLabel}»";
            return result;
        }

        // --- حالت تک‌متغیره ---
        if (IsNumericField(rows, fieldX))
        {
            var values = new List<double>();
            foreach (var row in rows)
            {
                if (TryNum(row, fieldX, out var v)) values.Add(v);
                else result.MissingCount++;
            }

            result.ValidCount = values.Count;
            result.Title = $"توزیع «{xLabel}»";

            if (values.Count > 0)
            {
                var sorted = values.OrderBy(v => v).ToList();
                var avg = values.Average();
                result.Numeric = new NumericSummary
                {
                    Count = values.Count,
                    Min = sorted[0],
                    Max = sorted[^1],
                    Average = avg,
                    Median = sorted.Count % 2 == 1
                        ? sorted[sorted.Count / 2]
                        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0,
                    StdDev = values.Count < 2 ? 0
                        : Math.Sqrt(values.Sum(v => (v - avg) * (v - avg)) / (values.Count - 1))
                };

                BuildHistogram(result, sorted, histogramBins);
            }

            return result;
        }

        // دسته‌ای: توزیع فراوانی
        var groupsCat = rows
            .Where(r => r.Values.ContainsKey(fieldX))
            .GroupBy(r => r.Values[fieldX])
            .OrderByDescending(g => g.Count())
            .Take(30)
            .ToList();

        var total = groupsCat.Sum(g => g.Count());
        foreach (var g in groupsCat)
            result.Categories.Add(new CategoryCount
            {
                Label = g.Key,
                Count = g.Count(),
                Percent = total == 0 ? 0 : g.Count() * 100.0 / total
            });

        result.ValidCount = total;
        result.MissingCount = rows.Count - total;
        result.Title = $"تعداد افراد به تفکیک «{xLabel}»";
        return result;
    }

    /// <summary>ساخت هیستوگرام از مقادیر عددی مرتب‌شده.</summary>
    private static void BuildHistogram(AnalysisResult result, List<double> sorted, int bins)
    {
        if (sorted.Count == 0) return;
        if (bins < 2) bins = 2;

        var min = sorted[0];
        var max = sorted[^1];

        if (Math.Abs(max - min) < 1e-9)
        {
            result.Categories.Add(new CategoryCount
            {
                Label = ToFa(min), Count = sorted.Count, Percent = 100
            });
            return;
        }

        var width = (max - min) / bins;
        var counts = new int[bins];

        foreach (var v in sorted)
        {
            var idx = (int)((v - min) / width);
            if (idx >= bins) idx = bins - 1;
            if (idx < 0) idx = 0;
            counts[idx]++;
        }

        for (var i = 0; i < bins; i++)
        {
            var lo = min + i * width;
            var hi = lo + width;
            result.Categories.Add(new CategoryCount
            {
                Label = $"{ToFa(lo)} تا {ToFa(hi)}",
                Count = counts[i],
                Percent = sorted.Count == 0 ? 0 : counts[i] * 100.0 / sorted.Count
            });
        }
    }

    public async Task<IReadOnlyList<(string Label, string Value)>> GetHighlightsAsync(
        string? filterField = null, string? filterValue = null, CancellationToken ct = default)
    {
        var rows = ApplyFilter(await GetRowsAsync(ct), filterField, filterValue);

        var countLabel = string.IsNullOrWhiteSpace(filterValue) || filterValue == AllValues
            ? "تعداد کل پرسنل"
            : "تعداد پرسنل (فیلترشده)";

        var list = new List<(string, string)>
        {
            (countLabel, ToFa(rows.Count))
        };

        // میانگین سن — فقط ستونی که واقعاً «سن» است (نه «شناسنامه»)
        var ageKey = FindKey(rows, "سن_فرد", "سن");
        if (ageKey is not null && IsNumericField(rows, ageKey))
        {
            var ages = new List<double>();
            foreach (var r in rows)
                if (TryNum(r, ageKey, out var v) && v is > 0 and < 120) ages.Add(v);

            if (ages.Count > 0)
            {
                list.Add(("میانگین سن", $"{ToFa(Math.Round(ages.Average(), 1))} سال"));
                list.Add(("بازهٔ سنی", $"{ToFa(ages.Min())} تا {ToFa(ages.Max())} سال"));
            }
        }

        // پرتکرارترین استان سکونت
        var provinceKey = FindKey(rows, "استان_محل_سکونت", "استان_محل_تولد");
        if (provinceKey is not null)
            AddTopCategory(list, rows, provinceKey, "بیشترین استان");

        // پرتکرارترین مدرک تحصیلی
        var eduKey = FindKey(rows, "مدرک_تحصیلی", "مدرک");
        if (eduKey is not null)
            AddTopCategory(list, rows, eduKey, "بیشترین مدرک");

        // وضعیت تاهل
        var maritalKey = FindKey(rows, "وضعیت_تاهل", "تاهل");
        if (maritalKey is not null)
        {
            var married = rows.Count(r => r.Values.TryGetValue(maritalKey, out var v)
                                          && v.Contains("متاهل", StringComparison.Ordinal));
            var single = rows.Count(r => r.Values.TryGetValue(maritalKey, out var v)
                                         && v.Contains("مجرد", StringComparison.Ordinal));
            if (married + single > 0)
                list.Add(("متاهل / مجرد", $"{ToFa(married)} / {ToFa(single)}"));
        }

        // نوع عضویت پرتکرار
        var memberKey = FindKey(rows, "نوع_عضویت");
        if (memberKey is not null)
            AddTopCategory(list, rows, memberKey, "بیشترین نوع عضویت");

        // تعداد فیلدهای در دسترس
        var fieldCount = rows.SelectMany(r => r.Values.Keys).Distinct().Count();
        list.Add(("فیلدهای قابل تحلیل", ToFa(fieldCount)));

        return list;
    }

    /// <summary>افزودن «پرتکرارترین مقدار» یک ستون دسته‌ای به فهرست آمارهای کلیدی.</summary>
    private static void AddTopCategory(
        List<(string, string)> list, IReadOnlyList<AnalyticsRow> rows, string key, string label)
    {
        var top = rows.Where(r => r.Values.ContainsKey(key))
                      .GroupBy(r => r.Values[key])
                      .OrderByDescending(g => g.Count())
                      .FirstOrDefault();

        if (top is not null && !string.IsNullOrWhiteSpace(top.Key))
            list.Add((label, $"{top.Key} ({ToFa(top.Count())} نفر)"));
    }

    /// <summary>مقدار ویژه‌ای که یعنی «بدون فیلتر».</summary>
    public const string AllValues = "همه";

    /// <summary>محدود کردن ردیف‌ها به یک مقدار مشخص از یک فیلد (مثلاً یک پروژه).</summary>
    private static IReadOnlyList<AnalyticsRow> ApplyFilter(
        IReadOnlyList<AnalyticsRow> rows, string? field, string? value)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value) || value == AllValues)
            return rows;

        return rows.Where(r => r.Values.TryGetValue(field, out var v)
                               && string.Equals(v, value, StringComparison.Ordinal)).ToList();
    }

    /// <summary>مقادیر متمایز یک فیلد، مرتب‌شده بر اساس تعداد تکرار (پرتکرارترین اول).</summary>
    public async Task<IReadOnlyList<string>> GetDistinctValuesAsync(string field, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(field)) return Array.Empty<string>();

        var rows = await GetRowsAsync(ct);
        return rows.Where(r => r.Values.ContainsKey(field) && !string.IsNullOrWhiteSpace(r.Values[field]))
                   .GroupBy(r => r.Values[field])
                   .OrderByDescending(g => g.Count())
                   .Select(g => g.Key)
                   .ToList();
    }

    // ---------- کمک‌کننده‌ها ----------

    private static readonly (string Key, string Name)[] CoreFields =
    {
        ("کد_پرسنلی", "کد پرسنلی"), ("شماره_عضویت", "شماره عضویت"),
        ("نام", "نام"), ("نام_خانوادگی", "نام خانوادگی"), ("نام_پدر", "نام پدر"),
        ("کد_ملی", "کد ملی"), ("سمت", "سمت"), ("واحد", "واحد"),
        ("شماره_تماس", "شماره تماس"), ("تاریخ_استخدام", "تاریخ استخدام")
    };

    /// <summary>
    /// یافتن کلید مناسب بر اساس «واژهٔ کامل»، نه رشتهٔ جزئی.
    /// مثال مهم: جستجوی «سن» نباید «شماره_شناسنامه» را برگرداند (چون «شناسنامه» شامل
    /// حروف س و ن پشت سر هم است). به همین دلیل کلید به واژه‌های جداشده با زیرخط
    /// تقسیم و مقایسه می‌شود.
    /// </summary>
    private static string? FindKey(IReadOnlyList<AnalyticsRow> rows, params string[] words)
    {
        var keys = rows.SelectMany(r => r.Values.Keys).Distinct().ToList();

        // اولویت ۱: تطبیق دقیق کل کلید
        foreach (var w in words)
        {
            var exact = keys.FirstOrDefault(k => string.Equals(k, w, StringComparison.Ordinal));
            if (exact is not null) return exact;
        }

        // اولویت ۲: یکی از واژه‌های کلید دقیقاً برابر واژهٔ موردنظر باشد
        foreach (var w in words)
        {
            var tokenMatch = keys.FirstOrDefault(k =>
                k.Split('_', StringSplitOptions.RemoveEmptyEntries)
                 .Any(t => string.Equals(t, w, StringComparison.Ordinal)));
            if (tokenMatch is not null) return tokenMatch;
        }

        return null;
    }

    /// <summary>
    /// تشخیص عددی بودن یک ستون: اگر دست‌کم ۷۰٪ مقادیر موجود عددی باشند.
    /// ارقام فارسی هم به انگلیسی تبدیل و در نظر گرفته می‌شوند.
    /// </summary>
    private static bool IsNumericField(IReadOnlyList<AnalyticsRow> rows, string field)
    {
        var present = 0; var numeric = 0;
        foreach (var row in rows)
        {
            if (!row.Values.TryGetValue(field, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            present++;
            if (TryParseNumber(raw, out _)) numeric++;
        }
        return present > 0 && numeric >= present * 0.7;
    }

    private static bool TryNum(AnalyticsRow row, string field, out double value)
    {
        value = 0;
        return row.Values.TryGetValue(field, out var raw) && TryParseNumber(raw, out value);
    }

    /// <summary>تجزیهٔ عدد با پشتیبانی از ارقام فارسی و جداکنندهٔ هزارگان.</summary>
    private static bool TryParseNumber(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = PersianDate.ToEnglishDigits(raw.Trim()).Replace(",", "").Replace("٬", "");

        // تاریخ‌های شمسی مثل 1400/01/01 نباید عدد در نظر گرفته شوند.
        if (s.Contains('/') || s.Contains('-')) return false;

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static double Correlation(double[] xs, double[] ys)
    {
        var n = xs.Length;
        var mx = xs.Average(); var my = ys.Average();
        double cov = 0, vx = 0, vy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - mx; var dy = ys[i] - my;
            cov += dx * dy; vx += dx * dx; vy += dy * dy;
        }
        if (vx <= 0 || vy <= 0) return 0;
        return cov / Math.Sqrt(vx * vy);
    }

    private static (double Slope, double Intercept) LinearRegression(double[] xs, double[] ys)
    {
        var n = xs.Length;
        var mx = xs.Average(); var my = ys.Average();
        double num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - mx;
            num += dx * (ys[i] - my);
            den += dx * dx;
        }
        var slope = Math.Abs(den) < 1e-12 ? 0 : num / den;
        return (slope, my - slope * mx);
    }

    private static string Pretty(string key) => key.Replace('_', ' ');

    private static string ToFa(double v)
    {
        var s = Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
        // ممیز فارسی برای نمایش درست اعداد اعشاری
        return PersianDate.ToPersianDigits(s).Replace('.', '\u066B');
    }
}
