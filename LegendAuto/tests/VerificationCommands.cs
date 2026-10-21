using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using StormLegend;
[assembly: CommandClass(typeof(MultiVerification))]

/// <summary>AutoCAD Core Console에서 실행하는 실제 생성 검증(verify.ps1).</summary>
public sealed class MultiVerification
{
    private static readonly List<string> log = new List<string>(); private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new System.Exception(name); checks++; }
    private static ObjectId Id(Database db, string h) { return db.GetObjectId(false, new Handle(long.Parse(h, NumberStyles.HexNumber)), 0); }
    private static string root, output, template;

    [CommandMethod("LEGENDMULTIVERIFY")]
    public void Verify()
    {
        root = Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"); output = Path.Combine(root, "verification"); template = Path.Combine(root, "Template");
        Directory.CreateDirectory(Path.Combine(output, "generated")); log.Clear(); checks = 0;
        try
        {
            VerifyAnalyzerMatchesShipped();
            VerifyCategories();
            VerifyBatch();
            VerifyExplodedBatch();
            VerifyBlankRows();
            VerifyTemplateEdit();
            checks += ReviewVerification.Run(root, log);
            checks += ReviewFixVerification.Run(root, log);
            log.Add("SUCCESS: " + checks + " native checks.");
        }
        catch (System.Exception ex) { log.Add("FAIL: " + ex); }
        File.WriteAllLines(Path.Combine(output, "runtime-verification.txt"), log);
        AcApplication.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + log.Last());
    }

    private static CatalogIndex Shipped() { return CatalogIndex.Load(Path.Combine(template, CatalogIndex.FileName)); }

    // ------------------------------------------------------------ 1. C# 분석기 = 기존 연결 정보
    private static void VerifyAnalyzerMatchesShipped()
    {
        var shipped = Shipped();
        string dwg = Path.Combine(template, TemplateRepository.TemplateFileName);
        Check(string.Equals(TemplateManifest.ComputeSha256(dwg), shipped.SourceDwgSha256, StringComparison.OrdinalIgnoreCase), "shipped catalog records the deployed template DWG hash");
        string work = Path.Combine(root, "work", "analyzer-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var started = DateTime.Now;
        var analysis = TemplateRepository.AnalyzeIntoCache(dwg, null, work);
        double seconds = (DateTime.Now - started).TotalSeconds;
        var index = CatalogIndex.Load(Path.Combine(work, CatalogIndex.FileName));
        Check(index.Categories.Select(c => c.Id).SequenceEqual(shipped.Categories.Select(c => c.Id)), "analyzer finds the same seven categories in order");
        var differences = new List<string>();
        foreach (var entry in shipped.Categories)
        {
            var a = shipped.LoadManifest(entry); var b = index.Manifest(entry.Id);
            Action<bool, string> same = (ok, what) => { if (!ok) differences.Add(entry.Id + ": " + what); };
            Func<double, double, bool> near = (x, y) => Math.Abs(x - y) < 1e-6;
            same(a.Rows.Count == b.Rows.Count, "row count " + a.Rows.Count + " vs " + b.Rows.Count);
            same(new HashSet<string>(a.AllHandles).SetEquals(b.AllHandles), "all handles");
            same(a.HeaderHandles.SequenceEqual(b.HeaderHandles), "header handles");
            same(a.VerticalBorderHandles.SequenceEqual(b.VerticalBorderHandles), "vertical borders");
            same(a.AuxBorderHandles.SequenceEqual(b.AuxBorderHandles), "aux borders");
            same(a.FullSeparatorHandle == b.FullSeparatorHandle && near(a.FullSeparatorY, b.FullSeparatorY), "full separator");
            same(a.OuterBorderHandle == b.OuterBorderHandle && a.WipeoutHandle == b.WipeoutHandle && a.HasQuantityColumn == b.HasQuantityColumn, "frame/wipeout/quantity policy");
            same(near(a.Origin.X, b.Origin.X) && near(a.Origin.Y, b.Origin.Y) && near(a.FrameWidth, b.FrameWidth) && near(a.BottomMargin, b.BottomMargin) && near(a.RowsTopY, b.RowsTopY) && near(a.SourceBottomY, b.SourceBottomY) && near(a.TitleTopY, b.TitleTopY), "frame geometry");
            same(near(a.QuantityCenterX, b.QuantityCenterX) && near(a.QuantityCellWidth, b.QuantityCellWidth), "quantity column");
            for (int i = 0; i < Math.Min(a.Rows.Count, b.Rows.Count); i++)
            {
                var r = a.Rows[i]; var s = b.Rows[i]; string at = " row " + r.Id;
                same(r.Id == s.Id && r.Name == s.Name && r.Spec == s.Spec && r.Unit == s.Unit && r.Remark == s.Remark, at + " text '" + r.Name + "/" + r.Spec + "' vs '" + s.Name + "/" + s.Spec + "'");
                same(r.SymbolHandles.SequenceEqual(s.SymbolHandles) && r.QuantityHandle == s.QuantityHandle && r.ContentHandles.SequenceEqual(s.ContentHandles), at + " handles [" + string.Join(",", r.ContentHandles) + "] vs [" + string.Join(",", s.ContentHandles) + "]");
                same(near(r.SourceTopY, s.SourceTopY) && near(r.Height, s.Height) && near(r.QuantitySourceCenterY, s.QuantitySourceCenterY), at + " geometry");
                same(r.Cells.Count == s.Cells.Count && r.Cells.Zip(s.Cells, (x, y) => x.Column == y.Column && x.KeepRowOffset == y.KeepRowOffset && x.Handles.SequenceEqual(y.Handles) && near(x.CenterX, y.CenterX) && near(x.Width, y.Width) && near(x.SourceCenterY, y.SourceCenterY)).All(v => v), at + " cells");
            }
        }
        Check(differences.Count == 0, "C# analyzer reproduces the verified mapping exactly: " + string.Join(" | ", differences.Take(12)));
        var preview = PreviewCatalogFile.Load(Path.Combine(work, CatalogIndex.PreviewFileName));
        var symbolHandles = index.Manifests.SelectMany(m => m.Rows.SelectMany(r => r.SymbolHandles)).Distinct().ToList();
        var lookup = preview.Entities.ToDictionary(e => e.Handle, e => e.Primitives);
        Check(symbolHandles.All(h => lookup.ContainsKey(h)) && symbolHandles.Count(h => lookup[h].Count > 0) >= symbolHandles.Count * .95, "analyzer preview draws the original symbols");
        Check(analysis.Warnings.Count == 1 && analysis.Warnings[0].StartsWith("부대 32행: 명칭 칸이 비어"), "original template: only the known note for 부대 32행 (separate cell with empty name): " + string.Join(" | ", analysis.Warnings));
        log.Add("PASS: C# template analyzer = verified mapping (7 categories / " + index.Categories.Sum(c => c.ItemCount) + " items / " + preview.Entities.Count + " preview entities) in " + seconds.ToString("0.00", CultureInfo.InvariantCulture) + "s");
        Directory.Delete(work, true);
    }

    // ------------------------------------------------------------ 2. 공종별 원본 복제(블록 API)
    private static void VerifyCategories()
    {
        var index = Shipped(); int total = 0;
        foreach (var entry in index.Categories)
        {
            var m = index.LoadManifest(entry); total += m.Rows.Count; string source = index.SourcePath(m); string name = entry.Id;
            using (var src = LegendComposer.OpenSource(source))
            using (var st = src.TransactionManager.StartTransaction())
            {
                foreach (bool subset in new[] { false, true })
                using (var target = new Database(true, true))
                {
                    var rows = subset ? new[] { m.Rows.First(), m.Rows[m.Rows.Count / 2], m.Rows.Last() }.Distinct().ToList() : m.Rows;
                    var choices = rows.Select(r => new LegendSelection(r.Id, "12.5")).ToList();
                    var result = LegendComposer.Compose(target, m, source, choices, new Point3d(100, 200, 0), Matrix3d.Identity);
                    Check(result.ItemCount == rows.Count, name + " item count"); Check(Math.Abs(result.Width - m.FrameWidth) < 1e-7, name + " width");
                    Check(Math.Abs(result.Height - (m.Origin.Y - m.RowsTopY + rows.Sum(r => r.Height) + m.BottomMargin)) < 1e-6, name + " height");
                    using (var tr = target.TransactionManager.StartTransaction())
                    {
                        var block = (BlockTableRecord)tr.GetObject(result.BlockDefinitionId, OpenMode.ForRead);
                        var br = (BlockReference)tr.GetObject(result.BlockReferenceId, OpenMode.ForRead); Check(br.Position.DistanceTo(new Point3d(100, 200, 0)) < 1e-7, name + " insertion");
                        Check(((BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().Count() == 1, name + " single output block");
                        double top = m.RowsTopY;
                        foreach (var row in rows)
                        {
                            var symbolCell = row.Cells.Single(c => c.Column == 0); var ids = result.RowSymbolCopies[row.Id]; Check(ids.Count == symbolCell.Handles.Count, name + row.Id + " symbol count");
                            for (int i = 0; i < ids.Count; i++)
                            {
                                var actual = (Entity)tr.GetObject(ids[i], OpenMode.ForRead); var original = (Entity)st.GetObject(Id(src, symbolCell.Handles[i]), OpenMode.ForRead);
                                using (var expected = (Entity)original.Clone())
                                {
                                    expected.TransformBy(Matrix3d.Displacement(new Vector3d(-m.Origin.X, top - row.Height / 2 - row.SourceCenterOf(symbolCell) - m.Origin.Y, -m.Origin.Z)));
                                    Compare(expected, actual, name + ":" + row.Id + ":" + symbolCell.Handles[i]);
                                }
                            }
                            if (m.HasQuantityColumn)
                            {
                                double center = top - row.Height / 2 - m.Origin.Y;
                                Check(block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().Any(t => t.TextString == "12.5" && Math.Abs(t.AlignmentPoint.Y - center) < .01 && Math.Abs(t.AlignmentPoint.X - (m.QuantityCenterX - m.Origin.X)) < .01), name + " quantity row " + row.Id);
                            }
                            top -= row.Height;
                        }
                        var border = (Polyline)tr.GetObject(result.OriginalEntityCopies[m.OuterBorderHandle], OpenMode.ForRead);
                        Check(Math.Abs(Enumerable.Range(0, border.NumberOfVertices).Min(i => border.GetPoint2dAt(i).Y) + result.Height) < .001, name + " resized border");
                        foreach (var h in m.HeaderHandles)
                        {
                            var actual = (Entity)tr.GetObject(result.OriginalEntityCopies[h], OpenMode.ForRead); var original = (Entity)st.GetObject(Id(src, h), OpenMode.ForRead);
                            using (var expected = (Entity)original.Clone()) { expected.TransformBy(Matrix3d.Displacement(new Vector3d(-m.Origin.X, -m.Origin.Y, -m.Origin.Z))); Compare(expected, actual, name + " header " + h); }
                        }
                        tr.Commit();
                    }
                    string basename = Path.Combine(output, "generated", name + (subset ? "-selected" : "-all")); target.SaveAs(basename + ".dwg", DwgVersion.AC1032);
                    int before = CountEntities(target); bool rejected = false;
                    try { LegendComposer.Compose(target, m, source, new[] { new LegendSelection("unknown", "-") }, Point3d.Origin, Matrix3d.Identity); } catch (ArgumentException) { rejected = true; }
                    Check(rejected && before == CountEntities(target), name + " invalid input no mutation");
                }
                using (var target = new Database(true, true))
                {
                    var result = LegendComposer.ComposeOriginal(target, m, source, Point3d.Origin, Matrix3d.Identity);
                    using (var tr = target.TransactionManager.StartTransaction())
                    {
                        var block = (BlockTableRecord)tr.GetObject(result.BlockDefinitionId, OpenMode.ForRead); Check(block.Cast<ObjectId>().Count() == m.AllHandles.Count, name + " original complete root count");
                        foreach (string h in m.AllHandles)
                        {
                            var actual = (Entity)tr.GetObject(result.OriginalEntityCopies[h], OpenMode.ForRead); var original = (Entity)st.GetObject(Id(src, h), OpenMode.ForRead);
                            using (var expected = (Entity)original.Clone()) { expected.TransformBy(Matrix3d.Displacement(new Vector3d(-m.Origin.X, -m.Origin.Y, -m.Origin.Z))); Compare(expected, actual, name + " original " + h); }
                        }
                        tr.Commit();
                    }
                    target.SaveAs(Path.Combine(output, "generated", name + "-original.dwg"), DwgVersion.AC1032);
                }
                if (name == "paving") VerifyPavingEdits(m, source, src);
                st.Commit();
            }
            log.Add("PASS: " + m.CategoryName + " / " + m.Rows.Count + " items / all, subset, original; source geometry, headers, quantities, borders and rollback");
        }
        Check(total == 94, "total 94");
    }

    private static void VerifyPavingEdits(TemplateManifest m, string source, Database src)
    {
        using (var target = new Database(true, true))
        {
            var choices = m.Rows.Select((row, i) => new LegendSelection(row.Id, "3.5", i == 0 ? "프로젝트 아스콘포장" : null, i == 0 ? "T=600" : null)).ToList();
            var result = LegendComposer.Compose(target, m, source, choices, Point3d.Origin, Matrix3d.Identity);
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var block = (BlockTableRecord)tr.GetObject(result.BlockDefinitionId, OpenMode.ForRead);
                var texts = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().ToList();
                Check(texts.Count(t => t.TextString == "T=600") == 1 && !texts.Any(t => t.TextString == "T=450"), "edited spec replaces source cell once");
                Check(texts.Count(t => t.TextString == "프로젝트 아스콘포장") == 1, "edited name appears once");
                foreach (var column in new[] { 1, 2 })
                {
                    var cell = m.Rows[0].Cells.Single(c => c.Column == column); var text = texts.Single(t => t.TextString == (column == 1 ? "프로젝트 아스콘포장" : "T=600"));
                    Check(Math.Abs(text.AlignmentPoint.X - cell.CenterX + m.Origin.X) < .01 && Math.Abs(text.AlignmentPoint.Y - (m.RowsTopY - 225 - m.Origin.Y)) < .01, "edited text centered");
                    Check(text.GeometricExtents.MaxPoint.X - text.GeometricExtents.MinPoint.X <= cell.Width + .1, "edited text fits cell");
                }
                tr.Commit();
            }
            target.SaveAs(Path.Combine(output, "generated", "paving-edited-T600.dwg"), DwgVersion.AC1032);
            int originalCount = CountEntities(target); int originalLayers = CountLayers(target);
            string oldQuantity = m.Rows[0].QuantityHandle; m.Rows[0].QuantityHandle = m.Rows[0].SymbolHandles.First();
            bool failed = false;
            try { LegendComposer.Compose(target, m, source, choices, Point3d.Origin, Matrix3d.Identity); } catch (System.Exception) { failed = true; } finally { m.Rows[0].QuantityHandle = oldQuantity; }
            Check(failed && CountEntities(target) == originalCount && CountLayers(target) == originalLayers, "staging failure leaves existing drawing and layer table unchanged");
            string tampered = Path.Combine(root, "work", "tampered-source" + Path.GetExtension(source)); File.WriteAllBytes(tampered, File.ReadAllBytes(source).Concat(new byte[] { 10, 32 }).ToArray()); failed = false;
            try { LegendComposer.Compose(target, m, tampered, choices, Point3d.Origin, Matrix3d.Identity); } catch (InvalidDataException) { failed = true; }
            Check(failed && CountEntities(target) == originalCount && CountLayers(target) == originalLayers, "modified template hash is rejected before drawing changes");
            log.Add("PASS: paving T=450 -> T=600, custom name, quantity 3.5; rollback; tampered source rejected");
        }
    }

    private static List<LegendBatchItem> AllItems(CatalogIndex index)
    {
        return index.Categories.Select(entry =>
        {
            var m = index.LoadManifest(entry);
            return new LegendBatchItem(m, m.Rows.Select((r, i) => new LegendSelection(r.Id, "3.5", entry.Id == "paving" && i == 0 ? "프로젝트 아스콘포장" : null, entry.Id == "paving" && i == 0 ? "T=600" : null)).ToList(), index.SourcePath(m));
        }).ToList();
    }

    // ------------------------------------------------------------ 3. 여러 공종 블록 배치(기존 API 유지)
    private static void VerifyBatch()
    {
        var index = Shipped(); var items = AllItems(index); var names = index.Categories.Select(c => c.Id).ToArray();
        double baselineWidth = 0, baselineHeight = 0;
        foreach (int denominator in new[] { 100, 300 }) using (var target = new Database(true, true))
        {
            var origin = new Point3d(120, 340, 0); var started = DateTime.Now;
            var result = LegendComposer.ComposeBatch(target, items.AsEnumerable().Reverse().ToList(), origin, Matrix3d.Identity, denominator);
            if (denominator == 100) log.Add("INFO: 7-category batch generated in " + (DateTime.Now - started).TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s (source template read once)");
            Check(CountEntities(target) == 1 && result.Placements.Count == 7 && result.ItemCount == 94, "batch one root / seven disciplines / 94 items");
            double factor = denominator / 100.0; Check(Math.Abs(result.ScaleFactor - factor) < 1e-8, "batch factor");
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var reference = (BlockReference)tr.GetObject(result.BlockReferenceId, OpenMode.ForRead); Check(reference.ScaleFactors.IsEqualTo(new Scale3d(factor)) && reference.Position.DistanceTo(origin) < 1e-6, "batch uniform scale and anchor");
                var extents = reference.GeometricExtents; double width = extents.MaxPoint.X - extents.MinPoint.X, height = extents.MaxPoint.Y - extents.MinPoint.Y;
                if (denominator == 100) { baselineWidth = width; baselineHeight = height; } else Check(Math.Abs(width - baselineWidth * 3) < .001 && Math.Abs(height - baselineHeight * 3) < .001, "native extents are exactly three times");
                for (int i = 0; i < result.Placements.Count; i++)
                {
                    var placement = result.Placements[i]; var child = (BlockReference)tr.GetObject(placement.BlockReferenceId, OpenMode.ForRead);
                    Check(placement.CategoryId == names[i], "batch canonical order"); Check(child.Position.DistanceTo(new Point3d(placement.X / factor, placement.Y / factor, 0)) < 1e-6, "batch scaled layout offsets");
                    if (i > 0) Check(placement.X >= result.Placements[i - 1].X + result.Placements[i - 1].Width + 299.99 * factor, "batch scaled horizontal gap");
                }
                tr.Commit();
            }
            int count = CountEntities(target), layers = CountLayers(target); bool rejected = false;
            try { LegendComposer.ComposeBatch(target, items, Point3d.Origin, Matrix3d.Identity, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && CountEntities(target) == count, "invalid batch scale has no output");
            var bad = new List<LegendBatchItem> { items[0], new LegendBatchItem(items[1].Manifest, new[] { new LegendSelection("unknown", "-") }, items[1].SourcePath) }; rejected = false;
            try { LegendComposer.ComposeBatch(target, bad, Point3d.Origin, Matrix3d.Identity, 300); } catch (ArgumentException) { rejected = true; }
            Check(rejected && CountEntities(target) == count && CountLayers(target) == layers, "later discipline failure rolls back entire batch and layers");
        }
        using (var target = new Database(true, true))
        {
            var result = LegendComposer.ComposeBatch(target, new[] { items[5] }, new Point3d(10, 20, 0), Matrix3d.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin), 300);
            using (var tr = target.TransactionManager.StartTransaction()) { var reference = (BlockReference)tr.GetObject(result.BlockReferenceId, OpenMode.ForRead); Check(new Point3d(100, 0, 0).TransformBy(reference.BlockTransform).DistanceTo(new Point3d(10, 320, 0)) < 1e-6, "rotated UCS scales around insertion"); }
        }
        using (var target = new Database(true, true))
        {
            var result = LegendComposer.ComposeBatch(target, new[] { items[2], items[3] }, Point3d.Origin, Matrix3d.Identity, 300, false, true, 600);
            Check(Math.Abs(result.Placements[1].X - (result.Placements[0].X + result.Placements[0].Width + 600 * 3)) < 1e-6, "category gap setting (600 at 1:300 = 1800)");
            bool rejected = false; try { LegendComposer.ComposeBatch(target, new[] { items[2] }, Point3d.Origin, Matrix3d.Identity, 100, false, true, -1); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "negative gap rejected");
        }
        log.Add("PASS: block batch / canonical order / 100=1x, 300=3x / UCS / atomic rollback");
    }

    // ------------------------------------------------------------ 4. 분해 생성(LEGEND 기본)
    private static void VerifyExplodedBatch()
    {
        var index = Shipped(); var items = AllItems(index);
        double blockWidth, blockHeight;
        using (var reference = new Database(true, true))
        {
            var block = LegendComposer.ComposeBatch(reference, items, Point3d.Origin, Matrix3d.Identity, 300);
            using (var tr = reference.TransactionManager.StartTransaction())
            { var e = ((Entity)tr.GetObject(block.BlockReferenceId, OpenMode.ForRead)).GeometricExtents; blockWidth = e.MaxPoint.X - e.MinPoint.X; blockHeight = e.MaxPoint.Y - e.MinPoint.Y; }
        }
        using (var target = new Database(true, true))
        {
            var origin = new Point3d(500, 700, 0);
            var result = LegendComposer.ComposeBatch(target, items, origin, Matrix3d.Identity, 300, false, true);
            int layersAfterFirst = CountLayers(target), blocksAfterFirst = CountBlocks(target);
            Check(result.Exploded && result.ItemCount == 94 && result.EntityIds.Count == CountEntities(target) && result.EntityIds.Count > 900, "exploded output is loose entities: " + result.EntityIds.Count);
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var entities = result.EntityIds.Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                var inserts = entities.OfType<BlockReference>().ToList();
                Check(inserts.All(b => !((BlockTableRecord)tr.GetObject(b.BlockTableRecord, OpenMode.ForRead)).Name.StartsWith("STORM_LEGEND_") && !((BlockTableRecord)tr.GetObject(b.BlockTableRecord, OpenMode.ForRead)).Name.StartsWith("LEGEND_BATCH_")), "no legend wrapper block remains (only original template symbols that were blocks: " + inserts.Count + ")");
                Check(!((BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).Any(n => n.StartsWith("STORM_LEGEND_") || n.StartsWith("LEGEND_BATCH_")), "no wrapper block definitions left in drawing");
                var box = new Extents3d(); foreach (var e in entities) try { box.AddExtents(e.GeometricExtents); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                Check(Math.Abs(box.MaxPoint.X - box.MinPoint.X - blockWidth) < .01 && Math.Abs(box.MaxPoint.Y - box.MinPoint.Y - blockHeight) < .01, "exploded legend has the same 3x extents as block output");
                Check(Math.Abs(box.MinPoint.X - origin.X) < 1 && Math.Abs(box.MaxPoint.Y - origin.Y) < 1, "exploded legend top-left at insertion point");
                Check(entities.OfType<DBText>().Count(t => t.TextString == "T=600") == 1, "exploded output keeps edited labels");
                var names = ((LayerTable)tr.GetObject(target.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name)
                    .Concat(((LinetypeTable)tr.GetObject(target.LinetypeTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name))
                    .Concat(((TextStyleTable)tr.GetObject(target.TextStyleTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name)).ToList();
                Check(!names.Any(n => n.StartsWith("$0$")) && names.Contains("C-우수-U형측구-SO") && names.Contains("H-DICHETCU1"), "layer/linetype/style names stay exactly as in the template (no $0$ prefix)");
                var uType = entities.OfType<Polyline>().Where(p => p.Linetype == "H-DICHETCU1").ToList();
                Check(uType.Count == 1 && Math.Abs(uType[0].LinetypeScale - 900) < 1e-6, "U형측구 complex linetype scale follows 1:300 (300 x 3 = 900) so the pattern matches the template: " + string.Join(",", uType.Select(p => p.LinetypeScale)));
                var order = (DrawOrderTable)tr.GetObject(((BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForRead)).DrawOrderTableId, OpenMode.ForRead);
                var full = order.GetFullDrawOrder(0).Cast<ObjectId>().ToList();
                int lastMask = full.FindLastIndex(id => tr.GetObject(id, OpenMode.ForRead) is Wipeout);
                int firstContent = full.FindIndex(id => !(tr.GetObject(id, OpenMode.ForRead) is Wipeout));
                Check(lastMask >= 0 && lastMask < firstContent, "table masks (WIPEOUT) stay under legend content");
                tr.Commit();
            }
            target.SaveAs(Path.Combine(output, "generated", "batch-seven-exploded-scale300.dwg"), DwgVersion.AC1032);
            LegendComposer.ComposeBatch(target, items, new Point3d(0, -20000, 0), Matrix3d.Identity, 100, false, true);
            Check(CountLayers(target) == layersAfterFirst && CountBlocks(target) == blocksAfterFirst, "second run in same drawing adds no duplicate layers/blocks (" + layersAfterFirst + " layers, " + blocksAfterFirst + " blocks)");
        }
        log.Add("PASS: exploded generation (no wrapper blocks), same extents as block output, masks under content, repeat run adds no duplicate layers");
    }

    // ------------------------------------------------------------ 5. 빈 행(중간·맨 아래)
    private static void VerifyBlankRows()
    {
        var index = Shipped(); var m = index.Manifest("storm"); string source = index.SourcePath(m);
        var model = new PickerModel(m, false);
        model.Toggle(m.Rows[0].Id); model.Toggle(m.Rows[1].Id); model.Toggle(m.Rows[2].Id);
        string middle = model.AddBlank(m.Rows[0].Id); string end = model.AddBlank();
        model.Toggle(m.Rows[5].Id);
        var ids = model.Selection.Select(s => s.ItemId).ToList();
        Check(ids.SequenceEqual(new[] { m.Rows[0].Id, middle, m.Rows[1].Id, m.Rows[2].Id, m.Rows[5].Id, end }), "new item goes above the bottom blank row and below the middle blank: " + string.Join(",", ids));
        model.Move(end, -1); Check(model.Selection[4].ItemId == end, "blank rows can be moved");
        model.Move(end, 1);
        using (var target = new Database(true, true))
        {
            var selection = model.Selection;
            var result = LegendComposer.Compose(target, m, source, selection, Point3d.Origin, Matrix3d.Identity);
            Check(result.ItemCount == 4 && Math.Abs(result.Height - (m.Origin.Y - m.RowsTopY + 6 * m.RowHeight + m.BottomMargin)) < 1e-6, "two blank rows add two full row heights");
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var block = (BlockTableRecord)tr.GetObject(result.BlockDefinitionId, OpenMode.ForRead);
                var texts = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().ToList();
                Func<int, double> rowCenter = i => m.RowsTopY - (i + .5) * m.RowHeight - m.Origin.Y;
                Check(!texts.Any(t => Math.Abs(t.AlignmentPoint.Y - rowCenter(1)) < 1 || Math.Abs(t.AlignmentPoint.Y - rowCenter(5)) < 1), "blank rows contain no text");
                Check(texts.Count(t => t.TextString == "-" && Math.Abs(t.AlignmentPoint.X - (m.QuantityCenterX - m.Origin.X)) < .01) == 4, "quantity only on real rows");
                var separators = block.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Line>().Where(l => Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < .01 && Math.Abs(l.Length - m.TableWidth) < 1 && l.StartPoint.Y < m.RowsTopY - m.Origin.Y - 1).ToList();
                Check(separators.Count == 5, "row separators drawn between all six rows including blanks: " + separators.Count);
                Check(result.RowSymbolCopies[m.Rows[1].Id].Count > 0 && result.RowSymbolCopies[middle].Count == 0, "symbols only on real rows");
                tr.Commit();
            }
            target.SaveAs(Path.Combine(output, "generated", "storm-blank-rows.dwg"), DwgVersion.AC1032);
        }
        bool rejected = false; try { new PickerModel(index.Manifest("watershed"), false).AddBlank(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "watershed set refuses blank rows");
        string presets = Path.Combine(root, "work", "blank-preset-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
        new PresetStore(m, presets).SaveDefault("빈 행 포함", model.Selection);
        var restored = new PickerModel(m, false); restored.Apply(new PresetStore(m, presets).Load().Presets[0].Items);
        Check(restored.Selection.Select(s => s.IsBlank ? "blank" : s.ItemId).SequenceEqual(model.Selection.Select(s => s.IsBlank ? "blank" : s.ItemId)), "preset keeps blank rows and order");
        File.Delete(presets);
        log.Add("PASS: blank rows in middle and at bottom (no text/quantity/symbol, separators kept), ordering, preset round trip");
    }

    // ------------------------------------------------------------ 6. 템플릿에 항목 추가 → 자동 반영
    private static void VerifyTemplateEdit()
    {
        string work = Path.Combine(root, "work", "template-edit-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string baseDir = Path.Combine(work, "plugin"); Directory.CreateDirectory(Path.Combine(baseDir, TemplateRepository.TemplateFolder));
        foreach (var file in Directory.GetFiles(template, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(baseDir, TemplateRepository.TemplateFolder, file.Substring(template.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(file, destination);
        }
        string cache = Path.Combine(work, "cache");
        var unchanged = TemplateRepository.Resolve(baseDir, cache);
        Check(!unchanged.FromChangedTemplate && unchanged.Index.SourceDwgSha256 == Shipped().SourceDwgSha256 && !Directory.Exists(cache), "unchanged template uses shipped mapping without analysis");

        // 사용자가 하는 것처럼: 포장 표 마지막 행을 450 아래(빈 행)와 900 아래(표 바깥)에 복사하고 명칭을 고칩니다.
        string dwg = TemplateRepository.TemplateDwgPath(baseDir); var paving = Shipped().Manifest("paving"); var last = paving.Rows.Last();
        using (var db = LegendComposer.OpenSource(dwg))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var handles = last.ContentHandles.Concat(new[] { last.QuantityHandle }).Distinct().ToList();
                foreach (var copy in new[] { new { Dy = -450.0, Name = "추가항목A" }, new { Dy = -900.0, Name = "추가항목B" } })
                    foreach (string h in handles)
                    {
                        var clone = (Entity)((Entity)tr.GetObject(Id(db, h), OpenMode.ForRead)).Clone();
                        clone.TransformBy(Matrix3d.Displacement(new Vector3d(0, copy.Dy, 0)));
                        var text = clone as DBText; if (text != null && text.TextString == last.Name) text.TextString = copy.Name;
                        model.AppendEntity(clone); tr.AddNewlyCreatedDBObject(clone, true);
                    }
                tr.Commit();
            }
            db.SaveAs(dwg, DwgVersion.Current);
        }
        var changed = TemplateRepository.Resolve(baseDir, cache);
        var updated = changed.Index.Manifest("paving");
        Check(changed.FromChangedTemplate && changed.Notice != null && changed.Notice.Contains("포장 9→11"), "edited template is re-analyzed automatically: " + changed.Notice);
        Check(updated.Rows.Count == 11 && updated.Rows[9].Name == "추가항목A" && updated.Rows[10].Name == "추가항목B" && updated.Rows[10].Spec == last.Spec, "rows added inside and below the original table are registered");
        Check(updated.Rows.Take(9).Select(r => r.Id + r.Name + r.Spec).SequenceEqual(paving.Rows.Select(r => r.Id + r.Name + r.Spec)), "existing rows keep their ids");
        Check(changed.Index.Categories.Where(c => c.Id != "paving").All(c => c.ItemCount == Shipped().Categories.Single(x => x.Id == c.Id).ItemCount), "other categories unchanged");
        Check(TemplateRepository.Resolve(baseDir, cache).Notice == null, "second run reuses the analysis cache");
        using (var target = new Database(true, true))
        {
            var selection = updated.Rows.Skip(8).Select(r => new LegendSelection(r.Id, "7")).ToList();
            var result = LegendComposer.ComposeBatch(target, new[] { new LegendBatchItem(updated, selection, changed.Index.SourcePath(updated)) }, Point3d.Origin, Matrix3d.Identity, 100, false, true);
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var texts = result.EntityIds.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().Select(t => t.TextString).ToList();
                Check(texts.Contains("추가항목A") && texts.Contains("추가항목B") && texts.Count(t => t == "7") == 3, "new template rows generate in the drawing");
                var frame = result.EntityIds.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>().Single(p => p.Layer == "Defpoints");
                double low = Enumerable.Range(0, frame.NumberOfVertices).Min(i => frame.GetPoint2dAt(i).Y);
                Check(Math.Abs(low + (updated.Origin.Y - updated.RowsTopY + 3 * updated.RowHeight + updated.BottomMargin)) < .01, "outer frame fits the generated rows");
                tr.Commit();
            }
            target.SaveAs(Path.Combine(output, "generated", "paving-template-added-rows.dwg"), DwgVersion.AC1032);
        }
        // 고친 템플릿을 읽을 수 없게 망가뜨려도 이전 분석본으로 계속 쓸 수 있어야 합니다.
        File.WriteAllBytes(dwg, new byte[] { 1, 2, 3, 4 });
        var fallback = TemplateRepository.Resolve(baseDir, cache);
        Check(fallback.Source == TemplateSource.Recovered && fallback.Notice.Contains("마지막 정상 분석본") && fallback.Index.Manifest("paving").Rows.Count == 11, "broken template falls back to last good analysis");
        try { Directory.Delete(work, true); } catch (IOException) { }
        log.Add("PASS: template edit — rows added in the blank row and below the frame are registered automatically, generate correctly, cache reused, broken template falls back");
    }

    private static int CountLayers(Database db) { using (var tr = db.TransactionManager.StartTransaction()) return ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>().Count(); }
    private static int CountBlocks(Database db) { using (var tr = db.TransactionManager.StartTransaction()) return ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Cast<ObjectId>().Count(); }
    private static int CountEntities(Database db) { using (var tr = db.TransactionManager.StartTransaction()) return ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().Count(); }

    private static void Compare(Entity expected, Entity actual, string name)
    {
        Check(expected.GetType() == actual.GetType(), name + " entity type"); Check(expected.ColorIndex == actual.ColorIndex, name + " color"); Check(actual.Layer.EndsWith(expected.Layer, StringComparison.Ordinal), name + " source layer"); Check(expected.LineWeight == actual.LineWeight, name + " lineweight");
        if (!(expected is DBText) && !(expected is MText))
        {
            Extents3d a, b;
            try { a = expected.GeometricExtents; b = actual.GeometricExtents; }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { throw new System.Exception(name + " (" + expected.GetType().Name + ") extents: " + ex.ErrorStatus, ex); }
            Check(a.MinPoint.DistanceTo(b.MinPoint) < .05 && a.MaxPoint.DistanceTo(b.MaxPoint) < .05, name + " native geometry extents expected=" + a + " actual=" + b);
        }
        var textExpected = expected as DBText; var textActual = actual as DBText;
        if (textExpected != null)
        {
            Check(textExpected.AlignmentPoint.DistanceTo(textActual.AlignmentPoint) < .01, name + " text alignment");
            Check(textExpected.HorizontalMode == textActual.HorizontalMode && textExpected.VerticalMode == textActual.VerticalMode, name + " text justification");
            Check(Math.Abs(textExpected.WidthFactor - textActual.WidthFactor) < 1e-8 && Math.Abs(textExpected.Rotation - textActual.Rotation) < 1e-8, name + " text width rotation");
            Check(textExpected.TextString == textActual.TextString && Math.Abs(textExpected.Height - textActual.Height) < 1e-9, name + " text");
        }
        var mt = expected as MText; if (mt != null) { var other = (MText)actual; Check(mt.Contents == other.Contents && mt.Location.DistanceTo(other.Location) < 1e-7 && Math.Abs(mt.TextHeight - other.TextHeight) < 1e-8, name + " mtext"); }
        var p = expected as Polyline; var q = actual as Polyline;
        if (p != null) { Check(p.NumberOfVertices == q.NumberOfVertices && p.Closed == q.Closed, name + " polyline shape"); for (int i = 0; i < p.NumberOfVertices; i++) { Check(p.GetPoint3dAt(i).DistanceTo(q.GetPoint3dAt(i)) < 1e-6, name + " vertex"); Check(Math.Abs(p.GetBulgeAt(i) - q.GetBulgeAt(i)) < 1e-9, name + " bulge"); } }
        var c = expected as Circle; if (c != null) Check(Math.Abs(c.Radius - ((Circle)actual).Radius) < 1e-8, name + " radius");
        var h = expected as Hatch; if (h != null) Check(h.PatternName == ((Hatch)actual).PatternName && h.NumberOfLoops == ((Hatch)actual).NumberOfLoops, name + " hatch pattern");
    }

    /// <summary>개발용: 배포 Template의 미리보기 도형(preview-catalog.json)을 C# 분석기로 다시 만듭니다.</summary>
    [CommandMethod("LEGENDREBUILDPREVIEW")]
    public void RebuildPreview()
    {
        string folder = Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "Template");
        string report = Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "work", "rebuild-preview.txt");
        try
        {
            var shipped = CatalogIndex.Load(Path.Combine(folder, CatalogIndex.FileName));
            var first = shipped.Manifests.First(); string source = shipped.SourcePath(first);
            TemplateAnalysis analysis;
            using (var db = LegendComposer.OpenSource(source)) analysis = TemplateAnalyzer.Analyze(db, first.SourceFile, first.SourceSha256, shipped.SourceDwg, shipped.SourceDwgSha256);
            Check(analysis.Manifests.Count == shipped.Categories.Count && analysis.Manifests.All(m => m.Rows.Count == shipped.Manifest(m.CategoryId).Rows.Count), "same mapping");
            analysis.Preview.Save(Path.Combine(folder, CatalogIndex.PreviewFileName));
            File.WriteAllText(report, "PASS: preview rebuilt, entities=" + analysis.Preview.Entities.Count + ", warnings=" + analysis.Warnings.Count);
        }
        catch (System.Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
    }

    /// <summary>동시 분석 시험 준비: 배포 Template을 LEGEND_CONC_INSTALL에 복사하고 포장 표에 행을 하나 추가해 '고친 템플릿'을 만듭니다.</summary>
    [CommandMethod("LEGENDCONCSETUP")]
    public void ConcurrencySetup()
    {
        string install = Environment.GetEnvironmentVariable("LEGEND_CONC_INSTALL"), report = Path.Combine(install, "setup.txt");
        try
        {
            string template = Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "Template");
            foreach (var source in Directory.GetFiles(template, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(install, "Template", source.Substring(template.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination, true);
            }
            ReviewVerification.UseTemplate(template);
            ReviewVerification.AddPavingRows(TemplateRepository.TemplateDwgPath(install), 1, "동시분석");
            File.WriteAllText(report, "PASS: setup");
        }
        catch (System.Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
    }

    /// <summary>동시 분석 시험: 준비 신호(ready) 후 go 파일이 생기면 바로 같은 템플릿·같은 캐시로 Resolve하고 결과를 씁니다.</summary>
    [CommandMethod("LEGENDCONCURRENT")]
    public void Concurrent()
    {
        string install = Environment.GetEnvironmentVariable("LEGEND_CONC_INSTALL"), cache = Environment.GetEnvironmentVariable("LEGEND_CONC_CACHE");
        string id = Environment.GetEnvironmentVariable("LEGEND_CONC_ID"), report = Path.Combine(install, "result-" + id + ".txt");
        try
        {
            File.WriteAllText(Path.Combine(install, "ready-" + id), "");
            var wait = DateTime.UtcNow;
            while (!File.Exists(Path.Combine(install, "go")) && (DateTime.UtcNow - wait).TotalSeconds < 180) System.Threading.Thread.Sleep(5);
            var started = DateTime.UtcNow;
            var bundle = TemplateRepository.Resolve(install, cache);
            File.WriteAllLines(report, new[]
            {
                "PASS: resolved",
                "source=" + bundle.Source,
                "dir=" + Path.GetFileName(bundle.Index.DirectoryPath),
                "paving=" + bundle.Index.Manifest("paving").Rows.Count,
                "started=" + started.ToString("HH:mm:ss.fff"),
                "ms=" + (int)(DateTime.UtcNow - started).TotalMilliseconds
            });
        }
        catch (System.Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
    }

    /// <summary>진단용: LEGEND_ANALYZE_DWG의 템플릿 DWG(복사본)를 현재 분석기로 읽어 공종별 마지막 행·기호·경고를 work/analyze-dwg.txt에 씁니다.</summary>
    [CommandMethod("LEGENDANALYZEDWG")]
    public void AnalyzeDwg()
    {
        string report = Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "work", "analyze-dwg.txt");
        var lines = new List<string>();
        try
        {
            string dwg = Environment.GetEnvironmentVariable("LEGEND_ANALYZE_DWG");
            TemplateAnalysis analysis;
            using (var db = LegendComposer.OpenSource(dwg)) analysis = TemplateAnalyzer.Analyze(db, Path.GetFileName(dwg), "diagnostic", Path.GetFileName(dwg), "diagnostic");
            foreach (var m in analysis.Manifests)
            {
                lines.Add(m.CategoryName + ": " + m.Rows.Count + "행");
                foreach (var row in m.Rows.Skip(Math.Max(0, m.Rows.Count - 2)))
                {
                    var shapes = row.SymbolHandles.Select(h => analysis.Preview.Entities.FirstOrDefault(e => e.Handle == h)).Where(e => e != null).Sum(e => e.Primitives.Count);
                    lines.Add("  " + row.Id + " '" + row.Name + "' " + row.Spec + " 기호객체=[" + string.Join(",", row.SymbolHandles) + "] 미리보기도형=" + shapes);
                }
            }
            lines.Add("경고:"); lines.AddRange(analysis.Warnings.Select(w => "  - " + w));
            lines.Add("PASS: analyzed");
        }
        catch (System.Exception ex) { lines.Add("FAIL: " + ex); }
        File.WriteAllLines(report, lines);
    }

    /// <summary>진단용: 복합 선종류(U형측구 H-DICHETCU1/2)를 DWG·DXF·생성 결과에서 비교합니다.</summary>
    private static string SafeText(LinetypeTableRecord lt, int i) { try { return lt.TextAt(i); } catch (Autodesk.AutoCAD.Runtime.Exception) { return ""; } }

    [CommandMethod("LEGENDLTCHECK")]
    public void LinetypeCheck()
    {
        string rootDir = Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"); var lines = new List<string>();
        try
        {
            Func<Database, string, string> describe = (db, title) =>
            {
                var text = new List<string> { "[" + title + "] LTSCALE=" + db.Ltscale + " PSLTSCALE=" + db.Psltscale };
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    foreach (ObjectId id in table)
                    {
                        var lt = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (!lt.Name.Contains("DICHETCU")) continue;
                        var parts = new List<string>();
                        for (int i = 0; i < lt.NumDashes; i++)
                        {
                            string style = "";
                            if (!lt.ShapeStyleAt(i).IsNull) { var st = (TextStyleTableRecord)tr.GetObject(lt.ShapeStyleAt(i), OpenMode.ForRead); style = "style='" + st.Name + "' file=" + st.FileName + " shape=" + st.IsShapeFile; }
                            parts.Add("dash " + lt.DashLengthAt(i) + " shapeNo=" + lt.ShapeNumberAt(i) + " text='" + SafeText(lt, i) + "' scale=" + lt.ShapeScaleAt(i) + " offset=" + lt.ShapeOffsetAt(i) + " " + style);
                        }
                        text.Add("  " + lt.Name + " len=" + lt.PatternLength + " :: " + string.Join(" | ", parts));
                    }
                    tr.Commit();
                }
                return string.Join(Environment.NewLine, text);
            };
            string template = Path.Combine(rootDir, "Template");
            using (var dwg = LegendComposer.OpenSource(Path.Combine(template, "범례템플릿.dwg"))) lines.Add(describe(dwg, "template DWG"));
            using (var dxf = LegendComposer.OpenSource(Path.Combine(template, "범례템플릿.dxf"))) lines.Add(describe(dxf, "template DXF"));
            var index = CatalogIndex.Load(Path.Combine(template, CatalogIndex.FileName)); var m = index.Manifest("storm");
            using (var target = new Database(true, true))
            {
                LegendComposer.ComposeBatch(target, new[] { new LegendBatchItem(m, m.Rows.Select(r => new LegendSelection(r.Id, "-")).ToList(), index.SourcePath(m)) }, Point3d.Origin, Matrix3d.Identity, 100, false, true);
                lines.Add(describe(target, "generated (shipped source " + m.SourceFile + ")"));
                target.SaveAs(Path.Combine(rootDir, "work", "lt-generated.dwg"), DwgVersion.AC1032);
            }
        }
        catch (System.Exception ex) { lines.Add("FAIL: " + ex); }
        File.WriteAllLines(Path.Combine(rootDir, "work", "lt-check.txt"), lines);
    }

    private static string Fingerprint(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var parts = new List<string>();
            parts.Add("entities=" + ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().Count());
            parts.Add("layers=" + string.Join(",", ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).OrderBy(n => n)));
            parts.Add("linetypes=" + ((LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead)).Cast<ObjectId>().Count());
            parts.Add("styles=" + ((TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead)).Cast<ObjectId>().Count());
            parts.Add("blocks=" + string.Join(",", ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Cast<ObjectId>().Select(id => ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).OrderBy(n => n)));
            tr.Commit();
            return string.Join(" | ", parts);
        }
    }

    private static string baseline;

    /// <summary>실제 명령 흐름 검사 시작 전 도면 상태(UNDO 뒤 비교용).</summary>
    [CommandMethod("LEGENDBASELINE")]
    public void Baseline() { baseline = Fingerprint(AcApplication.DocumentManager.MdiActiveDocument.Database); }

    [CommandMethod("LEGENDCLICHECK")]
    public void CheckCli()
    {
        var doc = AcApplication.DocumentManager.MdiActiveDocument; string text = "FAIL: command output missing";
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
            var entities = ms.Cast<ObjectId>().Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
            bool wrapper = entities.OfType<BlockReference>().Any(b => ((BlockTableRecord)tr.GetObject(b.BlockTableRecord, OpenMode.ForRead)).Name.StartsWith("LEGEND_BATCH_"));
            var texts = entities.OfType<DBText>().ToList();
            var frame = entities.OfType<Polyline>().FirstOrDefault(p => p.Layer == "Defpoints");
            if (entities.Count > 100 && !wrapper && frame != null && texts.Any())
            {
                var extents = frame.GeometricExtents;
                var auxiliary = CatalogIndex.Load(Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "Template", CatalogIndex.FileName)).Manifest("auxiliary");
                if (Math.Abs(extents.MaxPoint.X - extents.MinPoint.X - auxiliary.FrameWidth * 3) < .01)
                    text = "PASS: actual NETLOAD + LEGENDALL (auxiliary) drew " + entities.Count + " loose entities (no wrapper block) at 1:300 (3x).";
            }
        }
        // 실제로 불러온 DLL 위치(검증 도구와 플러그인이 다른 Core를 섞어 쓰지 않았는지 확인용)
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("StormLegend")).Select(a => "LOADED: " + a.GetName().Name + " " + a.Location).ToList();
        File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "verification", "command-verification.txt"), new[] { text }.Concat(loaded));
    }

    /// <summary>UNDO 한 번(표시 지점까지)으로 범례 생성 전 상태로 돌아가는지 확인합니다.</summary>
    [CommandMethod("LEGENDUNDOCHECK")]
    public void CheckUndo()
    {
        string now = Fingerprint(AcApplication.DocumentManager.MdiActiveDocument.Database);
        string text = baseline != null && now == baseline ? "PASS: UNDO restored the drawing to the state before LEGENDALL (" + now.Split('|')[0].Trim() + ")" : "FAIL: after UNDO " + now + " / before " + baseline;
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LEGEND_PROJECT_ROOT"), "verification", "undo-verification.txt"), text);
    }
}
