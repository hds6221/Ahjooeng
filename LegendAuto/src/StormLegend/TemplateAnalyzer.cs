using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace StormLegend
{
    public sealed class TemplateAnalysis
    {
        public CatalogIndex Index { get; internal set; }
        public IList<TemplateManifest> Manifests { get; internal set; }
        public PreviewCatalogFile Preview { get; internal set; }
        /// <summary>사용자에게 알려줄 주의 사항(예: "부대 36행: 명칭이 없어 건너뜀").</summary>
        public IList<string> Warnings { get; internal set; }

        /// <summary>catalog-index.json, categories/*.json, preview-catalog.json을 씁니다.</summary>
        public void WriteTo(string directory)
        {
            Directory.CreateDirectory(Path.Combine(directory, "categories"));
            foreach (var manifest in Manifests) manifest.Save(Path.Combine(directory, "categories", manifest.CategoryId + ".json"));
            Preview.Save(Path.Combine(directory, CatalogIndex.PreviewFileName));
            Index.Save(Path.Combine(directory, CatalogIndex.FileName));
            File.WriteAllLines(Path.Combine(directory, "analysis-report.txt"),
                new[] { "범례 템플릿 분석 결과", "원본: " + Index.SourceDwg + " (" + Index.SourceDwgSha256 + ")" }
                .Concat(Index.Categories.Select(c => "  " + c.Name + ": " + c.ItemCount + (c.Id == "watershed" ? "세트" : "개")))
                .Concat(Warnings.Count == 0 ? new[] { "주의 사항 없음" } : new[] { "주의 사항:" }.Concat(Warnings.Select(w => "  - " + w))));
        }
    }

    /// <summary>
    /// 범례 템플릿 DWG의 공종 표(Defpoints 외곽선 안)를 읽어 항목·칸·테두리 연결 정보와 미리보기 도형을 만듭니다.
    /// 표의 빈 행에 내용을 채우거나 마지막 행을 아래로 복사해 고치면 그 행이 새 항목으로 등록됩니다.
    /// </summary>
    public static class TemplateAnalyzer
    {
        public const double RowHeight = 450;
        private const double Tolerance = .1;
        /// <summary>외곽선 아래로 늘려 그린 행도 같은 공종으로 인정하는 범위(행 60개).</summary>
        private const double ExtraBelow = RowHeight * 60;

        private static readonly string[][] KnownCategories =
        {
            new[] { "construction", "공사" }, new[] { "watershed", "유역" }, new[] { "storm", "우수" }, new[] { "sewage", "오수" },
            new[] { "water", "상수" }, new[] { "paving", "포장" }, new[] { "auxiliary", "부대" }
        };

        public static TemplateAnalysis Analyze(Database db, string sourceFile, string sourceSha256, string sourceDwgName, string sourceDwgSha256)
        {
            var warnings = new List<string>();
            var manifests = new List<TemplateManifest>();
            var preview = new PreviewCatalogFile { SchemaVersion = 1, SourceSha256 = sourceSha256, Entities = new List<PreviewEntity>() };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var all = new List<Item>();
                foreach (ObjectId id in model)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity != null) all.Add(Item.From(entity));
                }
                var frames = all.Where(e => e.HasExtents && e.Type == "LWPOLYLINE" && e.Layer == "Defpoints" && e.MaxY - e.MinY > 1000).OrderBy(e => e.MinX).ToList();
                if (frames.Count == 0) throw new InvalidDataException("템플릿에서 공종 표 외곽선(Defpoints 레이어)을 찾지 못했습니다.");
                var used = new HashSet<string>();
                for (int k = 0; k < frames.Count; k++)
                {
                    var frame = frames[k];
                    var own = all.Where(e => e.HasExtents && e.MinX >= frame.MinX - Tolerance && e.MaxX <= frame.MaxX + Tolerance && e.MaxY <= frame.MaxY + Tolerance && e.MinY >= frame.MinY - ExtraBelow).ToList();
                    string label = "표 " + (k + 1);
                    try
                    {
                        var manifest = AnalyzeTable(frame, own, frames.Count == KnownCategories.Length ? k : -1, sourceFile, sourceSha256, warnings);
                        if (!used.Add(manifest.CategoryId)) { warnings.Add(manifest.CategoryName + ": 같은 공종 표가 두 개 있어 오른쪽 표는 건너뜁니다."); continue; }
                        label = manifest.CategoryName;
                        manifest.Validate();
                        manifests.Add(manifest);
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException)
                    {
                        warnings.Add(label + ": 표를 읽지 못해 제외했습니다 — " + ex.Message);
                    }
                }
                if (manifests.Count == 0) throw new InvalidDataException("읽을 수 있는 공종 표가 없습니다. " + string.Join(" / ", warnings));
                var layers = new LayerCache(tr, db);
                var wanted = new HashSet<string>(manifests.SelectMany(m => m.AllHandles), StringComparer.OrdinalIgnoreCase);
                foreach (var item in all.Where(e => wanted.Contains(e.Handle)))
                {
                    var entity = (Entity)tr.GetObject(item.Id, OpenMode.ForRead);
                    var primitives = new List<PreviewPrimitive>();
                    try { Flatten(entity, layers, "0", new Paint(7), 0, primitives); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException)) { warnings.Add("미리보기 생략(" + item.Type + " " + item.Handle + "): " + ex.Message); primitives.Clear(); }
                    preview.Entities.Add(new PreviewEntity { Handle = item.Handle, Primitives = primitives });
                }
                tr.Commit();
            }
            manifests = manifests.OrderBy(m => { int rank = Array.IndexOf(LegendComposer.CategoryOrder, m.CategoryId); return rank < 0 ? int.MaxValue : rank; }).ToList();
            var index = new CatalogIndex
            {
                SchemaVersion = 1, SourceDwg = sourceDwgName, SourceDwgSha256 = sourceDwgSha256,
                DefaultCategoryId = manifests.Any(m => m.CategoryId == "storm") ? "storm" : manifests[0].CategoryId,
                Categories = manifests.Select(m => new CatalogEntry { Id = m.CategoryId, Name = m.CategoryName, ManifestFile = "categories/" + m.CategoryId + ".json", ItemCount = m.Rows.Count }).ToList()
            };
            return new TemplateAnalysis { Index = index, Manifests = manifests, Preview = preview, Warnings = warnings };
        }

        // ---------------------------------------------------------------- 표 구조 분석

        private static TemplateManifest AnalyzeTable(Item frame, List<Item> own, int knownIndex, string sourceFile, string sourceSha256, List<string> warnings)
        {
            var wipe = own.FirstOrDefault(e => e.Type == "WIPEOUT");
            if (wipe == null) throw new InvalidDataException("표 바탕 가림(WIPEOUT)이 없습니다.");
            string id, name; IdentifyCategory(wipe, own, knownIndex, out id, out name);
            double left = wipe.MinX, right = wipe.MaxX, top = wipe.MaxY, bottom = wipe.MinY;
            var border = own.Where(e => e.Layer.Contains("범례") && (e.Type == "LINE" || e.Type == "LWPOLYLINE")).ToList();
            var m = new TemplateManifest
            {
                SchemaVersion = TemplateManifest.SupportedSchema, CategoryId = id, CategoryName = name, SourceFile = sourceFile, SourceSha256 = sourceSha256,
                BaseScaleDenominator = 100, Origin = new ManifestPoint { X = frame.MinX, Y = frame.MaxY }, FrameWidth = frame.MaxX - frame.MinX,
                FrameBottomY = frame.MinY, BottomMargin = bottom - frame.MinY, TableWidth = right - left, TableLeftX = left, TableRightX = right,
                TitleTopY = top, SourceBottomY = bottom, RowHeight = RowHeight, OuterBorderHandle = frame.Handle, WipeoutHandle = wipe.Handle,
                AllHandles = own.Select(e => e.Handle).ToList(), Groups = new List<LegendGroup>(), Rows = new List<LegendRow>(), HeaderHandles = new List<string>(),
                VerticalBorderHandles = new List<string>(), AuxBorderHandles = new List<string>(), RowBoundaryHandles = new List<string>(), SplitSeparatorHandles = new List<string>()
            };
            if (id == "watershed") AnalyzeSet(m, frame, wipe, own, border);
            else AnalyzeRows(m, frame, wipe, own, border, warnings);
            if (m.Rows.Count == 0) throw new InvalidDataException("등록할 항목 행이 없습니다.");
            return m;
        }

        private static void IdentifyCategory(Item wipe, List<Item> own, int knownIndex, out string id, out string name)
        {
            // 레이어 이름 "C-우수-범례-SO"의 가운데 글자로 공종을 찾습니다.
            var names = new[] { wipe }.Concat(own).Select(e => Regex.Match(e.Layer, @"^C-([^-]+)-범례")).Where(x => x.Success).Select(x => x.Groups[1].Value).ToList();
            name = names.GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (name == null && knownIndex >= 0) name = KnownCategories[knownIndex][1];
            if (name == null) throw new InvalidDataException("공종 이름을 알 수 없습니다. 표 레이어를 'C-공종-범례' 형식으로 두세요.");
            string found = name;
            var known = KnownCategories.FirstOrDefault(c => c[1] == found);
            id = known != null ? known[0] : "custom-" + string.Concat(found.Select(ch => ((int)ch).ToString("x4")));
        }

        /// <summary>유역: 관로·맨홀·경계가 연결된 설명 도형이라 전체를 한 세트로 둡니다.</summary>
        private static void AnalyzeSet(TemplateManifest m, Item frame, Item wipe, List<Item> own, List<Item> border)
        {
            var aux = border.Where(e => e.Type == "LWPOLYLINE" && e.MaxX - e.MinX > m.TableWidth * .99 && e.MaxY - e.MinY > 900).OrderByDescending(e => e.MaxY).FirstOrDefault();
            if (aux == null) throw new InvalidDataException("유역 설명 영역 테두리를 찾지 못했습니다.");
            double bodyTop = aux.MaxY;
            var header = own.Where(e => e != frame && e != wipe && e != aux && e.MinY >= bodyTop - .01).ToList();
            var content = own.Where(e => e != frame && e != wipe && e != aux && !header.Contains(e)).Select(e => e.Handle).ToList();
            m.RowsTopY = bodyTop; m.HeaderTopY = bodyTop; m.HasQuantityColumn = false; m.QuantityCenterX = 0; m.QuantityCellWidth = 650;
            m.FullSeparatorHandle = null; m.FullSeparatorY = bodyTop; m.AuxBorderHandles = new List<string> { aux.Handle }; m.HeaderHandles = header.Select(e => e.Handle).ToList();
            m.Rows.Add(new LegendRow
            {
                Id = "watershed_annotation_set", Name = "유역 표기 세트", Spec = "관로·맨홀·유역경계·유역면적", Remark = "연결된 원본 설명 도형 전체", Quantity = "-", Unit = "",
                GroupId = "watershed_annotation_set", SourceTopY = bodyTop, Height = bodyTop - m.SourceBottomY, ContentHandles = content, SymbolHandles = content.ToList(),
                QuantityHandle = null, QuantitySourceCenterY = 0,
                Cells = new List<LegendSourceCell> { new LegendSourceCell { Column = 0, Handles = content.ToList(), SourceCenterY = (bodyTop + m.SourceBottomY) / 2 } }
            });
        }

        private sealed class Segment { public double A, B, Y; public string Handle; }

        /// <summary>일반 표: 열 6개(기호·명칭·규격·단위·수량·비고), 행 높이 450.</summary>
        private static void AnalyzeRows(TemplateManifest m, Item frame, Item wipe, List<Item> own, List<Item> border, List<string> warnings)
        {
            double rowTop = m.TitleTopY - 2 * RowHeight, bottom = m.SourceBottomY;
            m.RowsTopY = rowTop; m.HeaderTopY = m.TitleTopY - RowHeight; m.HasQuantityColumn = true;
            var vertical = border.Where(e => e.MaxX - e.MinX < .01 && e.MaxY - e.MinY > (rowTop - bottom) * .8).ToList();
            var xs = Unique(vertical.Select(e => (e.MinX + e.MaxX) / 2));
            if (xs.Count != 5) throw new InvalidDataException("열 구분 세로선 5개가 필요합니다(현재 " + xs.Count + "개).");
            var bounds = new List<double> { m.TableLeftX }; bounds.AddRange(xs); bounds.Add(m.TableRightX);
            m.VerticalBorderHandles = vertical.Select(e => e.Handle).ToList();
            // '범례' 레이어 선 중 표 구조(열 경계 위 세로선·행 경계 위 가로선·보조 테두리·머리글)만 테두리로 봅니다.
            // 표 레이어를 그대로 쓴 채 그린 기호(예: 기호 칸 안의 사각형 폴리선)는 내용으로 남깁니다.
            Func<double, bool> onRowLine = y => { double k = (rowTop - y) / RowHeight; return Math.Abs(k - Math.Round(k)) * RowHeight < Tolerance; };
            Func<double, bool> onColumnLine = x => bounds.Any(b => Math.Abs(b - x) < Tolerance);
            Func<Point2d[], bool> structural = s => Math.Abs(s[0].Y - s[1].Y) < .01 ? onRowLine((s[0].Y + s[1].Y) / 2)
                : Math.Abs(s[0].X - s[1].X) < .01 && onColumnLine((s[0].X + s[1].X) / 2);
            border = border.Where(e => e.MinY >= rowTop - Tolerance || vertical.Contains(e)
                || (e.MaxX - e.MinX > m.TableWidth * .99 && e.MaxY - e.MinY > 900)
                || (e.Segments.Count > 0 && e.Segments.All(structural))).ToList();
            m.QuantityCenterX = (bounds[4] + bounds[5]) / 2; m.QuantityCellWidth = bounds[5] - bounds[4] - 100;
            m.AuxBorderHandles = border.Where(e => e.MaxX - e.MinX > m.TableWidth * .99 && e.MaxY - e.MinY > 900).Select(e => e.Handle).ToList();
            m.HeaderHandles = own.Where(e => e != frame && e != wipe && e.MinY >= rowTop - .01).Select(e => e.Handle).ToList();
            var horizontal = new List<Segment>();
            foreach (var e in border)
                foreach (var s in e.Segments)
                    if (Math.Abs(s[0].Y - s[1].Y) < .01 && Math.Min(s[0].Y, s[1].Y) < rowTop + .01)
                        horizontal.Add(new Segment { A = Math.Min(s[0].X, s[1].X), B = Math.Max(s[0].X, s[1].X), Y = (s[0].Y + s[1].Y) / 2, Handle = e.Handle });
            var full = horizontal.FirstOrDefault(v => v.B - v.A > m.TableWidth * .99 && own.First(e => e.Handle == v.Handle).Type == "LINE");
            if (full != null) { m.FullSeparatorHandle = full.Handle; m.FullSeparatorY = full.Y; }
            else warnings.Add(m.CategoryName + ": 행 구분선(표 폭 전체 LINE)을 찾지 못해 행 사이 선 없이 생성됩니다.");
            m.RowBoundaryHandles = horizontal.Select(v => v.Handle).Distinct().ToList();
            var excluded = new HashSet<Item>(border) { frame, wipe };
            var body = own.Where(e => !excluded.Contains(e) && e.AnchorY < rowTop - .01).ToList();
            if (body.Count == 0) throw new InvalidDataException("표 안에 항목이 없습니다.");

            int originalRows = (int)Math.Round((rowTop - bottom) / RowHeight); // 템플릿 원래 표(바탕 가림 영역) 안의 행 수
            int nominal = Math.Max(originalRows, body.Max(e => (int)((rowTop - e.AnchorY) / RowHeight)) + 1);
            Func<Item, int> rowIndex = e => Math.Min(nominal - 1, Math.Max(0, (int)((rowTop - e.AnchorY) / RowHeight)));
            Func<Item, int?> column = e =>
            {
                for (int i = 0; i < 6; i++) if (bounds[i] - Tolerance <= e.AnchorX && e.AnchorX <= bounds[i + 1] + Tolerance) return i;
                return null;
            };
            var cols = Enumerable.Range(0, 6).ToDictionary(c => c, c => body.Where(e => column(e) == c).ToList());
            Func<int, int, int[]> span = (c, r) =>
            {
                double center = (bounds[c] + bounds[c + 1]) / 2;
                // 행 경계에 구분선이 있으면 별도 칸입니다. 원본 표 아래로 늘려 그린 행은 구분선이 없어도 각자 한 행으로 봅니다.
                var cut = Enumerable.Range(1, Math.Max(0, nominal - 1)).Select(i => rowTop - i * RowHeight <= bottom + Tolerance
                    || horizontal.Any(h => h.A - Tolerance <= center && center <= h.B + Tolerance && Math.Abs(h.Y - (rowTop - i * RowHeight)) < Tolerance)).ToArray();
                int start = r, end = r;
                while (start > 0 && !cut[start - 1]) start--;
                while (end < nominal - 1 && !cut[end]) end++;
                return new[] { start, end };
            };
            Func<IEnumerable<Item>, double, List<Item>> nearest = (candidates, target) =>
            {
                var list = candidates.ToList();
                if (list.Count == 0) return list;
                double near = list.Min(e => Math.Abs(e.AnchorY - target));
                return list.Where(e => Math.Abs(Math.Abs(e.AnchorY - target) - near) < Tolerance).ToList();
            };
            Func<int, int, List<Item>> choose = (c, r) =>
            {
                var exact = cols[c].Where(e => rowIndex(e) == r).ToList();
                var s = span(c, r); var within = cols[c].Where(e => s[0] <= rowIndex(e) && rowIndex(e) <= s[1]).ToList();
                double target = rowTop - (r + .5) * RowHeight;
                if (c == 0)
                {
                    // 병합된 기호 칸이라도 행마다 다른 기호는 각자 유지합니다.
                    if (exact.Count > 0) return exact;
                    if (within.Count > 0) return nearest(within, target);
                }
                if (c == 5) return exact.Count > 0 ? exact : within.Count == 1 ? within : new List<Item>();
                if ((c == 1 || c == 2) && within.Count > 0) return within;
                if (exact.Count > 0) return exact;
                if (within.Count > 0) return within;
                var n = span(1, r);
                return nearest(cols[c].Where(e => n[0] <= rowIndex(e) && rowIndex(e) <= n[1]), target);
            };
            var quantityTemplates = cols[4].Where(e => e.Type == "TEXT").ToList();
            if (quantityTemplates.Count == 0) throw new InvalidDataException("수량 칸의 문자(TEXT)를 찾지 못했습니다.");
            int last = body.Max(rowIndex);
            for (int i = 0; i <= last; i++)
            {
                string where = m.CategoryName + " " + (i + 1) + "행";
                if (!body.Any(e => rowIndex(e) == i)) continue; // 완전히 빈 행은 등록하지 않습니다.
                var fields = Enumerable.Range(0, 6).ToDictionary(c => c, c => choose(c, i));
                // 명칭 글자는 구분선으로 실제 병합된 칸(span) 안에서만 함께 씁니다(choose가 처리).
                // 병합되지 않은 칸이 비어 있으면 다른 행의 글자를 도면에 복사하지 않습니다.
                string itemName = Text(fields[1]);
                bool nameFromAbove = false;
                if (itemName.Length == 0)
                {
                    var above = m.Rows.LastOrDefault();
                    bool adjacent = above != null && Math.Abs(above.SourceTopY - (rowTop - (i - 1) * RowHeight)) < Tolerance;
                    if (i < originalRows && adjacent && fields[0].Count > 0 && fields[2].Count > 0)
                    {
                        // 원본 표 안에서 바로 위 항목과 같은 무리로 쓴 행(예: 노면표시 아래 '문자'): 목록 이름만 빌리고 도면 칸은 비워 둠.
                        itemName = above.Name; nameFromAbove = true;
                        warnings.Add(where + ": 명칭 칸이 비어 있어 목록에는 위 행 이름 '" + itemName + "'을 씁니다(도면의 명칭 칸은 템플릿처럼 비워 둡니다).");
                    }
                    else
                    {
                        warnings.Add(where + ": 명칭이 없어 등록하지 않았습니다 — " + (i >= originalRows ? "원래 표 아래에 추가한 행입니다. 명칭 글자를 명칭 칸 안에 넣으세요." : fields[0].Count == 0 ? "기호와 명칭이 모두 비어 있습니다." : "명칭 칸에 글자를 넣으세요."));
                        continue;
                    }
                }
                if (fields[0].Count == 0) warnings.Add(where + " '" + itemName + "': 기호가 없습니다. 기호 칸이 빈 채로 생성됩니다.");
                var quantity = fields[4].FirstOrDefault(e => e.Type == "TEXT") ?? quantityTemplates[0];
                var cells = new[] { 0, 1, 2, 3, 5 }.Where(c => fields[c].Count > 0 || c == 1 || c == 2).Select(c => new LegendSourceCell
                {
                    Column = c, Handles = fields[c].Select(e => e.Handle).ToList(),
                    SourceCenterY = fields[c].Count > 0 ? CellCenter(fields[c]) : rowTop - (i + .5) * RowHeight,
                    CenterX = (bounds[c] + bounds[c + 1]) / 2, Width = bounds[c + 1] - bounds[c] - 100
                }).ToList();
                double bandTop = rowTop - i * RowHeight, bandBottom = bandTop - RowHeight;
                foreach (var cell in cells.Where(c => c.Column == 0 && c.Handles.Count > 0))
                    cell.KeepRowOffset = fields[0].Min(e => e.MinY) >= bandBottom - 1 && fields[0].Max(e => e.MaxY) <= bandTop + 1;
                string rowId = m.CategoryId + "_" + (i + 1).ToString("000", CultureInfo.InvariantCulture);
                string spec = Text(fields[2]);
                m.Rows.Add(new LegendRow
                {
                    Id = rowId, Name = itemName, Spec = spec.Length == 0 ? "원본 기호" : spec, Remark = Text(fields[5]), Quantity = "-", Unit = Text(fields[3]),
                    GroupId = rowId, SourceTopY = rowTop - i * RowHeight, Height = RowHeight, NameFromAbove = nameFromAbove,
                    ContentHandles = cells.SelectMany(c => c.Handles).Distinct().ToList(), SymbolHandles = fields[0].Select(e => e.Handle).ToList(),
                    QuantityHandle = quantity.Handle, QuantitySourceCenterY = quantity.AnchorY, Cells = cells
                });
            }
        }

        private static List<double> Unique(IEnumerable<double> values)
        {
            var output = new List<double>();
            foreach (var value in values.OrderBy(v => v)) if (output.Count == 0 || Math.Abs(value - output[output.Count - 1]) > .01) output.Add(value);
            return output;
        }

        private static double CellCenter(List<Item> items)
        {
            var text = items.Where(e => e.IsText).Select(e => e.AnchorY).ToList();
            return text.Count > 0 ? text.Sum() / text.Count : (items.Min(e => e.MinY) + items.Max(e => e.MaxY)) / 2;
        }

        private static string Text(IEnumerable<Item> items)
        {
            return string.Join(" / ", items.Where(e => e.IsText).OrderByDescending(e => e.AnchorY).Select(e => e.Text.Trim().Replace("\r", "").Replace("\n", " ")));
        }

        /// <summary>모형 공간 객체 한 개의 위치·범위 요약.</summary>
        private sealed class Item
        {
            public ObjectId Id; public string Handle, Type, Layer, Text = "";
            public double X, Y, MinX, MinY, MaxX, MaxY; public bool HasExtents;
            public List<Point2d[]> Segments = new List<Point2d[]>();
            public bool IsText { get { return Type == "TEXT" || Type == "MTEXT"; } }
            public double AnchorX { get { return IsText ? X : (MinX + MaxX) / 2; } }
            public double AnchorY { get { return IsText ? Y : (MinY + MaxY) / 2; } }

            public static Item From(Entity entity)
            {
                var item = new Item { Id = entity.ObjectId, Handle = entity.Handle.ToString(), Type = entity.GetRXClass().DxfName, Layer = entity.Layer ?? "" };
                try
                {
                    var extents = entity.GeometricExtents;
                    item.MinX = extents.MinPoint.X; item.MinY = extents.MinPoint.Y; item.MaxX = extents.MaxPoint.X; item.MaxY = extents.MaxPoint.Y; item.HasExtents = true;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                var text = entity as DBText;
                if (text != null)
                {
                    item.Text = text.TextString ?? "";
                    bool basic = text.HorizontalMode == TextHorizontalMode.TextLeft && text.VerticalMode == TextVerticalMode.TextBase;
                    var p = basic ? text.Position : text.AlignmentPoint; item.X = p.X; item.Y = p.Y;
                }
                var mtext = entity as MText;
                if (mtext != null) { item.Text = mtext.Text ?? ""; item.X = mtext.Location.X; item.Y = mtext.Location.Y; }
                var line = entity as Line;
                if (line != null) item.Segments.Add(new[] { new Point2d(line.StartPoint.X, line.StartPoint.Y), new Point2d(line.EndPoint.X, line.EndPoint.Y) });
                var polyline = entity as Polyline;
                if (polyline != null)
                {
                    int count = polyline.NumberOfVertices;
                    for (int i = 0; i + 1 < count; i++) item.Segments.Add(new[] { polyline.GetPoint2dAt(i), polyline.GetPoint2dAt(i + 1) });
                    if (polyline.Closed && count > 1) item.Segments.Add(new[] { polyline.GetPoint2dAt(count - 1), polyline.GetPoint2dAt(0) });
                }
                return item;
            }
        }

        // ---------------------------------------------------------------- 미리보기 도형

        /// <summary>색: ACI 번호 또는 RGB.</summary>
        private struct Paint
        {
            public readonly int Aci; public readonly bool IsRgb; public readonly byte R, G, B;
            public Paint(int aci) { Aci = Math.Abs(aci); IsRgb = false; R = G = B = 0; }
            public Paint(byte r, byte g, byte b) { Aci = 0; IsRgb = true; R = r; G = g; B = b; }
            public static Paint From(Color color, Paint byLayer, Paint byBlock)
            {
                if (color.IsByLayer) return byLayer;
                if (color.IsByBlock) return byBlock;
                if (color.ColorMethod == ColorMethod.ByColor) return new Paint(color.Red, color.Green, color.Blue);
                return new Paint(color.ColorIndex);
            }
            public string Html()
            {
                if (IsRgb)
                {
                    // 흰 바탕 미리보기에서 보이도록 아주 밝은 색은 짙은 색으로 바꿉니다.
                    if (R * .299 + G * .587 + B * .114 > 215) return "#253347";
                    return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
                }
                switch (Aci)
                {
                    case 0: return "#243447"; case 1: return "#D63737"; case 2: return "#A08000"; case 3: return "#25833D"; case 4: return "#008E99";
                    case 5: return "#345BD2"; case 6: return "#A531AB"; case 7: return "#253347"; case 8: return "#667080"; case 9: return "#8690A0";
                }
                if (Aci < 10 || Aci > 249) return "#667080";
                int group = (Aci - 10) / 10, shade = (Aci - 10) % 10;
                double[] values = { 1, 1, .65, .65, .5, .5, .3, .3, .15, .15 };
                double r, g, b; Hsv(group / 24.0, shade % 2 == 0 ? 1 : .5, values[shade], out r, out g, out b);
                return "#" + ((int)Math.Round(r * 255)).ToString("X2") + ((int)Math.Round(g * 255)).ToString("X2") + ((int)Math.Round(b * 255)).ToString("X2");
            }
            private static void Hsv(double h, double s, double v, out double r, out double g, out double b)
            {
                int i = (int)(h * 6) % 6; double f = h * 6 - Math.Floor(h * 6), p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
                switch (i) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            }
        }

        private sealed class LayerCache
        {
            private readonly Dictionary<string, KeyValuePair<Paint, string>> layers = new Dictionary<string, KeyValuePair<Paint, string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Transaction tr; private readonly Database db;
            public LayerCache(Transaction tr, Database db) { this.tr = tr; this.db = db; }
            public KeyValuePair<Paint, string> Get(string name)
            {
                KeyValuePair<Paint, string> value;
                if (layers.TryGetValue(name, out value)) return value;
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                value = new KeyValuePair<Paint, string>(new Paint(7), "Continuous");
                if (table.Has(name))
                {
                    var layer = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                    var paint = layer.Color.ColorMethod == ColorMethod.ByColor ? new Paint(layer.Color.Red, layer.Color.Green, layer.Color.Blue) : new Paint(layer.Color.ColorIndex);
                    string linetype = "Continuous";
                    if (!layer.LinetypeObjectId.IsNull) linetype = ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name;
                    value = new KeyValuePair<Paint, string>(paint, linetype);
                }
                layers[name] = value;
                return value;
            }
        }

        private static void Flatten(Entity e, LayerCache layers, string parentLayer, Paint parentPaint, int depth, List<PreviewPrimitive> output)
        {
            if (depth > 12) throw new InvalidDataException("블록이 너무 깊게 중첩되어 있습니다.");
            string layer = e.Layer == "0" ? parentLayer : e.Layer;
            var definition = layers.Get(layer);
            var paint = Paint.From(e.Color, definition.Key, parentPaint);
            string linetype = e.Linetype ?? "ByLayer";
            if (string.Equals(linetype, "ByLayer", StringComparison.OrdinalIgnoreCase)) linetype = definition.Value;
            bool dash = !string.Equals(linetype, "Continuous", StringComparison.OrdinalIgnoreCase) && !string.Equals(linetype, "ByBlock", StringComparison.OrdinalIgnoreCase);
            string color = paint.Html();
            Func<IEnumerable<Point2d>, bool, string, PreviewPrimitive> poly = (points, closed, fill) => new PreviewPrimitive
            { Kind = "polyline", Color = color, Dash = dash, Points = points.Select(p => new PreviewPoint { X = Round(p.X), Y = Round(p.Y) }).ToList(), Closed = closed, Fill = fill };

            var reference = e as BlockReference;
            if (reference != null)
            {
                var parts = new DBObjectCollection();
                try
                {
                    reference.Explode(parts);
                    foreach (DBObject part in parts)
                    {
                        var child = part as Entity;
                        if (child != null && !(child is AttributeDefinition)) Flatten(child, layers, layer, paint, depth + 1, output);
                    }
                }
                finally { foreach (DBObject part in parts) part.Dispose(); }
                return;
            }
            if (e is Wipeout || e is RasterImage) return;
            var line = e as Line;
            if (line != null) { output.Add(poly(new[] { Flat(line.StartPoint), Flat(line.EndPoint) }, false, "")); return; }
            var polyline = e as Polyline;
            if (polyline != null)
            {
                int count = polyline.NumberOfVertices; if (count == 0) return;
                var points = new List<Point2d>();
                for (int i = 0; i < count - (polyline.Closed ? 0 : 1); i++) points.AddRange(BulgeSamples(polyline.GetPoint2dAt(i), polyline.GetPoint2dAt((i + 1) % count), polyline.GetBulgeAt(i)));
                if (!polyline.Closed) points.Add(polyline.GetPoint2dAt(count - 1));
                output.Add(poly(points, polyline.Closed, ""));
                // 폭이 있는 구간(화살표 머리 등)은 실제 두께대로 채운 도형으로 그립니다.
                for (int i = 0; i < count - (polyline.Closed ? 0 : 1); i++)
                {
                    double start = polyline.GetStartWidthAt(i), end = polyline.GetEndWidthAt(i);
                    if (start < 1e-6 && end < 1e-6) continue;
                    var a = polyline.GetPoint2dAt(i); var b = polyline.GetPoint2dAt((i + 1) % count);
                    var path = BulgeSamples(a, b, polyline.GetBulgeAt(i)).ToList(); path.Add(b);
                    var outline = WideOutline(path, start, end);
                    if (outline.Count > 2) output.Add(poly(outline, true, "solid"));
                }
                return;
            }
            var circle = e as Circle;
            if (circle != null) { output.Add(poly(ArcPoints(circle.Center, circle.Radius, 0, 2 * Math.PI), true, "")); return; }
            var arc = e as Arc;
            if (arc != null)
            {
                double sweep = arc.EndAngle - arc.StartAngle; while (sweep < 0) sweep += 2 * Math.PI; while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
                output.Add(poly(ArcPoints(arc.Center, arc.Radius, arc.StartAngle, sweep), false, "")); return;
            }
            var ellipse = e as Ellipse;
            if (ellipse != null)
            {
                double a = ellipse.StartParam, sweep = ellipse.EndParam - ellipse.StartParam; while (sweep <= 0) sweep += 2 * Math.PI;
                var major = ellipse.MajorAxis; double ratio = ellipse.RadiusRatio; var c = ellipse.Center;
                var points = Enumerable.Range(0, 161).Select(i => { double t = a + sweep * i / 160; return new Point2d(c.X + major.X * Math.Cos(t) - major.Y * ratio * Math.Sin(t), c.Y + major.Y * Math.Cos(t) + major.X * ratio * Math.Sin(t)); });
                output.Add(poly(points, Math.Abs(sweep - 2 * Math.PI) < 1e-6, "")); return;
            }
            var solid = e as Solid;
            if (solid != null) { output.Add(poly(new short[] { 0, 1, 3, 2 }.Select(i => Flat(solid.GetPointAt(i))), true, "solid")); return; }
            var mtext = e as MText;
            if (mtext != null)
            {
                var lines = (mtext.Text ?? "").Replace("\r", "").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    output.Add(new PreviewPrimitive { Kind = "text", Color = color, X = mtext.Location.X, Y = mtext.Location.Y - i * mtext.TextHeight * 1.4, Text = CleanText(lines[i]), Height = mtext.TextHeight, WidthFactor = 1, Rotation = 0, Horizontal = 0, Vertical = 3 });
                return;
            }
            var text = e as DBText;
            if (text != null)
            {
                int ha = (int)text.HorizontalMode, va = (int)text.VerticalMode;
                var p = ha != 0 || va != 0 ? text.AlignmentPoint : text.Position;
                output.Add(new PreviewPrimitive { Kind = "text", Color = color, X = p.X, Y = p.Y, Text = CleanText(text.TextString ?? ""), Height = text.Height, WidthFactor = text.WidthFactor, Rotation = text.Rotation * 180 / Math.PI, Horizontal = ha, Vertical = va });
                return;
            }
            var hatch = e as Hatch;
            if (hatch != null)
            {
                string fill = hatch.IsSolidFill ? "solid" : "hatch";
                var contours = new List<List<PreviewPoint>>();
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    var loop = hatch.GetLoopAt(i); var points = new List<Point2d>();
                    if (hatch.HatchStyle == HatchStyle.Ignore && (loop.LoopType & (HatchLoopTypes.External | HatchLoopTypes.Outermost)) == 0) continue;
                    if (loop.IsPolyline)
                    {
                        var vertices = loop.Polyline;
                        for (int j = 0; j < vertices.Count; j++)
                            points.AddRange(BulgeSamples(vertices[j].Vertex, vertices[(j + 1) % vertices.Count].Vertex, vertices[j].Bulge));
                    }
                    else foreach (Curve2d curve in loop.Curves)
                    {
                        var segment = curve as LineSegment2d;
                        if (segment != null) { points.Add(segment.StartPoint); points.Add(segment.EndPoint); }
                        else points.AddRange(curve.GetSamplePoints(curve is EllipticalArc2d ? 101 : 81));
                    }
                    if (points.Count >= 3) contours.Add(points.Select(p => new PreviewPoint { X = Round(p.X), Y = Round(p.Y) }).ToList());
                }
                List<PreviewHatchLine> pattern = null;
                if (!hatch.IsSolidFill && !hatch.IsGradient && hatch.NumberOfPatternDefinitions > 0 && hatch.NumberOfPatternDefinitions <= 64)
                {
                    pattern = new List<PreviewHatchLine>();
                    for (int i = 0; i < hatch.NumberOfPatternDefinitions; i++)
                    {
                        var d = hatch.GetPatternDefinitionAt(i); var dashes = d.GetDashes();
                        pattern.Add(new PreviewHatchLine { Angle = Round(d.Angle * 180 / Math.PI), X = Round(d.BaseX), Y = Round(d.BaseY), OffsetX = Round(d.OffsetX), OffsetY = Round(d.OffsetY),
                            Dashes = dashes.Count == 0 ? null : dashes.Cast<double>().Take(64).Select(Round).ToList() });
                    }
                }
                if (contours.Count > 0)
                    output.Add(new PreviewPrimitive { Kind = "polyline", Color = color, Dash = dash, Closed = true, Fill = fill,
                        Points = contours[0], Contours = contours, Pattern = pattern });
                return;
            }
            var other = e as Curve;
            if (other != null)
            {
                double start = other.StartParam, end = other.EndParam;
                output.Add(poly(Enumerable.Range(0, 65).Select(i => Flat(other.GetPointAtParameter(start + (end - start) * i / 64))), other.Closed, ""));
            }
        }

        private static double Round(double value) { return Math.Round(value, 4); }
        private static Point2d Flat(Point3d p) { return new Point2d(p.X, p.Y); }

        private static IEnumerable<Point2d> ArcPoints(Point3d center, double radius, double start, double sweep)
        {
            int n = Math.Max(24, (int)(sweep * 30));
            for (int i = 0; i <= n; i++) yield return new Point2d(center.X + radius * Math.Cos(start + sweep * i / n), center.Y + radius * Math.Sin(start + sweep * i / n));
        }

        private static IEnumerable<Point2d> BulgeSamples(Point2d a, Point2d b, double q)
        {
            if (Math.Abs(q) < 1e-10) { yield return a; yield break; }
            double chord = a.GetDistanceTo(b);
            double cx = (a.X + b.X) / 2 - (b.Y - a.Y) * (1 - q * q) / (4 * q), cy = (a.Y + b.Y) / 2 + (b.X - a.X) * (1 - q * q) / (4 * q);
            double radius = chord * (1 + q * q) / (4 * Math.Abs(q)), angle = Math.Atan2(a.Y - cy, a.X - cx), sweep = 4 * Math.Atan(q);
            int n = Math.Max(8, (int)(Math.Abs(sweep) * 24));
            for (int i = 0; i < n; i++) yield return new Point2d(cx + radius * Math.Cos(angle + sweep * i / n), cy + radius * Math.Sin(angle + sweep * i / n));
        }

        /// <summary>중심선을 따라 시작 폭 → 끝 폭으로 변하는 띠의 외곽선(왼쪽 → 오른쪽 역순).</summary>
        private static List<Point2d> WideOutline(List<Point2d> path, double startWidth, double endWidth)
        {
            var left = new List<Point2d>(); var right = new List<Point2d>();
            int n = path.Count; if (n < 2) return left;
            double total = 0; var lengths = new double[n];
            for (int k = 1; k < n; k++) { total += path[k].GetDistanceTo(path[k - 1]); lengths[k] = total; }
            if (total < 1e-9) return left;
            for (int k = 0; k < n; k++)
            {
                var direction = path[Math.Min(k + 1, n - 1)] - path[Math.Max(k - 1, 0)];
                if (direction.Length < 1e-12) continue;
                direction = direction.GetNormal();
                var normal = new Vector2d(-direction.Y, direction.X);
                double half = (startWidth + (endWidth - startWidth) * lengths[k] / total) / 2;
                left.Add(path[k] + normal * half); right.Add(path[k] - normal * half);
            }
            right.Reverse(); left.AddRange(right);
            return left;
        }

        private static string CleanText(string text)
        {
            text = Regex.Replace(text, @"\\U\+([0-9A-Fa-f]{4})", x => ((char)int.Parse(x.Groups[1].Value, NumberStyles.HexNumber)).ToString());
            return text.Replace("%%c", "Ø").Replace("%%C", "Ø").Replace("%%d", "°").Replace("%%D", "°").Replace("%%p", "±").Replace("%%P", "±");
        }
    }
}
