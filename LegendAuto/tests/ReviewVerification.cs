using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using StormLegend;

/// <summary>
/// 코드 검토 항목(1~7)의 재현·회귀 검사. 각 항목을 따로 기록하고 끝에 실패 목록을 모아 보고합니다.
/// </summary>
public static class ReviewVerification
{
    private static readonly List<string> failures = new List<string>();
    private static List<string> log;
    private static int count;
    private static string root, template, work;

    private static void Expect(bool ok, string name) { count++; if (!ok) failures.Add(name); }

    public static int Run(string projectRoot, List<string> output)
    {
        root = projectRoot; log = output; template = Path.Combine(root, "Template"); failures.Clear(); count = 0;
        work = Path.Combine(root, "work", "review-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(work);
        Section("1 프리셋 재연결", PresetRelink);
        Section("2 템플릿별 복구", TemplateRecovery);
        Section("3 명칭 상속", NameInheritance);
        Section("5 미리보기 검증", PreviewValidation);
        Section("6 복사 단계 실패 복구", CopyFailure);
        Section("7 정의 충돌·레이어 상태·선종류", DefinitionConflicts);
        Section("8 표 레이어로 그린 기호", SymbolOnLegendLayer);
        try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (failures.Count > 0) throw new Exception("검토 항목 실패 " + failures.Count + "건: " + string.Join(" | ", failures));
        return count;
    }

    private static void Section(string name, Action body)
    {
        int before = failures.Count;
        try { body(); }
        catch (Exception ex) { failures.Add(name + " 예외: " + ex.GetType().Name + " " + ex.Message + " @ " + string.Join(" < ", (ex.StackTrace ?? "").Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Take(4).Select(l => l.Trim()))); }
        log.Add((failures.Count == before ? "PASS: " : "FAIL: ") + "[검토 " + name + "] " + (failures.Count == before ? "" : string.Join(" | ", failures.Skip(before))));
    }

    private static CatalogIndex Shipped() { return CatalogIndex.Load(Path.Combine(template, CatalogIndex.FileName)); }

    // ------------------------------------------------------------------ 1
    /// <summary>배포 우수 공종을 바꾼 사본(행 ID는 분석기처럼 위치로 다시 매김).</summary>
    private static TemplateManifest Variant(string sha, Action<List<LegendRow>> edit)
    {
        var m = TemplateManifest.Load(Path.Combine(template, "categories", "storm.json"));
        var rows = m.Rows.ToList(); edit(rows);
        for (int i = 0; i < rows.Count; i++) { rows[i].Id = "storm_" + (i + 1).ToString("000"); rows[i].GroupId = rows[i].Id; }
        m.Rows = rows; m.SourceSha256 = sha;
        foreach (var h in rows.SelectMany(r => r.ContentHandles.Concat(r.SymbolHandles).Concat(r.Cells.SelectMany(c => c.Handles)).Concat(new[] { r.QuantityHandle })).Where(h => h != null && !m.AllHandles.Contains(h)).Distinct().ToList()) m.AllHandles.Add(h);
        m.Validate();
        return m;
    }

    private static LegendRow Copy(LegendRow row, string handlePrefix)
    {
        Func<string, string> h = x => handlePrefix + x;
        return new LegendRow { Id = "x", Name = row.Name, Spec = row.Spec, Unit = row.Unit, Remark = row.Remark, Quantity = "-", GroupId = "x", Height = row.Height, SourceTopY = row.SourceTopY,
            QuantityHandle = row.QuantityHandle, QuantitySourceCenterY = row.QuantitySourceCenterY, SymbolHandles = row.SymbolHandles.Select(h).ToList(), ContentHandles = row.ContentHandles.Select(h).ToList(),
            Cells = row.Cells.Select(c => new LegendSourceCell { Column = c.Column, CenterX = c.CenterX, Width = c.Width, SourceCenterY = c.SourceCenterY, KeepRowOffset = c.KeepRowOffset, Handles = c.Handles.Select(h).ToList() }).ToList() };
    }

    private static void PresetRelink()
    {
        var original = TemplateManifest.Load(Path.Combine(template, "categories", "storm.json"));
        var steel = original.Rows.Single(r => r.Id == "storm_012"); var silent = original.Rows.Single(r => r.Id == "storm_013");
        Expect(steel.Name == silent.Name && steel.Spec == silent.Spec && steel.Remark != silent.Remark && steel.Key != silent.Key, "재현 조건: 우수 012/013은 명칭·규격이 같고 비고·원본 객체가 다름");
        string file = Path.Combine(work, "storm-preset.json");
        new PresetStore(original, file).SaveDefault("U형", new[] { new LegendSelection("storm_012", "3"), new LegendSelection("storm_013", "7", "무소음 U형", null) });

        Func<TemplateManifest, PresetDocument> load = m => { File.Copy(file, file + ".try", true); return new PresetStore(m, file + ".try").Load(); };

        // a) 동명·동규격 항목 앞에 행 삽입: 012→013, 013→014가 되어도 원래 기호에 연결
        var inserted = Variant("variant-insert", rows => rows.Insert(11, Copy(rows[4], "N1")));
        var doc = load(inserted);
        string silentId = doc.Presets[0].Items.Single(i => i.Quantity == "7").ItemId, steelId = doc.Presets[0].Items.Single(i => i.Quantity == "3").ItemId;
        Expect(doc.Issues.Count == 0 && inserted.FindRow(silentId).Remark == "무소음트렌치" && inserted.FindRow(steelId).Remark == "스틸그레이팅", "행 삽입 뒤 무소음트렌치/스틸그레이팅이 각자 원래 기호로 연결: " + silentId + "," + steelId);
        Expect(doc.Presets[0].Items.Single(i => i.Quantity == "7").NameOverride == "무소음 U형", "고친 명칭이 같은 기호를 따라감");
        // 이전 방식(명칭·규격만 저장)은 같은 행 번호의 다른 기호를 골랐을 것: 이제는 고르지 않고 이유를 알림
        string legacyFile = Path.Combine(work, "legacy-namespec.json");
        File.WriteAllText(legacyFile, "{\"schemaVersion\":1,\"categoryId\":\"storm\",\"sourceSha256\":\"" + original.SourceSha256 + "\",\"defaultPresetId\":\"p\",\"presets\":[{\"id\":\"p\",\"name\":\"old\",\"items\":[{\"itemId\":\"storm_013\",\"quantity\":\"7\",\"sourceName\":\"U형측구\",\"sourceSpec\":\"300x300\"}]}]}");
        var legacyDoc = new PresetStore(inserted, legacyFile).Load();
        Expect(legacyDoc.Presets[0].Items.Count == 0 && legacyDoc.Issues.Count == 1 && legacyDoc.Issues[0].Reason.Contains("2개"), "명칭·규격만 저장된 이전 형식 프리셋: 후보 2개면 임의 선택하지 않고 이유 표시: " + string.Join(";", legacyDoc.Issues));
        Expect(File.ReadAllText(legacyFile).Contains("storm_013"), "재연결 실패가 있으면 원본 프리셋 파일을 덮어쓰지 않음");

        // b) 행 삭제
        var deleted = Variant("variant-delete", rows => rows.RemoveAt(4));
        doc = load(deleted);
        Expect(doc.Issues.Count == 0 && deleted.FindRow(doc.Presets[0].Items.Single(i => i.Quantity == "7").ItemId).Remark == "무소음트렌치", "앞 행 삭제 뒤에도 같은 항목");
        // c) 행 이동(012와 013 순서 바꿈)
        var moved = Variant("variant-move", rows => { var t = rows[11]; rows[11] = rows[12]; rows[12] = t; });
        doc = load(moved);
        Expect(doc.Issues.Count == 0 && moved.FindRow(doc.Presets[0].Items.Single(i => i.Quantity == "7").ItemId).Remark == "무소음트렌치" && doc.RelinkedItems == 2, "행 순서를 바꿔도 같은 항목(2개 재연결)");
        // d) 명칭 변경(같은 원본 객체)
        var renamed = Variant("variant-rename", rows => rows[12].Name = "U형측구(무소음)");
        doc = load(renamed);
        Expect(doc.Issues.Count == 0 && renamed.FindRow(doc.Presets[0].Items.Single(i => i.Quantity == "7").ItemId).Name == "U형측구(무소음)", "템플릿에서 명칭만 바꿔도 같은 항목");
        // e) 기호만 바꿈(새 기호 객체) → 명칭·규격·비고로 유일하게 찾음
        var resymbol = Variant("variant-symbol", rows => rows[12] = Copy(rows[12], "S9"));
        doc = load(resymbol);
        Expect(doc.Issues.Count == 0 && resymbol.FindRow(doc.Presets[0].Items.Single(i => i.Quantity == "7").ItemId).Remark == "무소음트렌치", "기호만 바꿔도 명칭·규격·비고로 같은 항목");
        // f) 두 항목 모두 기호를 바꾸고 비고도 같아지면 → 고르지 않음
        var ambiguous = Variant("variant-ambiguous", rows => { rows[11] = Copy(rows[11], "A1"); rows[12] = Copy(rows[12], "A2"); rows[11].Remark = rows[12].Remark = ""; });
        doc = load(ambiguous);
        Expect(doc.Issues.Count == 2 && doc.Issues.All(i => i.Reason.Contains("단위·비고") || i.Reason.Contains("2개")) && doc.Presets[0].Items.Count == 0, "구분 근거가 사라지면 둘 다 다시 선택 대상으로 알림: " + string.Join(";", doc.Issues));
        // g) 구버전(근거 없음) 프리셋
        string oldFile = Path.Combine(work, "legacy-format.json");
        File.WriteAllText(oldFile, "{\"schemaVersion\":1,\"categoryId\":\"storm\",\"sourceSha256\":\"" + original.SourceSha256 + "\",\"defaultPresetId\":\"p\",\"presets\":[{\"id\":\"p\",\"name\":\"v4\",\"items\":[{\"itemId\":\"storm_013\",\"quantity\":\"5\"}]}]}");
        File.Copy(oldFile, oldFile + ".changed");
        var sameDoc = new PresetStore(original, oldFile).Load();
        Expect(sameDoc.Issues.Count == 0 && sameDoc.Presets[0].Items[0].SourceKey == silent.Key && File.ReadAllText(oldFile).Contains("sourceKey") && File.Exists(oldFile + ".bak"), "구버전 프리셋(같은 템플릿): ID로 복원하고 근거를 채워 저장(.bak 보관)");
        var changedDoc = new PresetStore(inserted, oldFile + ".changed").Load();
        Expect(changedDoc.Issues.Count == 1 && changedDoc.Issues[0].Reason.Contains("이전 버전") && changedDoc.Issues[0].ToString().Contains("storm_013"), "구버전 프리셋(템플릿 바뀜): ID만 믿지 않고 이름과 이유를 알림");
    }

    // ------------------------------------------------------------------ 2
    private static string Install(string name)
    {
        string baseDir = Path.Combine(work, name); Directory.CreateDirectory(Path.Combine(baseDir, TemplateRepository.TemplateFolder));
        foreach (var file in Directory.GetFiles(template, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(baseDir, TemplateRepository.TemplateFolder, file.Substring(template.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(file, destination);
        }
        return baseDir;
    }

    /// <summary>템플릿의 포장 표 마지막 행을 n번 아래로 복사해 저장(사용자가 행을 추가하는 것과 같음).</summary>
    /// <summary>Run 밖에서 AddPavingRows를 쓸 때(동시 분석 시험 준비) 배포 Template 위치를 정합니다.</summary>
    public static void UseTemplate(string folder) { template = folder; }

    public static void AddPavingRows(string dwg, int n, string prefix, bool withName = true, double startOffset = 450)
    {
        var paving = Shipped().Manifest("paving"); var last = paving.Rows.Last();
        using (var db = LegendComposer.OpenSource(dwg))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var handles = last.ContentHandles.Concat(new[] { last.QuantityHandle }).Distinct().ToList();
                for (int k = 0; k < n; k++)
                    foreach (string h in handles)
                    {
                        var source = (Entity)tr.GetObject(db.GetObjectId(false, new Handle(long.Parse(h, NumberStyles.HexNumber)), 0), OpenMode.ForRead);
                        var text = source as DBText;
                        if (text != null && text.TextString == last.Name && !withName) continue;
                        var clone = (Entity)source.Clone();
                        clone.TransformBy(Matrix3d.Displacement(new Vector3d(0, -startOffset - 450 * k, 0)));
                        var cloneText = clone as DBText; if (cloneText != null && cloneText.TextString == last.Name) cloneText.TextString = prefix + (k + 1);
                        model.AppendEntity(clone); tr.AddNewlyCreatedDBObject(clone, true);
                    }
                tr.Commit();
            }
            db.SaveAs(dwg, DwgVersion.Current);
        }
    }

    private static void TemplateRecovery()
    {
        string cache = Path.Combine(work, "cache");
        string a = Install("A"), b = Install("B");
        string dwgA = TemplateRepository.TemplateDwgPath(a), dwgB = TemplateRepository.TemplateDwgPath(b);
        AddPavingRows(dwgA, 1, "A항목");
        var a1 = TemplateRepository.Resolve(a, cache);
        Expect(a1.Source == TemplateSource.Changed && a1.Index.Manifest("paving").Rows.Count == 10, "A 수정본 분석(포장 10)");
        AddPavingRows(dwgB, 2, "B항목", true, 450);
        var b1 = TemplateRepository.Resolve(b, cache);
        Expect(b1.Source == TemplateSource.Changed && b1.Index.Manifest("paving").Rows.Count == 11, "B 수정본 분석(포장 11) — B가 가장 최근 분석본");
        File.WriteAllBytes(dwgA, new byte[] { 1, 2, 3 });
        var a2 = TemplateRepository.Resolve(a, cache);
        Expect(a2.Source == TemplateSource.Recovered && a2.Index.Manifest("paving").Rows.Count == 10 && a2.Notice.Contains("마지막 정상"), "A 손상 → B가 아니라 A의 마지막 정상 분석본으로 복구: 포장 " + a2.Index.Manifest("paving").Rows.Count);
        Expect(a2.Describe().StartsWith("복구본"), "프로그램 정보 표시: 복구본으로 구분");
        Expect(TemplateRepository.Resolve(b, cache).Source == TemplateSource.Changed, "B는 영향 없음");
        // 복구 대상 분석본의 source.dwg가 없으면 그 분석본은 쓰지 않음 → 배포본
        var aHistory = TemplateRepository.History(cache, dwgA);
        File.Delete(Path.Combine(aHistory[0], TemplateRepository.SnapshotFileName));
        var a3 = TemplateRepository.Resolve(a, cache);
        Expect(a3.Source == TemplateSource.Recovered && a3.Index.Manifest("paving").Rows.Count == 9, "복구 분석본의 source.dwg가 없으면 배포본으로(다른 설치본 B는 쓰지 않음)");
        // 최근 분석본 손상 → 이전 정상 분석본
        string c = Install("C"), dwgC = TemplateRepository.TemplateDwgPath(c);
        AddPavingRows(dwgC, 1, "C1"); TemplateRepository.Resolve(c, cache);
        AddPavingRows(dwgC, 1, "C2", true, 900); var c2 = TemplateRepository.Resolve(c, cache);
        Expect(c2.Index.Manifest("paving").Rows.Count == 11, "C 두 번째 수정본(포장 11)");
        var cHistory = TemplateRepository.History(cache, dwgC);
        File.WriteAllText(Path.Combine(cHistory[0], CatalogIndex.PreviewFileName), "{ broken");
        File.WriteAllBytes(dwgC, new byte[] { 9 });
        var c3 = TemplateRepository.Resolve(c, cache);
        Expect(c3.Source == TemplateSource.Recovered && c3.Index.Manifest("paving").Rows.Count == 10, "최근 분석본(미리보기 손상)을 건너뛰고 이전 정상 분석본으로: 포장 " + c3.Index.Manifest("paving").Rows.Count);
        Expect(TemplateRepository.Resolve(Install("D"), cache).Source == TemplateSource.Shipped, "고치지 않은 설치본은 배포본");
    }

    // ------------------------------------------------------------------ 8
    /// <summary>
    /// 사용자 재현: 템플릿 우수 표 아래에 행을 추가하면서 기호(사각형 폴리선)를 표 레이어(C-공사-범례-SO)로 그림
    /// → 이전 분석기는 '범례' 레이어 선을 모두 표 테두리로 보아 기호가 빠지고 글자·칸만 생성됐습니다.
    /// </summary>
    private static void SymbolOnLegendLayer()
    {
        var storm = Shipped().Manifest("storm"); var last = storm.Rows.Last();
        string dir = Install("LegendLayerSymbol"), dwg = TemplateRepository.TemplateDwgPath(dir);
        string rectangle, slash;
        double top = last.SourceTopY - last.Height, cx = last.Cells.Single(c => c.Column == 0).CenterX;
        using (var db = LegendComposer.OpenSource(dwg))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (string h in last.ContentHandles.Except(last.SymbolHandles).Concat(new[] { last.QuantityHandle }).Distinct())
                {
                    var clone = (Entity)tr.GetObject(db.GetObjectId(false, new Handle(long.Parse(h, NumberStyles.HexNumber)), 0), OpenMode.ForRead).Clone();
                    clone.TransformBy(Matrix3d.Displacement(new Vector3d(0, -last.Height, 0)));
                    var text = clone as DBText; if (text != null && text.TextString == last.Name) text.TextString = "사각기호";
                    model.AppendEntity(clone); tr.AddNewlyCreatedDBObject(clone, true);
                }
                var box = new Polyline(); box.SetDatabaseDefaults(db); box.Layer = "C-공사-범례-SO"; box.Closed = true;
                box.AddVertexAt(0, new Point2d(cx - 320, top - 115), 0, 0, 0); box.AddVertexAt(1, new Point2d(cx + 320, top - 115), 0, 0, 0);
                box.AddVertexAt(2, new Point2d(cx + 320, top - 335), 0, 0, 0); box.AddVertexAt(3, new Point2d(cx - 320, top - 335), 0, 0, 0);
                model.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true); rectangle = box.Handle.ToString();
                // 가로 한 획짜리 기호(행 경계가 아닌 높이)도 기호로 인정
                var line = new Line(new Point3d(cx - 300, top - 225, 0), new Point3d(cx + 300, top - 225, 0)); line.SetDatabaseDefaults(db); line.Layer = "C-우수-범례-SO";
                model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true); slash = line.Handle.ToString();
                tr.Commit();
            }
            db.SaveAs(dwg, DwgVersion.Current);
        }
        var bundle = TemplateRepository.Resolve(dir, Path.Combine(work, "cache8"));
        var m = bundle.Index.Manifest("storm"); var row = m.Rows.Last();
        Expect(m.Rows.Count == storm.Rows.Count + 1 && row.Name == "사각기호", "추가 행 인식: 우수 " + m.Rows.Count + " / " + row.Name);
        Expect(row.SymbolHandles.Contains(rectangle) && row.SymbolHandles.Contains(slash), "표 레이어로 그린 사각형·가로선이 기호로 등록: " + string.Join(",", row.SymbolHandles));
        Expect(!m.RowBoundaryHandles.Contains(rectangle) && !m.RowBoundaryHandles.Contains(slash), "추가 기호가 행 경계선으로 잘못 분류되지 않음");
        Expect(!bundle.Warnings.Any(w => w.Contains("사각기호") && w.Contains("기호가 없습니다")), "'기호가 없습니다' 경고 없음: " + string.Join(" / ", bundle.Warnings));
        var preview = PreviewCatalogFile.Load(bundle.Index.PreviewPath).Entities;
        Expect(new[] { rectangle, slash }.All(h => preview.Any(e => e.Handle == h && e.Primitives.Count > 0)), "미리보기 데이터에 추가 기호가 들어감");
        // 기존 행의 기호·테두리 분류는 그대로
        Expect(m.Rows.Take(storm.Rows.Count).Select(r => string.Join("+", r.SymbolHandles)).SequenceEqual(storm.Rows.Select(r => string.Join("+", r.SymbolHandles))), "기존 우수 행의 기호 객체 변화 없음");
        Expect(storm.RowBoundaryHandles.All(m.RowBoundaryHandles.Contains) && storm.VerticalBorderHandles.SequenceEqual(m.VerticalBorderHandles), "기존 표 테두리 분류 그대로");
        foreach (bool explode in new[] { true, false })
            using (var db = UserDrawing(false))
            {
                var item = new LegendBatchItem(m, new List<LegendSelection> { new LegendSelection(row.Id, "2") }, bundle.Index.SourcePath(m));
                LegendComposer.ComposeBatch(db, new[] { item }, Point3d.Origin, Matrix3d.Identity, 100, false, explode);
                int boxes = 0, strokes = 0;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Action<BlockTableRecord> scan = null;
                    scan = btr =>
                    {
                        foreach (ObjectId id in btr)
                        {
                            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                            var pl = e as Polyline; var ln = e as Line; var br = e as BlockReference;
                            if (pl != null && pl.Layer == "C-공사-범례-SO" && pl.Closed && pl.NumberOfVertices == 4 && Math.Abs(pl.GeometricExtents.MaxPoint.X - pl.GeometricExtents.MinPoint.X - 640) < .5) boxes++;
                            if (ln != null && ln.Layer == "C-우수-범례-SO" && Math.Abs(ln.Length - 600) < .5) strokes++;
                            if (br != null) scan((BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead));
                        }
                    };
                    scan((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead));
                    tr.Commit();
                }
                Expect(boxes == 1 && strokes == 1, (explode ? "개별 객체" : "블록") + " 생성 결과에 사각형 기호 " + boxes + "개 · 가로선 " + strokes + "개(각 1개여야 함)");
            }
    }

    // ------------------------------------------------------------------ 3
    private static void NameInheritance()
    {
        var aux = Shipped().Manifest("auxiliary"); var row32 = aux.FindRow("auxiliary_032");
        Expect(row32.NameFromAbove && row32.Cells.Single(x => x.Column == 1).Handles.Count == 0 && row32.Name == "노면표시", "원본 부대 32행(구분선으로 나뉜 빈 명칭 칸): 목록 이름만 위 행에서, 도면 명칭 칸은 템플릿처럼 비움");
        Expect(aux.FindRow("auxiliary_031").Cells.Single(x => x.Column == 1).Handles.SequenceEqual(new[] { "8C5" }), "실제 병합 명칭(29~31행)은 그대로");
        string cache = Path.Combine(work, "cache3");
        // 표 아래쪽 불완전한 복사 행(명칭 없음): 등록하지 않고 이유 경고
        string below = Install("NoNameBelow"); AddPavingRows(TemplateRepository.TemplateDwgPath(below), 1, "x", false, 900);
        var r1 = TemplateRepository.Resolve(below, cache);
        Expect(r1.Index.Manifest("paving").Rows.Count == 9 && r1.Warnings.Any(w => w.Contains("포장 11행") && w.Contains("원래 표 아래")), "표 아래 무명칭 복사 행은 이전 이름을 물려받지 않고 경고: " + string.Join(" / ", r1.Warnings));
        // 표 안 빈 행에 명칭만 빠뜨림: 등록하되 도면 명칭 칸은 비우고 경고
        string inside = Install("NoNameInside"); AddPavingRows(TemplateRepository.TemplateDwgPath(inside), 1, "x", false, 450);
        var r2 = TemplateRepository.Resolve(inside, cache);
        var row10 = r2.Index.Manifest("paving").Rows.Last();
        Expect(r2.Index.Manifest("paving").Rows.Count == 10 && row10.NameFromAbove && row10.Cells.Single(x => x.Column == 1).Handles.Count == 0 && r2.Warnings.Any(w => w.Contains("포장 10행") && w.Contains("명칭 칸이 비어")), "표 안 무명칭 행: 다른 행 글자를 복사하지 않고 경고");
        // 공종 하나 구조 손상(세로선 삭제) → 일부만 반영으로 구분
        string broken = Install("BrokenCategory"); var construction = Shipped().Manifest("construction");
        using (var db = LegendComposer.OpenSource(TemplateRepository.TemplateDwgPath(broken)))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var h in construction.VerticalBorderHandles.Take(2)) tr.GetObject(db.GetObjectId(false, new Handle(long.Parse(h, NumberStyles.HexNumber)), 0), OpenMode.ForWrite).Erase();
                tr.Commit();
            }
            db.SaveAs(TemplateRepository.TemplateDwgPath(broken), DwgVersion.Current);
        }
        var r3 = TemplateRepository.Resolve(broken, cache);
        Expect(r3.Notice != null && r3.Notice.StartsWith("⚠ 템플릿 일부만") && r3.Notice.Contains("공사") && !r3.Index.Categories.Any(x => x.Id == "construction") && r3.Index.Categories.Count == 6, "공종 하나 손상 → '일부만 반영'으로 구분해 알림: " + r3.Notice);
    }

    // ------------------------------------------------------------------ 5
    private static void PreviewValidation()
    {
        var index = Shipped();
        Func<Action<PreviewCatalogFile>, string> broken = change =>
        {
            var file = PreviewCatalogFile.Load(index.PreviewPath); change(file);
            try { file.Validate(index.Manifests); return null; } catch (InvalidDataException ex) { return ex.Message; }
        };
        Expect(broken(f => { }) == null, "정상 미리보기 통과");
        Expect(broken(f => f.Entities = null) != null, "목록 null 거부");
        Expect(broken(f => f.Entities.Add(new PreviewEntity { Handle = f.Entities[0].Handle })) != null && broken(f => f.Entities.Add(new PreviewEntity { Handle = f.Entities[0].Handle })).Contains("두 번"), "중복 핸들 거부");
        Expect(broken(f => f.Entities.Add(new PreviewEntity { Handle = null })) != null, "빈 핸들 거부");
        Expect(broken(f => f.Entities.First(e => e.Primitives.Any(p => p.Points != null && p.Points.Count > 0)).Primitives.First(p => p.Points != null && p.Points.Count > 0).Points[0].X = double.NaN) != null, "NaN 좌표 거부");
        Expect(broken(f => f.Entities.First(e => e.Primitives.Count > 0).Primitives[0].Height = double.PositiveInfinity) != null, "무한대 크기 거부");
        Expect(broken(f => f.Entities.RemoveAt(0)) != null, "필요한 핸들 누락 거부");
        // 배포 미리보기가 망가져도 원본 DWG로 다시 만들어 연다
        string baseDir = Install("BrokenPreview");
        File.WriteAllText(Path.Combine(baseDir, TemplateRepository.TemplateFolder, CatalogIndex.PreviewFileName), "{ not json");
        var bundle = TemplateRepository.Resolve(baseDir, Path.Combine(work, "cache5"));
        bool rebuilt = false; try { PreviewCatalogFile.Load(bundle.Index.PreviewPath).Validate(bundle.Index.Manifests); rebuilt = true; } catch (Exception) { }
        Expect(bundle.Source == TemplateSource.Shipped && rebuilt && bundle.Notice.Contains("다시 만들었습니다") && bundle.Index.Categories.Sum(x => x.ItemCount) == 94, "배포 미리보기 손상 → 원본으로 재생성: " + bundle.Notice);
    }

    // ------------------------------------------------------------------ 6
    private sealed class Snapshot
    {
        public List<string> Lines = new List<string>();
        public static Snapshot Of(Database db)
        {
            var s = new Snapshot();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)) { var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead); s.Lines.Add("L " + l.Name + " c" + l.Color.ColorIndex + " lock" + l.IsLocked + " off" + l.IsOff + " frz" + l.IsFrozen); }
                foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead)) s.Lines.Add("T " + ((LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
                foreach (ObjectId id in (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead)) { var t = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead); s.Lines.Add("S " + t.Name + " " + t.FileName); }
                foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)) { var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead); s.Lines.Add("B " + b.Name + " n" + b.Cast<ObjectId>().Count()); }
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)) { var e = (Entity)tr.GetObject(id, OpenMode.ForRead); s.Lines.Add("E " + e.Handle + " " + e.GetType().Name + " " + e.Layer + " c" + e.ColorIndex + " " + e.GeometricExtents); }
                tr.Commit();
            }
            s.Lines.Sort(StringComparer.Ordinal); return s;
        }
        public string Diff(Snapshot other) { var a = new HashSet<string>(Lines); var b = new HashSet<string>(other.Lines); return "추가:" + string.Join(",", b.Except(a).Take(5)) + " 사라짐:" + string.Join(",", a.Except(b).Take(5)); }
        public bool Same(Snapshot other) { return Lines.SequenceEqual(other.Lines); }
    }

    private static Database UserDrawing(bool lockSameLayer)
    {
        var db = new Database(true, true);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            var user = new LayerTableRecord { Name = "USER" }; var userId = layers.Add(user); tr.AddNewlyCreatedDBObject(user, true);
            if (lockSameLayer)
            {
                var same = new LayerTableRecord { Name = "C-우수-범례-SO", Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3), IsLocked = true };
                layers.Add(same); tr.AddNewlyCreatedDBObject(same, true);
            }
            var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var line = new Line(new Point3d(0, 0, 0), new Point3d(1000, 500, 0)) { LayerId = userId, ColorIndex = 5 };
            model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            tr.Commit();
        }
        return db;
    }

    private static List<LegendBatchItem> Items(params string[] categories)
    {
        var index = Shipped();
        return categories.Select(id => { var m = index.Manifest(id); return new LegendBatchItem(m, m.Rows.Select(r => new LegendSelection(r.Id, "1")).ToList(), index.SourcePath(m)); }).ToList();
    }

    private static void CopyFailure()
    {
        foreach (var point in new[] { "explode-clone:1", "explode-transform:0", "explode-draworder", "block-clone" })
        {
            using (var db = UserDrawing(false))
            {
                var before = Snapshot.Of(db); string message = null;
                LegendComposer.FaultPoint = p => { if (p == point) throw new InvalidOperationException("의도한 실패 " + point); };
                try { LegendComposer.ComposeBatch(db, Items("storm", "sewage"), Point3d.Origin, Matrix3d.Identity, 300, false, !point.StartsWith("block"), 300); }
                catch (Exception ex) { message = ex.Message; }
                finally { LegendComposer.FaultPoint = null; }
                var after = Snapshot.Of(db);
                Expect(message != null && message.Contains("의도한 실패"), point + ": 최초 오류가 그대로 전달");
                // 알려진 제한: AutoCAD는 Defpoints 레이어를 지우거나 PURGE할 수 없습니다(빈 레이어 하나만 허용, 명령 안에서는 UNDO로 사라짐).
                var extra = after.Lines.Except(before.Lines).ToList(); var gone = before.Lines.Except(after.Lines).ToList();
                Expect(gone.Count == 0 && extra.All(l => l.StartsWith("L Defpoints ")) && !after.Lines.Any(l => l.StartsWith("E ") && l.Contains(" Defpoints ")), point + ": 실패 뒤 객체·레이어·선종류·문자 스타일·블록 정의가 실패 전과 같음(빈 Defpoints 레이어만 예외) " + before.Diff(after));
            }
        }
        // 정리 작업까지 실패해도 최초 오류가 가려지지 않음
        using (var db = UserDrawing(false))
        {
            string message = null;
            LegendComposer.FaultPoint = p => { if (p == "explode-transform:0") throw new InvalidOperationException("최초 오류"); if (p == "cleanup") throw new IOException("정리 실패"); };
            try { LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true); }
            catch (Exception ex) { message = ex.Message + (ex.Data.Contains("cleanup") ? " / " + ex.Data["cleanup"] : ""); }
            finally { LegendComposer.FaultPoint = null; }
            Expect(message != null && message.StartsWith("최초 오류") && message.Contains("정리 실패"), "정리 실패가 최초 오류를 가리지 않고 함께 기록: " + message);
        }
        // 같은 이름의 잠긴 레이어가 있는 도면
        using (var db = UserDrawing(true))
        {
            string message = null; LegendBatchResult result = null;
            try { result = LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true); }
            catch (Exception ex) { message = ex.GetType().Name + " " + ex.Message; }
            Expect(result != null, "잠긴 동일 이름 레이어가 있어도 생성: " + message);
            if (result != null)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layer = (LayerTableRecord)tr.GetObject(((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))["C-우수-범례-SO"], OpenMode.ForRead);
                    Expect(layer.IsLocked && layer.Color.ColorIndex == 3, "사용자 레이어의 잠금·색은 바꾸지 않음");
                    tr.Commit();
                }
                Expect(result.Warnings.Any(w => w.Contains("C-우수-범례-SO") && w.Contains("잠김")), "잠긴 레이어 안내: " + string.Join(" / ", result.Warnings));
            }
        }
    }

    // ------------------------------------------------------------------ 7
    private static void DefinitionConflicts()
    {
        // 같은 이름·다른 내용의 레이어: 기본은 기존 도면 정의 유지 + 경고, 선택 시 범례 전용 이름으로 따로 넣기
        foreach (var policy in new[] { DefinitionConflict.UseDrawing, DefinitionConflict.Separate })
            using (var db = new Database(true, true))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    var same = new LayerTableRecord { Name = "C-우수-범례-SO", Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3) };
                    layers.Add(same); tr.AddNewlyCreatedDBObject(same, true);
                    var off = new LayerTableRecord { Name = "C-우수-우수계획-SO", IsOff = true }; layers.Add(off); tr.AddNewlyCreatedDBObject(off, true);
                    var frozen = new LayerTableRecord { Name = "C-우수-U형측구-SO", IsFrozen = true, IsPlottable = false }; layers.Add(frozen); tr.AddNewlyCreatedDBObject(frozen, true);
                    tr.Commit();
                }
                var result = LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true, 300, policy);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var user = (LayerTableRecord)tr.GetObject(table["C-우수-범례-SO"], OpenMode.ForRead);
                    Expect(user.Color.ColorIndex == 3, policy + ": 사용자 레이어 정의는 덮어쓰지 않음");
                    var layersUsed = result.EntityIds.Select(id => ((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer).Distinct().ToList();
                    if (policy == DefinitionConflict.UseDrawing)
                        Expect(layersUsed.Contains("C-우수-범례-SO") && result.Warnings.Any(w => w.Contains("C-우수-범례-SO") && w.Contains("색")), "기존 정의 유지 + 내용 차이 경고: " + string.Join(" / ", result.Warnings.Take(3)));
                    else
                        Expect(layersUsed.Contains("C-우수-범례-SO_범례") && !layersUsed.Contains("C-우수-범례-SO") && table.Has("C-우수-범례-SO_범례"), "범례 전용 이름으로 따로 넣기: " + string.Join(",", layersUsed.Where(n => n.Contains("범례-SO"))));
                    if (policy == DefinitionConflict.UseDrawing)
                        Expect(result.Warnings.Any(w => w.Contains("C-우수-우수계획-SO") && w.Contains("꺼짐")) && result.Warnings.Any(w => w.Contains("C-우수-U형측구-SO") && w.Contains("동결") && w.Contains("출력 안 함")), "기존 정의 사용: 꺼짐·동결·출력 안 함 레이어 안내");
                    else
                    {
                        // 따로 넣기에서는 내용이 다른 레이어가 새 이름으로 들어가므로, 사용자 레이어의 꺼짐·동결 상태는 범례에 영향이 없습니다.
                        var userOff = (LayerTableRecord)tr.GetObject(table["C-우수-우수계획-SO"], OpenMode.ForRead);
                        bool renamed = layersUsed.Contains("C-우수-우수계획-SO_범례"); bool warned = result.Warnings.Any(w => w.StartsWith("레이어 'C-우수-우수계획-SO'") && w.Contains("꺼짐"));
                        Expect(userOff.IsOff && (renamed ? !warned : warned), "따로 넣기: 사용자 레이어 상태는 그대로, 범례가 그 레이어를 쓸 때만 꺼짐 안내(새 이름=" + renamed + ", 안내=" + warned + ")");
                    }
                    tr.Commit();
                }
            }
        // 내용이 같은 정의는 충돌로 보지 않음(같은 도면에 두 번 생성)
        using (var db = new Database(true, true))
        {
            LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true);
            var second = LegendComposer.ComposeBatch(db, Items("storm"), new Point3d(0, -20000, 0), Matrix3d.Identity, 100, false, true);
            Expect(!second.Warnings.Any(w => w.Contains("다릅니다")), "같은 내용의 정의는 충돌 아님: " + string.Join(" / ", second.Warnings));
        }
        // 선종류 표시: LTSCALE 차이 · 주석 축척(MSLTSCALE) · 블록 출력
        using (var db = new Database(true, true))
        {
            db.Ltscale = 10;
            var exploded = LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true);
            double ltExploded = LinetypeScaleOf(db, exploded.EntityIds);
            var block = LegendComposer.ComposeBatch(db, Items("storm"), new Point3d(0, -20000, 0), Matrix3d.Identity, 100, false, false);
            double ltBlock;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var reference = (BlockReference)tr.GetObject(block.BlockReferenceId, OpenMode.ForRead);
                var batch = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                var inner = batch.Cast<ObjectId>().Select(id => (BlockReference)tr.GetObject(id, OpenMode.ForRead)).Select(r => (BlockTableRecord)tr.GetObject(r.BlockTableRecord, OpenMode.ForRead)).SelectMany(b => b.Cast<ObjectId>()).ToList();
                ltBlock = LinetypeScaleOf(db, inner);
                tr.Commit();
            }
            Expect(Math.Abs(ltExploded - 30) < 1e-6, "도면 LTSCALE 10: 개별 출력 U형측구 선종류 축척 300/10=30 (실제 " + ltExploded + ")");
            Expect(Math.Abs(ltBlock - 30) < 1e-6, "도면 LTSCALE 10: 블록 출력 안쪽도 같은 보정 30 (실제 " + ltBlock + ")");
        }
        using (var db = new Database(true, true))
        {
            AnnotationScale hundred = null;
            foreach (ObjectContext context in db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES")) { var scale = context as AnnotationScale; if (scale != null && Math.Abs(scale.Scale - 0.01) < 1e-9) hundred = scale; }
            if (hundred != null)
            {
                db.Cannoscale = hundred; db.MsLtScale = true;
                var result = LegendComposer.ComposeBatch(db, Items("storm"), Point3d.Origin, Matrix3d.Identity, 100, false, true);
                double lt = LinetypeScaleOf(db, result.EntityIds);
                Expect(Math.Abs(lt - 3) < 1e-6, "주석 축척 1:100 + MSLTSCALE=1: 선종류 축척 300/100=3 (실제 " + lt + ")");
            }
            else log.Add("INFO: 주석 축척 1:100 항목이 없어 MSLTSCALE 검사는 건너뜀");
        }
        // 회전·이동된 UCS
        using (var db = new Database(true, true))
        {
            var ucs = Matrix3d.Displacement(new Vector3d(500, 600, 0)) * Matrix3d.Rotation(Math.PI / 6, Vector3d.ZAxis, Point3d.Origin);
            var insertion = new Point3d(1000, 2000, 0);
            var result = LegendComposer.ComposeBatch(db, Items("paving"), insertion, ucs, 100, false, true);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var frame = result.EntityIds.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>().Single(p => p.Layer == "Defpoints");
                var corners = Enumerable.Range(0, frame.NumberOfVertices).Select(i => frame.GetPoint3dAt(i)).ToList();
                Expect(corners.Any(p => p.DistanceTo(insertion) < 1e-6), "회전·이동 UCS: 표 왼쪽 위가 지정점");
                var x = ucs.CoordinateSystem3d.Xaxis; var y = ucs.CoordinateSystem3d.Yaxis;
                var next = corners.Where(p => Math.Abs((p - insertion).DotProduct(y)) < 1e-6).OrderByDescending(p => (p - insertion).DotProduct(x)).First(); // 윗변의 오른쪽 끝
                Expect((next - insertion).GetNormal().IsEqualTo(x, new Tolerance(1e-6, 1e-6)), "회전·이동 UCS: 표 윗변이 UCS X축 방향");
                tr.Commit();
            }
        }
        // 원본 기호 안 중첩 블록: 템플릿 정의와 같은 내용
        using (var db = new Database(true, true))
        using (var src = LegendComposer.OpenSource(Shipped().SourcePath(Shipped().Manifest("water"))))
        {
            var result = LegendComposer.ComposeBatch(db, Items("water"), Point3d.Origin, Matrix3d.Identity, 100, false, true);
            using (var tr = db.TransactionManager.StartTransaction())
            using (var st = src.TransactionManager.StartTransaction())
            {
                var inserts = result.EntityIds.Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().ToList();
                var srcBlocks = (BlockTable)st.GetObject(src.BlockTableId, OpenMode.ForRead);
                Expect(inserts.Count > 0 && inserts.All(r => { var b = (BlockTableRecord)tr.GetObject(r.BlockTableRecord, OpenMode.ForRead); return srcBlocks.Has(b.Name) && ((BlockTableRecord)st.GetObject(srcBlocks[b.Name], OpenMode.ForRead)).Cast<ObjectId>().Count() == b.Cast<ObjectId>().Count(); }), "중첩 블록 " + inserts.Count + "개: 이름·내용이 템플릿 정의와 같음");
                st.Commit(); tr.Commit();
            }
        }
    }

    private static double LinetypeScaleOf(Database db, IEnumerable<ObjectId> ids)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var scales = ids.Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).OfType<Polyline>().Where(p => p.Linetype == "H-DICHETCU1").Select(p => p.LinetypeScale).ToList();
            tr.Commit();
            return scales.Count == 1 ? scales[0] : double.NaN;
        }
    }
}
