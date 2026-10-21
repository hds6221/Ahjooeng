using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

namespace StormLegend
{
    /// <summary>
    /// 미리보기 그리기. 템플릿 전체의 표시용 도형을 한 번만 읽어 모든 공종 화면이 함께 씁니다.
    /// 색·글꼴·기호 범위는 캐시해 다시 그릴 때 계산하지 않습니다.
    /// </summary>
    public sealed class PreviewCatalog
    {
        private readonly Dictionary<string, List<PreviewPrimitive>> lookup;
        private readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SymbolShape> symbols = new Dictionary<string, SymbolShape>(StringComparer.Ordinal);
        private static readonly List<PreviewPrimitive> Empty = new List<PreviewPrimitive>();

        private PreviewCatalog(Dictionary<string, List<PreviewPrimitive>> lookup) { this.lookup = lookup; }

        public static PreviewCatalog Load(string path, IEnumerable<TemplateManifest> manifests)
        {
            var data = PreviewCatalogFile.Load(path);
            if (data == null) throw new InvalidDataException("기호 미리보기 데이터가 비어 있습니다.");
            data.Validate(manifests);
            var lookup = new Dictionary<string, List<PreviewPrimitive>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in data.Entities) lookup[entity.Handle] = entity.Primitives ?? Empty;
            return new PreviewCatalog(lookup);
        }

        /// <summary>미리보기를 쓸 수 없을 때(파일 없음·손상). 항목 선택·생성은 그대로 할 수 있고 그림만 비어 있습니다.</summary>
        public static PreviewCatalog Unavailable(string reason)
        {
            return new PreviewCatalog(new Dictionary<string, List<PreviewPrimitive>>(StringComparer.OrdinalIgnoreCase)) { UnavailableReason = reason ?? "미리보기 데이터를 읽지 못했습니다." };
        }

        /// <summary>null이면 정상. 값이 있으면 미리보기를 쓸 수 없는 이유.</summary>
        public string UnavailableReason { get; private set; }

        public static PreviewCatalog Load(string path, TemplateManifest manifest) { return Load(path, new[] { manifest }); }

        private static string cachedKey;
        private static PreviewCatalog cached;

        /// <summary>같은 파일이면 앞서 읽은 결과를 다시 씁니다(LEGEND를 다시 열 때 빠름). 파일이 바뀌면 새로 읽습니다.</summary>
        public static PreviewCatalog LoadCached(string path, IEnumerable<TemplateManifest> manifests)
        {
            var list = manifests.ToList();
            var file = new FileInfo(path);
            string key = file.FullName + "|" + (file.Exists ? file.Length + "|" + file.LastWriteTimeUtc.Ticks : "") + "|" + string.Join(",", list.Select(m => m.SourceSha256).Distinct());
            if (cached != null && key == cachedKey) return cached;
            var loaded = Load(path, list);
            cached = loaded; cachedKey = key;
            return loaded;
        }

        private List<PreviewPrimitive> Get(string handle)
        {
            List<PreviewPrimitive> list;
            return handle != null && lookup.TryGetValue(handle, out list) ? list : Empty;
        }

        private sealed class SymbolShape
        {
            public List<PreviewPrimitive> Shapes; public double MinX, MinY, MaxX, MaxY;
        }

        private SymbolShape Symbol(LegendRow row)
        {
            SymbolShape shape;
            if (symbols.TryGetValue(row.Id, out shape)) return shape;
            var shapes = row.SymbolHandles.SelectMany(Get).ToList();
            var points = shapes.SelectMany(p => p.IsText
                ? (IEnumerable<PreviewPoint>)new[] { new PreviewPoint { X = p.X - p.Height * (p.Text ?? "").Length * .35, Y = p.Y - p.Height }, new PreviewPoint { X = p.X + p.Height * (p.Text ?? "").Length * .35, Y = p.Y + p.Height } }
                : (IEnumerable<PreviewPoint>)(p.Contours != null ? p.Contours.SelectMany(c => c).ToList() : p.Points ?? new List<PreviewPoint>())).ToList();
            shape = new SymbolShape { Shapes = shapes };
            if (points.Count > 0) { shape.MinX = points.Min(p => p.X); shape.MaxX = points.Max(p => p.X); shape.MinY = points.Min(p => p.Y); shape.MaxY = points.Max(p => p.Y); }
            else shape.Shapes = Empty;
            symbols[row.Id] = shape;
            return shape;
        }

        public bool HasSymbol(LegendRow row) { return Symbol(row).Shapes.Count > 0; }

