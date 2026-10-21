using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using StormLegend;

public static class ReviewFixVerification
{
    private static int count;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception("review-fix: " + label); count++; }
    private static PresetDocument Read(string path)
    {
        using (var f = File.OpenRead(path)) return (PresetDocument)new DataContractJsonSerializer(typeof(PresetDocument)).ReadObject(f);
    }
    private static void Write(string path, PresetDocument data)
    {
        using (var f = File.Create(path)) new DataContractJsonSerializer(typeof(PresetDocument)).WriteObject(f, data);
    }
    public static int Run(string root, List<string> log)
    {
        count = 0;
        string work = Path.Combine(root, "work", "fix-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var index = CatalogIndex.Load(Path.Combine(root, "Template", CatalogIndex.FileName));
        var original = index.Manifest("storm");
        string file = Path.Combine(work, "presets.json");
        var store = new PresetStore(original, file);
        var a = store.SaveDefault("A", new[] { new LegendSelection(original.Rows[0].Id, "1") });
        var b = store.SaveDefault("B", new[] { new LegendSelection(original.Rows[1].Id, "99") });
        var raw = Read(file); var broken = raw.Presets.Single(p => p.Name == "B").Items[0];
        broken.ItemId = "missing"; broken.SourceKey = "unknown"; broken.SourceSymbol = "unknown"; broken.SourceName = "removed";
        raw.Presets.Add(new LegendPreset { Id = "bad", Name = "invalid", Items = null }); Write(file, raw);
        string before = File.ReadAllText(file);
        var display = store.Load();
        Check(display.Issues.Count == 2 && File.ReadAllText(file) == before, "invalid whole preset is reported without rewriting");
        store.SaveDefault("A", new[] { new LegendSelection(original.Rows[0].Id, "2", "", "") });
        raw = Read(file);
        Check(raw.Presets.Single(p => p.Name == "B").Items.Single().Quantity == "99" && raw.Presets.Single(p => p.Name == "B").Items.Single().ItemId == "missing", "saving A preserves unresolved B");
        Check(raw.Presets.Any(p => p.Id == "bad" && p.Items == null), "saving A preserves malformed preset");
        store.SaveDefault("B", new[] { new LegendSelection(original.Rows[1].Id, "3") });
        Check(Read(file).Presets.Single(p => p.Name == "B").UnresolvedItems.Single().Quantity == "99", "overwriting B archives unresolved originals");
        Check(Read(file).Presets.Single(p => p.Name == "A").Items.Single().NameOverride == "", "explicit empty label persists");
        store.Rename(a.Id, "renamed"); Check(Read(file).Presets.Any(p => p.Id == a.Id && p.Name == "renamed"), "rename preserves identity");
        store.ClearDefault(); Check(Read(file).StartEmpty && Read(file).DefaultPresetId == null, "clear default persists empty-start policy");
        store.Delete(b.Id); Check(!Read(file).Presets.Any(p => p != null && p.Id == b.Id), "delete only requested preset");
        Check(Directory.GetFiles(work, "*.backup-*").Length >= 4, "mutations preserve backups");
        log.Add("PASS: review-fix preset isolation, recovery archive, rename/delete/default, empty labels");

        foreach (double value in new[] { double.NaN, double.PositiveInfinity, 1e15 })
        {
            var m = TemplateManifest.Load(Path.Combine(root, "Template", "categories", "storm.json")); m.Rows[0].Height = value;
            bool rejected = false; try { m.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid manifest height rejected: " + value);
        }

        using (var target = Fixture(true, 4))
        {
            for (int run = 0; run < 3; run++) using (var source = Fixture(false, 2))
            {
                ObjectId rootId;
                using (var tr = source.TransactionManager.StartTransaction())
                { rootId = ((BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead))["Root"]; tr.Commit(); }
                var warnings = new List<string>();
                LegendDefinitions.Reconcile(source, target, new[] { rootId }, DefinitionConflict.Separate, warnings);
                using (var tr = source.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(source.LayerTableId, OpenMode.ForRead);
                    var blocks = (BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead);
                    Check(layers.Has("L_범례"), "ByLayer dependency isolates layer with same linetype name");
                    Check(blocks.Has("Symbol_범례"), "same extents / opposite geometry isolates symbol");
                    tr.Commit();
                }
                // 실제 생성처럼 감싸는 블록은 매번 새 이름(대상에 같은 이름이 있으면 Ignore로 아무것도 복사되지 않음)
                using (var tr = source.TransactionManager.StartTransaction()) { ((BlockTableRecord)tr.GetObject(rootId, OpenMode.ForWrite)).Name = "Root_" + run; tr.Commit(); }
                source.WblockCloneObjects(new ObjectIdCollection(new[] { rootId }), target.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                using (var tr = target.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(target.LayerTableId, OpenMode.ForRead);
                    var blocks = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead);
                    Check(layers.Has("L_범례") && !layers.Has("L_범례2") && !blocks.Has("Symbol_범례2"), "repeated isolation reuses equivalent definitions (run " + (run + 1) + "): layers=["
                        + string.Join(",", layers.Cast<ObjectId>().Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name)) + "] blocks=["
                        + string.Join(",", blocks.Cast<ObjectId>().Select(id => ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).Where(n => !n.StartsWith("*"))) + "] warnings=" + string.Join(" / ", warnings));
                    var preserved = (LayerTableRecord)tr.GetObject(layers["L"], OpenMode.ForRead);
                    Check(((LinetypeTableRecord)tr.GetObject(preserved.LinetypeObjectId, OpenMode.ForRead)).PatternLength == 8, "original linetype unchanged");
                    tr.Commit();
                }
            }
        }
        log.Add("PASS: review-fix geometry comparison, ByLayer dependencies, repeated definition reuse");

        string install = Path.Combine(work, "install");
        foreach (var source in Directory.GetFiles(Path.Combine(root, "Template"), "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(install, "Template", source.Substring(Path.Combine(root, "Template").Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination);
        }
        string cache = Path.Combine(work, "cache");
        File.AppendAllText(Path.Combine(install, "Template", "범례템플릿.dxf"), "broken");
        var recovered = TemplateRepository.Resolve(install, cache);
        Check(recovered.Index.Manifests.All(m => m.SourceFile == "source.dwg"), "damaged DXF rebuilt from DWG");
        string snapshot = recovered.Index.SourcePath(recovered.Index.Manifests.First());
        var timestamp = File.GetLastWriteTimeUtc(snapshot);
        TemplateRepository.AnalyzeIntoCache(TemplateRepository.TemplateDwgPath(install), recovered.Index.SourceDwgSha256, recovered.Index.DirectoryPath);
        Check(File.GetLastWriteTimeUtc(snapshot) == timestamp, "valid cache publication is immutable");
        File.Delete(TemplateRepository.TemplateDwgPath(install));
        Check(TemplateRepository.Resolve(install, cache).IsRecovered, "missing DWG uses recorded recovery");
        log.Add("PASS: review-fix invalid numeric values, damaged DXF recovery, immutable cache, missing DWG");

        PresetArchiveAndPartialSave(root, work, original);
        log.Add("PASS: review-fix preset archive not re-reported, clean presets migrated while broken ones stay raw, backups pruned");
        TextOnlyBlockDifference();
        log.Add("PASS: review-fix same-name block differing only in text is isolated");
        RepeatedTemplateGeneration(index);
        log.Add("PASS: review-fix repeated generation (template, both policies) adds no definitions and no new conflicts");
        LockedLayerRollback(index);
        log.Add("PASS: review-fix copy failure on a locked same-name layer is fully rolled back; cleanup failure + copy failure both reported");
        ChangedDxfContent(root, work);
        log.Add("PASS: review-fix DXF content changed (still valid) is detected before using shipped data");
        var preview = PreviewCatalogFile.Load(Path.Combine(root, "Template", CatalogIndex.PreviewFileName));
        var hatches = preview.Entities.SelectMany(e => e.Primitives).Where(p => p.Fill == "hatch").ToList();
        Check(hatches.Count > 0 && hatches.All(p => p.Pattern != null && p.Pattern.Count > 0), "shipped preview hatches carry real pattern lines (" + hatches.Count + ")");
        log.Add("PASS: review-fix hatch preview pattern data (" + hatches.Count + " hatches)");
        return count;
    }

    // ------------------------------------------------------------ 프리셋: 보관 항목·부분 저장·백업 개수
    private static void PresetArchiveAndPartialSave(string root, string work, TemplateManifest original)
    {
        string file = Path.Combine(work, "presets-partial.json");
        var store = new PresetStore(original, file);
        store.SaveDefault("Good", new[] { new LegendSelection(original.Rows[2].Id, "5") });
        store.SaveDefault("Broken", new[] { new LegendSelection(original.Rows[3].Id, "7") });
        var raw = Read(file); var lost = raw.Presets.Single(p => p.Name == "Broken").Items[0];
        lost.ItemId = "gone"; lost.SourceKey = "gone"; lost.SourceSymbol = "gone"; lost.SourceName = "지운 항목"; Write(file, raw);
        // 다시 골라 저장하면 연결 못 한 원본은 보관되지만, 다음부터 매번 경고하지 않습니다.
        store.SaveDefault("Broken", new[] { new LegendSelection(original.Rows[4].Id, "8") });
        var reopened = store.Load();
        Check(reopened.Issues.Count == 0 && reopened.ArchivedItems == 1, "archived originals are kept but not re-reported: issues=" + reopened.Issues.Count + " archived=" + reopened.ArchivedItems);
        Check(Read(file).Presets.Single(p => p.Name == "Broken").UnresolvedItems.Single().ItemId == "gone", "archived original stays in the file");

        // 템플릿이 바뀐 뒤: 확실히 연결된 프리셋만 새 해시로 저장, 문제 있는 프리셋은 원본 그대로
        raw = Read(file); var broken = raw.Presets.Single(p => p.Name == "Broken").Items[0];
        broken.ItemId = "gone2"; broken.SourceKey = "gone2"; broken.SourceSymbol = "gone2"; broken.SourceName = "또 지운 항목"; Write(file, raw);
        var changed = TemplateManifest.Load(Path.Combine(root, "Template", "categories", "storm.json")); changed.SourceSha256 = "changed-template";
        var display = new PresetStore(changed, file).Load();
        raw = Read(file);
        Check(display.Issues.Count == 1, "only the broken preset is reported after template change: " + display.Issues.Count);
        Check(raw.Presets.Single(p => p.Name == "Good").SourceSha256 == "changed-template", "clean preset migration is persisted even while another preset has issues");
        Check(raw.Presets.Single(p => p.Name == "Broken").Items.Single().ItemId == "gone2" && raw.SourceSha256 == original.SourceSha256, "broken preset and document hash stay untouched");

        for (int i = 0; i < 15; i++) store.SaveDefault("Good", new[] { new LegendSelection(original.Rows[2].Id, (i + 1).ToString()) });
        int backups = Directory.GetFiles(work, "presets-partial.json.backup-*").Length;
        Check(backups <= PresetStore.KeepBackups && backups > 0, "backups are pruned to the newest " + PresetStore.KeepBackups + ": " + backups);
    }

    // ------------------------------------------------------------ 정의 비교: 크기·위치가 같고 글자만 다른 동명 블록
    private static void TextOnlyBlockDifference()
    {
        using (var target = TextBlock("A"))
        using (var source = TextBlock("B"))
        using (var same = TextBlock("A"))
        {
            var warnings = new List<string>();
            LegendDefinitions.Reconcile(same, target, new[] { RootOf(same) }, DefinitionConflict.Separate, warnings);
            Check(warnings.Count == 0 && Has(same, "Tag") && !Has(same, "Tag_범례"), "identical text block is not a conflict: " + string.Join(" / ", warnings));
            LegendDefinitions.Reconcile(source, target, new[] { RootOf(source) }, DefinitionConflict.Separate, warnings);
            Check(Has(source, "Tag_범례"), "same size/position but different text is isolated");
        }
    }

    private static Database TextBlock(string text)
    {
        var db = new Database(true, true);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var tag = new BlockTableRecord { Name = "Tag" }; blocks.Add(tag); tr.AddNewlyCreatedDBObject(tag, true);
            var label = new DBText { TextString = text, Height = 10, Position = Point3d.Origin }; label.SetDatabaseDefaults(db);
            tag.AppendEntity(label); tr.AddNewlyCreatedDBObject(label, true);
            var root = new BlockTableRecord { Name = "Root" }; blocks.Add(root); tr.AddNewlyCreatedDBObject(root, true);
            var reference = new BlockReference(Point3d.Origin, tag.ObjectId); root.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            tr.Commit();
        }
        return db;
    }

    private static ObjectId RootOf(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction()) { var id = ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))["Root"]; tr.Commit(); return id; }
    }

    private static bool Has(Database db, string block)
    {
        using (var tr = db.TransactionManager.StartTransaction()) { bool has = ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Has(block); tr.Commit(); return has; }
    }

    // ------------------------------------------------------------ 실제 템플릿 반복 생성: 정의 수가 늘지 않고, 범례가 넣은 정의 때문에 새 충돌이 생기지 않음
    private static List<LegendBatchItem> AllItems(CatalogIndex index)
    {
        return index.Manifests.Select(m => new LegendBatchItem(m, m.Rows.Select(r => new LegendSelection(r.Id, "1")).ToList(), index.SourcePath(m))).ToList();
    }

    private static string Counts(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            Func<ObjectId, int> n = id => ((SymbolTable)tr.GetObject(id, OpenMode.ForRead)).Cast<ObjectId>().Count();
            string text = "layers=" + n(db.LayerTableId) + " linetypes=" + n(db.LinetypeTableId) + " styles=" + n(db.TextStyleTableId) + " blocks=" + n(db.BlockTableId);
            tr.Commit(); return text;
        }
    }

    private static void RepeatedTemplateGeneration(CatalogIndex index)
    {
        foreach (var policy in new[] { DefinitionConflict.Separate, DefinitionConflict.UseDrawing })
            using (var db = new Database(true, true))
            {
                var first = LegendComposer.ComposeBatch(db, AllItems(index), Point3d.Origin, Matrix3d.Identity, 100, false, true, 300, policy);
                string afterFirst = Counts(db);
                for (int run = 1; run <= 2; run++)
                {
                    var again = LegendComposer.ComposeBatch(db, AllItems(index), new Point3d(0, -30000 * run, 0), Matrix3d.Identity, 100, false, true, 300, policy);
                    Check(Counts(db) == afterFirst, policy + " run " + (run + 1) + " adds no definitions: " + afterFirst + " → " + Counts(db));
                    var conflicts = again.Warnings.Where(w => w.Contains("다릅니다") || w.Contains("내용이 달라")).ToList();
                    var before = first.Warnings.Where(w => w.Contains("다릅니다") || w.Contains("내용이 달라")).Select(w => w.Split('\'')[1]).ToList();
                    Check(conflicts.All(w => before.Contains(w.Split('\'')[1])) && conflicts.Count <= before.Count,
                        policy + " run " + (run + 1) + ": no new conflicts caused by the legend's own definitions: " + string.Join(" / ", conflicts.Take(4)));
                    if (policy == DefinitionConflict.Separate) Check(!again.Warnings.Any(w => w.Contains("_범례2")), "Separate does not number new copies on repeat");
                }
            }
    }

    // ------------------------------------------------------------ 잠긴 동명 레이어 + 복사 실패 → 완전히 되돌림 / 정리 실패와 동시 발생 → 둘 다 보고
    private static void LockedLayerRollback(CatalogIndex index)
    {
        var storm = new List<LegendBatchItem> { AllItems(index).Single(i => i.Manifest.CategoryId == "storm") };
        using (var db = new Database(true, true))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                var locked = new LayerTableRecord { Name = "C-우수-범례-SO", Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3), IsLocked = true };
                layers.Add(locked); tr.AddNewlyCreatedDBObject(locked, true); tr.Commit();
            }
            string before = Counts(db); int entitiesBefore = ModelCount(db);
            string message = null;
            LegendComposer.FaultPoint = p => { if (p == "explode-draworder") throw new InvalidOperationException("의도한 실패"); };
            try { LegendComposer.ComposeBatch(db, storm, Point3d.Origin, Matrix3d.Identity, 100, false, true, 300, DefinitionConflict.UseDrawing); }
            catch (Exception ex) { message = ex.Message + (ex.Data.Contains("cleanup") ? " / " + ex.Data["cleanup"] : ""); }
            finally { LegendComposer.FaultPoint = null; }
            Check(message != null && message.StartsWith("의도한 실패") && !message.Contains("되돌리"), "locked layer: original error kept, cleanup succeeded: " + message);
            Check(ModelCount(db) == entitiesBefore, "locked layer: no legend entities left (" + entitiesBefore + " → " + ModelCount(db) + ")");
            Check(Counts(db) == before || OnlyDefpointsAdded(db, before), "locked layer: definitions back to before (" + before + " → " + Counts(db) + ")");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layer = (LayerTableRecord)tr.GetObject(((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))["C-우수-범례-SO"], OpenMode.ForRead);
                Check(layer.IsLocked && layer.Color.ColorIndex == 3, "locked user layer stays locked and unchanged"); tr.Commit();
            }
        }
        using (var db = new Database(true, true))
        {
            string message = null;
            LegendComposer.FaultPoint = p => { if (p == "explode-transform:0") throw new InvalidOperationException("최초 오류"); if (p == "cleanup") throw new IOException("정리 실패"); };
            try { LegendComposer.ComposeBatch(db, storm, Point3d.Origin, Matrix3d.Identity, 100, false, true); }
            catch (Exception ex) { message = ex.Message + (ex.Data.Contains("cleanup") ? " / " + ex.Data["cleanup"] : ""); }
            finally { LegendComposer.FaultPoint = null; }
            Check(message != null && message.StartsWith("최초 오류") && message.Contains("정리 실패"), "copy failure + cleanup failure are both reported: " + message);
        }
    }

    private static int ModelCount(Database db)
    {
        using (var tr = db.TransactionManager.StartTransaction()) { int n = ((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead)).Cast<ObjectId>().Count(); tr.Commit(); return n; }
    }

    private static bool OnlyDefpointsAdded(Database db, string before)
    {
        // AutoCAD는 Defpoints 레이어를 지우지 못하게 합니다(알려진 제한): 레이어 1개 차이만 허용
        using (var tr = db.TransactionManager.StartTransaction())
        {
            bool hasDefpoints = ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Has("Defpoints"); tr.Commit();
            var b = before.Split(' ').Select(x => int.Parse(x.Split('=')[1])).ToArray(); var a = Counts(db).Split(' ').Select(x => int.Parse(x.Split('=')[1])).ToArray();
            return hasDefpoints && a[0] == b[0] + 1 && a[1] == b[1] && a[2] == b[2] && a[3] == b[3];
        }
    }

    // ------------------------------------------------------------ DWG는 그대로, DXF 내용만 바뀜(파일은 정상)
    private static void ChangedDxfContent(string root, string work)
    {
        string install = Path.Combine(work, "install-dxf-changed");
        foreach (var source in Directory.GetFiles(Path.Combine(root, "Template"), "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(install, "Template", source.Substring(Path.Combine(root, "Template").Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination);
        }
        string dxf = Path.Combine(install, "Template", "범례템플릿.dxf");
        var bytes = File.ReadAllBytes(dxf); var text = System.Text.Encoding.UTF8.GetString(bytes);
        int at = text.IndexOf("홈통받이", StringComparison.Ordinal);
        Check(at > 0, "fixture: DXF contains a label to change");
        File.WriteAllText(dxf, text.Substring(0, at) + "홈통받X" + text.Substring(at + 4), new System.Text.UTF8Encoding(bytes.Length > 2 && bytes[0] == 0xEF));
        var bundle = TemplateRepository.Resolve(install, Path.Combine(work, "cache-dxf"));
        Check(bundle.Index.Manifests.All(m => m.SourceFile == TemplateRepository.SnapshotFileName) && bundle.Notice != null && bundle.Notice.Contains("손상"),
            "changed DXF is not trusted; data rebuilt from the DWG: " + bundle.Notice);
    }

    private static Database Fixture(bool reverse, double dash)
    {
        var db = new Database(true, true);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lts = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForWrite);
            var lt = new LinetypeTableRecord { Name = "DASH", PatternLength = dash * 2, NumDashes = 2 };
            lt.SetDashLengthAt(0, dash); lt.SetDashLengthAt(1, -dash); var ltId = lts.Add(lt); tr.AddNewlyCreatedDBObject(lt, true);
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            var layer = new LayerTableRecord { Name = "L", LinetypeObjectId = ltId };
            var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var symbol = new BlockTableRecord { Name = "Symbol" }; blocks.Add(symbol); tr.AddNewlyCreatedDBObject(symbol, true);
            var line = new Line(new Point3d(0, reverse ? 10 : 0, 0), new Point3d(10, reverse ? 0 : 10, 0)) { LayerId = layerId };
            symbol.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            var root = new BlockTableRecord { Name = "Root" }; blocks.Add(root); tr.AddNewlyCreatedDBObject(root, true);
            var reference = new BlockReference(Point3d.Origin, symbol.ObjectId); root.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
            tr.Commit();
        }
        return db;
    }
}