        /// <summary>흰 바탕 기호 그림(목록·표에 넣는 작은 이미지). 기호가 없으면 "없음"을 씁니다.</summary>
        public Bitmap RenderSymbol(LegendRow row, int width, int height)
        {
            var image = new Bitmap(Math.Max(1, width), Math.Max(1, height));
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.White);
                if (row != null && HasSymbol(row)) DrawSymbol(g, row, new RectangleF(0, 0, width, height));
                else if (row != null) System.Windows.Forms.TextRenderer.DrawText(g, "없음", CadUi.CadTheme.Small, new Rectangle(0, 0, width, height), CadUi.CadTheme.Ink, System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter);
            }
            return image;
        }

        public void DrawSymbol(Graphics g, LegendRow row, RectangleF bounds)
        {
            var shape = Symbol(row);
            if (shape.Shapes.Count == 0) return;
            // 작은 칸에서도 기호가 칸을 거의 채우도록 여백을 칸 크기에 비례해 줄입니다(최소 2px).
            float pad = Math.Max(2F, Math.Min(bounds.Width, bounds.Height) * .08F);
            float scale = (float)Math.Min((bounds.Width - 2 * pad) / Math.Max(1, shape.MaxX - shape.MinX), (bounds.Height - 2 * pad) / Math.Max(1, shape.MaxY - shape.MinY));
            float offsetX = bounds.Left + (bounds.Width - (float)(shape.MaxX - shape.MinX) * scale) / 2 - (float)shape.MinX * scale;
            float offsetY = bounds.Top + (bounds.Height - (float)(shape.MaxY - shape.MinY) * scale) / 2 + (float)shape.MaxY * scale;
            var state = g.Save(); g.SetClip(bounds); g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var fonts = new FontCache()) foreach (var p in shape.Shapes) Draw(g, fonts, p, scale, offsetX, offsetY);
            g.Restore(state);
        }

        /// <summary>선택한 행(빈 행 포함)을 선택 순서대로 실제 생성 모양과 같게 그립니다.</summary>
        public void DrawLegend(Graphics g, TemplateManifest m, IList<LegendSelection> selection, float scale, float offsetX, float offsetY)
        {
            if (selection == null || selection.Count == 0) return;
            var rows = m.ResolveRows(selection);
            var changes = selection.ToDictionary(s => s.ItemId);
            float x = offsetX - (float)m.Origin.X * scale, y = offsetY + (float)m.Origin.Y * scale;
            double bottom = m.RowsTopY - rows.Sum(r => r.Height);
            bool resized = Math.Abs(bottom - m.SourceBottomY) > .01;
            using (var fonts = new FontCache())
            {
                foreach (string h in m.HeaderHandles) DrawHandle(g, fonts, h, scale, x, y);
                foreach (string h in m.VerticalBorderHandles.Concat(m.AuxBorderHandles)) DrawBorder(g, fonts, h, scale, x, y, resized ? bottom : (double?)null);
                DrawBorder(g, fonts, m.OuterBorderHandle, scale, x, y, resized ? bottom - m.BottomMargin : (double?)null);
                double top = m.RowsTopY;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i]; double center = top - row.Height / 2;
                    if (!row.IsBlank)
                    {
                        var change = changes[row.Id];
                        foreach (var cell in row.Cells)
                        {
                            string label = cell.Column == 1 ? change.NameOverride : cell.Column == 2 ? change.SpecOverride : null;
                            if (label != null)
                            {
                                var prototype = cell.Handles.SelectMany(Get).FirstOrDefault(p => p.IsText) ?? row.Cells.SelectMany(c => c.Handles).SelectMany(Get).FirstOrDefault(p => p.IsText);
                                var edited = new PreviewPrimitive { Kind = "text", Color = prototype != null ? prototype.Color : "#253347", X = cell.CenterX, Y = center, Text = label,
                                    Height = prototype != null ? prototype.Height : 150, WidthFactor = prototype != null ? prototype.WidthFactor : 1, Horizontal = 1, Vertical = 2 };
                                Draw(g, fonts, edited, scale, x, y, 0, label, null, cell.Width);
                            }
                            foreach (string h in cell.Handles) foreach (var p in Get(h)) if (label == null || !p.IsText) Draw(g, fonts, p, scale, x, y, center - row.SourceCenterOf(cell));
                        }
                        if (m.HasQuantityColumn)
                        {
                            var q = Get(row.QuantityHandle).FirstOrDefault(p => p.IsText);
                            if (q != null) DrawHandle(g, fonts, row.QuantityHandle, scale, x + (float)(m.QuantityCenterX - q.X) * scale, y, center - row.QuantitySourceCenterY, change.Quantity ?? "-");
                        }
                    }
                    top -= row.Height;
                    if (i < rows.Count - 1 && !string.IsNullOrEmpty(m.FullSeparatorHandle)) DrawHandle(g, fonts, m.FullSeparatorHandle, scale, x, y, top - m.FullSeparatorY);
                }
            }
        }

        private void DrawHandle(Graphics g, FontCache fonts, string handle, float scale, float x, float y, double dy = 0, string text = null)
        { foreach (var p in Get(handle)) Draw(g, fonts, p, scale, x, y, dy, text); }

        private void DrawBorder(Graphics g, FontCache fonts, string handle, float scale, float x, float y, double? newBottom)
        {
            foreach (var p in Get(handle))
            {
                if (newBottom == null || p.Points == null || p.Points.Count == 0) { Draw(g, fonts, p, scale, x, y); continue; }
                double low = p.Points.Min(pt => pt.Y), target = newBottom.Value;
                Draw(g, fonts, p, scale, x, y, 0, null, v => Math.Abs(v - low) < .01 ? target : v);
            }
        }

        private Color ColorOf(string html)
        {
            Color color;
            if (html == null) return CadUi.CadTheme.Ink;
            if (!colors.TryGetValue(html, out color))
            {
                try { color = OnPaper(ColorTranslator.FromHtml(html)); } catch (Exception) { color = CadUi.CadTheme.Ink; }
                colors[html] = color;
            }
            return color;
        }

        /// <summary>검은 CAD 화면용 밝은 색(노랑·하늘·연두 등)은 흰 종이에서 흐려 보이므로, 색상은 그대로 두고 밝기만 낮춥니다.</summary>
        internal static Color OnPaper(Color color)
        {
            double luminance = (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255;
            const double limit = .62;
            if (luminance <= limit) return color;
            double k = limit / luminance;
            return Color.FromArgb(color.A, (int)Math.Round(color.R * k), (int)Math.Round(color.G * k), (int)Math.Round(color.B * k));
        }

        private void Draw(Graphics g, FontCache fonts, PreviewPrimitive p, float scale, float offsetX, float offsetY, double dy = 0, string overrideText = null, Func<double, double> mapY = null, double maxTextWidth = 650)
        {
            Color color = ColorOf(p.Color);
            if (p.IsText)
            {
                string text = overrideText ?? p.Text;
                if (string.IsNullOrEmpty(text)) return;
                float height = Math.Max(.9F, (float)p.Height * scale * 1.22F);
                if (height < 1.2F) return; // 너무 작아 보이지 않는 글자는 건너뜁니다.
                var font = fonts.Get(height);
                using (var brush = new SolidBrush(color))
                {
                    var state = g.Save();
                    g.TranslateTransform(offsetX + (float)p.X * scale, offsetY - (float)(p.Y + dy) * scale);
                    g.RotateTransform(-(float)p.Rotation);
                    float widthFactor = (float)(p.WidthFactor > 0 ? p.WidthFactor : 1);
                    var measured = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
                    if (overrideText != null) widthFactor = Math.Min(widthFactor, (float)maxTextWidth * scale / Math.Max(1, measured.Width));
                    g.ScaleTransform(widthFactor, 1);
                    float x = p.Horizontal == 1 ? -measured.Width / 2 : p.Horizontal == 2 ? -measured.Width : 0;
                    float y = p.Vertical == 2 ? -measured.Height / 2 : p.Vertical == 3 ? 0 : -measured.Height;
                    g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic); g.Restore(state);
                }
                return;
            }
            if (p.Points == null || p.Points.Count < 2) return;
            var points = new PointF[p.Points.Count];
            for (int i = 0; i < points.Length; i++)
            {
                var pt = p.Points[i];
                points[i] = new PointF(offsetX + (float)pt.X * scale, offsetY - (float)((mapY == null ? pt.Y : mapY(pt.Y)) + dy) * scale);
            }
            if (p.Contours != null && p.Contours.Count > 0)
            {
                using (var path = new GraphicsPath(FillMode.Alternate))
                using (var pen = new Pen(color, 1.15F))
                {
                    foreach (var contour in p.Contours)
                        path.AddPolygon(contour.Select(pt => new PointF(offsetX + (float)pt.X * scale,
                            offsetY - (float)((mapY == null ? pt.Y : mapY(pt.Y)) + dy) * scale)).ToArray());
                    if (p.Fill == "solid") using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
                    else if (p.Fill == "hatch" && p.Pattern != null && p.Pattern.Count > 0) DrawPattern(g, path, p.Pattern, color, scale, offsetX, offsetY, dy);
                    else if (p.Fill == "hatch") using (var brush = new HatchBrush(HatchStyle.ForwardDiagonal, color, Color.White)) g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
                return;
            }
            using (var pen = new Pen(color, 1.15F))
            {
                if (p.Dash) pen.DashPattern = new[] { 6F, 2F, 1F, 2F };
                if (p.Closed && points.Length > 2)
                {
                    if (p.Fill == "solid") using (var brush = new SolidBrush(color)) g.FillPolygon(brush, points);
                    else if (p.Fill == "hatch") using (var brush = new HatchBrush(HatchStyle.ForwardDiagonal, color, Color.White)) g.FillPolygon(brush, points);
                    g.DrawPolygon(pen, points);
                }
                else g.DrawLines(pen, points);
            }
        }

        /// <summary>
        /// 해치 패턴 선을 실제 각도·간격으로 그립니다(경계 안으로 잘라 냄). 화면에서 선 간격이 1.5px보다 좁거나
        /// 선이 400개를 넘으면 선이 뭉개지므로 옅은 색으로 채웁니다.
        /// </summary>
        private static void DrawPattern(Graphics g, GraphicsPath path, List<PreviewHatchLine> pattern, Color color, float scale, float offsetX, float offsetY, double dy)
        {
            var bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            try
            {
                foreach (var family in pattern)
                {
                    double rad = family.Angle * Math.PI / 180;
                    // 화면 좌표(y 아래쪽)로 바꾼 기준점·선 방향·반복 간격
                    double bx = offsetX + family.X * scale, by = offsetY - (family.Y + dy) * scale;
                    double ox = family.OffsetX * scale, oy = -family.OffsetY * scale;
                    double dirX = Math.Cos(rad), dirY = -Math.Sin(rad);
                    double nx = -dirY, ny = dirX, step = ox * nx + oy * ny; // 선과 수직인 간격(부호 포함)
                    if (Math.Abs(step) < 1.5) { Tint(g, path, color); continue; }
                    var corners = new[] { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top), new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Right, bounds.Bottom) };
                    double baseN = bx * nx + by * ny;
                    double k1 = corners.Min(c => (c.X * nx + c.Y * ny - baseN) / step), k2 = corners.Max(c => (c.X * nx + c.Y * ny - baseN) / step);
                    int from = (int)Math.Floor(Math.Min(k1, k2)) - 1, to = (int)Math.Ceiling(Math.Max(k1, k2)) + 1;
                    if (to - from > 400) { Tint(g, path, color); continue; }
                    double reach = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) + Math.Abs(ox) + Math.Abs(oy) + 10;
                    var dashPattern = DashPattern(family.Dashes, scale);
                    double period = dashPattern == null ? 0 : dashPattern.Sum();
                    using (var pen = new Pen(color, 1F))
                    {
                        if (dashPattern != null) pen.DashPattern = dashPattern;
                        for (int k = from; k <= to; k++)
                        {
                            double px = bx + ox * k, py = by + oy * k;
                            double along = (bounds.Left + bounds.Width / 2 - px) * dirX + (bounds.Top + bounds.Height / 2 - py) * dirY;
                            double start = along - reach;
                            if (period > 0) start = Math.Floor(start / period) * period; // 무늬 위상을 기준점에 맞춤
                            g.DrawLine(pen, (float)(px + dirX * start), (float)(py + dirY * start), (float)(px + dirX * (along + reach)), (float)(py + dirY * (along + reach)));
                        }
                    }
                }
            }
            finally { g.Restore(state); }
        }

        private static void Tint(Graphics g, GraphicsPath path, Color color) { using (var brush = new SolidBrush(Color.FromArgb(70, color))) g.FillPath(brush, path); }

        /// <summary>패턴 무늬(도면 단위: + 선, − 틈, 0 점)를 GDI+ 무늬(펜 두께 배수, 선·틈 번갈아)로 바꿉니다. 실선이면 null.</summary>
        private static float[] DashPattern(List<double> dashes, float scale)
        {
            if (dashes == null || dashes.Count == 0 || dashes.All(d => d > 0)) return null;
            var runs = new List<float>(); bool? drawing = null;
            foreach (double d in dashes)
            {
                bool isDash = d >= 0; float length = Math.Max(0.6F, (float)(Math.Abs(d) * scale));
                if (drawing == isDash) runs[runs.Count - 1] += length; else { runs.Add(length); drawing = isDash; }
            }
            if (dashes[0] < 0) { runs.Add(runs[0]); runs.RemoveAt(0); } // GDI+ 무늬는 선부터 시작
            if (runs.Count % 2 == 1) { if (runs.Count == 1) return null; runs[0] += runs[runs.Count - 1]; runs.RemoveAt(runs.Count - 1); }
            return runs.Count >= 2 ? runs.ToArray() : null;
        }

        /// <summary>한 번 그리는 동안 같은 크기 글꼴을 재사용합니다.</summary>
        private sealed class FontCache : IDisposable
        {
            private readonly Dictionary<int, Font> fonts = new Dictionary<int, Font>();
            public Font Get(float pixels)
            {
                int key = (int)Math.Round(pixels * 4);
                Font font;
                if (!fonts.TryGetValue(key, out font)) { font = new Font("Arial", key / 4F, FontStyle.Regular, GraphicsUnit.Pixel); fonts.Add(key, font); }
                return font;
            }
            public void Dispose() { foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); }
        }
    }
}
