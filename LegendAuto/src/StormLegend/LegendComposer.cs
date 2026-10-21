using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("StormLegend.Verify")]
namespace StormLegend
{
    public sealed class LegendResult
    {
        public ObjectId BlockReferenceId { get; internal set; }
        public ObjectId BlockDefinitionId { get; internal set; }
        public int ItemCount { get; internal set; }
        public double Width { get; internal set; }
        public double Height { get; internal set; }
        public double ScaleFactor { get; internal set; }
        public IDictionary<string, ObjectId> OriginalEntityCopies { get; internal set; }
        public IDictionary<string, IList<ObjectId>> RowSymbolCopies { get; internal set; }
    }

    public sealed class LegendBatchPlacement
    {
        public string CategoryId { get; internal set; }
        public int ItemCount { get; internal set; }
        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double Width { get; internal set; }
        public double Height { get; internal set; }
        /// <summary>블록으로 생성한 경우 공종 블록 참조. 분해 생성이면 Null입니다.</summary>
        public ObjectId BlockReferenceId { get; internal set; }
        /// <summary>분해 생성한 경우 이 공종에 속한 도면 객체.</summary>
        public IList<ObjectId> EntityIds { get; internal set; }
    }

    /// <summary>사용자 도면에 같은 이름이지만 내용이 다른 레이어·문자 스타일·선종류·블록이 있을 때.</summary>
    public enum DefinitionConflict
    {
        /// <summary>기존 도면 정의를 그대로 쓰고(덮어쓰지 않음) 차이를 알려줍니다. 기본값.</summary>
        UseDrawing,
        /// <summary>범례 전용 이름(이름_범례)으로 템플릿 정의를 따로 넣습니다. 기존 정의는 그대로 둡니다.</summary>
        Separate
    }

    public sealed class LegendBatchResult
    {
        /// <summary>생성은 됐지만 사용자에게 알려야 할 것(정의 충돌, 꺼짐·동결·잠금·출력 안 함 레이어 등).</summary>
        public IList<string> Warnings { get; internal set; } = new List<string>();
        /// <summary>블록으로 생성한 경우에만 값이 있습니다.</summary>
        public ObjectId BlockReferenceId { get; internal set; }
        public ObjectId BlockDefinitionId { get; internal set; }
        /// <summary>분해 생성한 경우 도면에 추가된 모든 객체.</summary>
        public IList<ObjectId> EntityIds { get; internal set; }
        public bool Exploded { get; internal set; }
        public int ItemCount { get; internal set; }
        public int BlankRowCount { get; internal set; }
        public double Width { get; internal set; }
        public double Height { get; internal set; }
        public double ScaleFactor { get; internal set; }
        public IList<LegendBatchPlacement> Placements { get; internal set; }
    }

    public static class LegendComposer
    {
        /// <summary>공종 배치 순서(왼쪽 → 오른쪽). 목록에 없는 공종은 맨 오른쪽에 둡니다.</summary>
        public static readonly string[] CategoryOrder = { "construction", "watershed", "storm", "sewage", "water", "paving", "auxiliary" };
        public const double CategoryGap = 300;
        public const int MinScale = 1, MaxScale = 100000;

        /// <summary>검증 전용: 사용자 도면으로 복사하는 도중 일부러 실패시키는 지점(평소에는 null).</summary>
        internal static Action<string> FaultPoint;
        private static void Fault(string point) { var hook = FaultPoint; if (hook != null) hook(point); }

        /// <summary>
        /// 여러 공종을 가로 한 줄로 생성합니다. explode=true이면 블록 없이 개별 객체(선·문자·해치 등)로 그립니다.
        /// 모든 작업은 별도 데이터베이스에서 끝낸 뒤 한 번에 복사하므로 실패해도 사용자 도면에 일부만 남지 않습니다.
        /// </summary>
        public static LegendBatchResult ComposeBatch(Database target, IList<LegendBatchItem> items, Point3d insertionWcs, Matrix3d ucs,
            int scaleDenominator = 100, bool original = false, bool explode = false, double categoryGap = CategoryGap, DefinitionConflict conflicts = DefinitionConflict.UseDrawing)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (items == null || items.Count == 0 || items.Any(i => i == null || i.Manifest == null) || items.Select(i => i.Manifest.CategoryId).Distinct().Count() != items.Count)
                throw new ArgumentException("생성할 공종을 선택하세요. 공종은 중복할 수 없습니다.");
            if (scaleDenominator < MinScale || scaleDenominator > MaxScale) throw new ArgumentOutOfRangeException("scaleDenominator", "축척은 1~100000의 정수로 입력하세요.");
            if (double.IsNaN(categoryGap) || categoryGap < 0 || categoryGap > 100000) throw new ArgumentOutOfRangeException("categoryGap", "공종 사이 간격이 잘못되었습니다.");
            var axes = CheckUcs(ucs);
            items = items.OrderBy(item => { int rank = Array.IndexOf(CategoryOrder, item.Manifest.CategoryId); return rank < 0 ? int.MaxValue : rank; }).ToList();
            double factor = scaleDenominator / 100.0;
            var place = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, insertionWcs, axes.Xaxis, axes.Yaxis, axes.Zaxis)
                        * Matrix3d.Scaling(factor, Point3d.Origin);

            using (var sources = new SourceCache())
            using (var staging = new Database(true, true))
            {
                // 1) 공종별 표를 격리된 데이터베이스에서 완성합니다. 같은 원본 파일은 한 번만 읽습니다.
                var prepared = new List<LegendResult>();
                double sourceLinetypeBasis = 1;
                foreach (var item in items)
                {
                    var source = sources.Open(item.SourcePath, item.Manifest);
                    if (prepared.Count == 0) sourceLinetypeBasis = LinetypeBasis(source.Database, true);
                    var rows = original ? item.Manifest.Rows : ValidateSelection(item.Manifest, item.Selection);
                    prepared.Add(Build(staging, item.Manifest, source, rows, original ? null : item.Selection, original));
                }

                // 2) 가로 한 줄 배치 좌표를 계산합니다.
                var placements = new List<LegendBatchPlacement>();
                double x = 0, width = 0, height = 0;
                for (int j = 0; j < prepared.Count; j++)
                {
                    placements.Add(new LegendBatchPlacement { CategoryId = items[j].Manifest.CategoryId, ItemCount = prepared[j].ItemCount, X = x * factor, Y = 0,
                        Width = prepared[j].Width * factor, Height = prepared[j].Height * factor });
                    width = x + prepared[j].Width; height = Math.Max(height, prepared[j].Height); x += prepared[j].Width + categoryGap;
                }
                var result = new LegendBatchResult { ItemCount = prepared.Sum(p => p.ItemCount), ScaleFactor = factor, Width = width * factor, Height = height * factor,
                    Placements = placements, Exploded = explode, BlankRowCount = original ? 0 : items.Sum(i => i.Selection.Count(s => s.IsBlank)) };

                // 3) 같은 이름·다른 내용의 정의를 확인합니다(사용자 도면은 읽기만 함).
                LegendDefinitions.Reconcile(staging, target, prepared.Select(p => p.BlockDefinitionId), conflicts, result.Warnings);

                // 4) 사용자 도면으로 한 번에 복사합니다. 도중에 실패하면 이번에 들어간 것만 지워 실패 전으로 되돌립니다.
                var imports = new List<IdMapping>();
                double linetypeRatio = sourceLinetypeBasis / LinetypeBasis(target, IsModelSpace(target));
                try
                {
                    if (explode) CopyExploded(staging, target, prepared, placements, factor, place, result, imports, factor * linetypeRatio);
                    else CopyAsBlock(staging, target, prepared, placements, factor, place, result, imports, linetypeRatio);
                }
                catch (Exception failure)
                {
                    RollBackKeepingError(target, imports, failure);
                    throw;
                }
                // 5) 꺼짐·동결·잠금·출력 안 함 레이어 안내(상태는 바꾸지 않음).
                try
                {
                    var layers = explode ? LayersOfEntities(target, result.EntityIds) : LegendDefinitions.LayersOf(target, new[] { result.BlockDefinitionId }).Concat(LayersOfEntities(target, new[] { result.BlockReferenceId }));
                    LegendDefinitions.ReportLayerStates(target, layers, result.Warnings);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                return result;
            }
        }

        /// <summary>정리 작업이 실패해도 최초 오류를 가리지 않습니다(정리 오류는 Data["cleanup"]에 덧붙임).</summary>
        private static void RollBackKeepingError(Database target, IList<IdMapping> imports, Exception original)
        {
            try { Fault("cleanup"); LegendDefinitions.RollBack(target, imports); }
            catch (Exception cleanup)
            {
                try
                {
                    original.Data["cleanup"] = "되돌리는 중 오류: " + cleanup.Message;
                    if (cleanup.Data.Contains("remaining")) original.Data["remaining"] = cleanup.Data["remaining"];
                }
                catch (ArgumentException) { }
            }
        }

        private static IEnumerable<string> LayersOfEntities(Database db, IEnumerable<ObjectId> ids)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var names = ids.Where(id => id.IsValid && !id.IsErased).Select(id => ((Entity)tr.GetObject(id, OpenMode.ForRead, false, true)).Layer).Distinct().ToList();
                tr.Commit();
                return names;
            }
        }

        /// <summary>단일 공종을 블록으로 삽입합니다(검증용 API).</summary>
        public static LegendResult Compose(Database target, TemplateManifest manifest, string sourcePath, IList<LegendSelection> selected, Point3d insertionWcs, Matrix3d ucs)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            var rows = ValidateSelection(manifest, selected);
            return Import(target, manifest, sourcePath, rows, selected, insertionWcs, ucs, false);
        }

        public static LegendResult ComposeOriginal(Database target, TemplateManifest manifest, string sourcePath, Point3d insertionWcs, Matrix3d ucs)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            return Import(target, manifest, sourcePath, manifest.Rows, null, insertionWcs, ucs, true);
        }

        private static IList<LegendRow> ValidateSelection(TemplateManifest manifest, IList<LegendSelection> selected)
        {
            if (selected == null || selected.Count == 0) throw new ArgumentException(manifest.CategoryName + ": 항목을 한 개 이상 선택하세요.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selection in selected)
            {
                if (selection == null || !seen.Add(selection.ItemId) || (!selection.IsBlank && manifest.FindRow(selection.ItemId) == null))
                    throw new ArgumentException("알 수 없거나 중복된 항목 선택입니다.");
                if (!selection.IsBlank) LegendSelection.NormalizeQuantity(selection.Quantity);
            }
            return manifest.ResolveRows(selected);
        }

        private static CoordinateSystem3d CheckUcs(Matrix3d ucs)
        {
            var axes = ucs.CoordinateSystem3d;
            if (Math.Abs(axes.Xaxis.Length - 1) > 1e-8 || Math.Abs(axes.Yaxis.Length - 1) > 1e-8 || Math.Abs(axes.Zaxis.Length - 1) > 1e-8 || Math.Abs(axes.Xaxis.DotProduct(axes.Yaxis)) > 1e-8)
                throw new ArgumentException("직교 UCS가 필요합니다.");
            return axes;
        }

        // 블록 이름·레이어 등 이름 있는 정의가 이미 도면에 있으면 그대로 다시 씁니다.
        // (같은 도면에서 여러 번 생성해도 "$0$레이어" 같은 복제 레이어가 계속 늘어나지 않습니다.)
        private const DuplicateRecordCloning TargetCloning = DuplicateRecordCloning.Ignore;

        private static void CopyAsBlock(Database staging, Database target, IList<LegendResult> prepared, IList<LegendBatchPlacement> placements, double factor, Matrix3d place, LegendBatchResult result, IList<IdMapping> imports, double linetypeRatio)
        {
            ObjectId batchId;
            using (var tr = staging.TransactionManager.StartTransaction())
            {
                // 블록은 도형과 함께 선종류 무늬도 커지므로 배율은 그대로 두고, 도면 LTSCALE·주석 축척 차이만 범례 전용 정의 안에서 맞춥니다.
                if (Math.Abs(linetypeRatio - 1) > 1e-9)
                    foreach (var item in prepared)
                        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(item.BlockDefinitionId, OpenMode.ForRead))
                        {
                            var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (entity != null && !(entity is BlockReference)) { entity.UpgradeOpen(); entity.LinetypeScale *= linetypeRatio; }
                        }
                var blocks = (BlockTable)tr.GetObject(staging.BlockTableId, OpenMode.ForWrite);
                var batch = new BlockTableRecord { Name = "LEGEND_BATCH_" + Guid.NewGuid().ToString("N"), Origin = Point3d.Origin };
                batchId = blocks.Add(batch); tr.AddNewlyCreatedDBObject(batch, true);
                for (int j = 0; j < prepared.Count; j++)
                {
                    var reference = new BlockReference(new Point3d(placements[j].X / factor, 0, 0), prepared[j].BlockDefinitionId);
                    placements[j].BlockReferenceId = batch.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                }
                tr.Commit();
            }
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var mapping = new IdMapping(); imports.Add(mapping);
                staging.WblockCloneObjects(new ObjectIdCollection(new[] { batchId }), target.BlockTableId, mapping, TargetCloning, false);
                Fault("block-clone");
                var finalId = mapping[batchId].Value;
                var owner = (BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForWrite);
                var reference = new BlockReference(Point3d.Origin, finalId); reference.TransformBy(place);
                result.BlockReferenceId = owner.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                result.BlockDefinitionId = finalId;
                foreach (var placement in placements) placement.BlockReferenceId = mapping[placement.BlockReferenceId].Value;
                tr.Commit();
            }
        }

        private static void CopyExploded(Database staging, Database target, IList<LegendResult> prepared, IList<LegendBatchPlacement> placements, double factor, Matrix3d place, LegendBatchResult result, IList<IdMapping> imports, double linetypeFactor)
        {
            // 공종 블록 안의 객체를 그리기 순서대로 직접 사용자 도면 공간에 복사한 뒤 위치·축척을 적용합니다.
            var drawOrders = new List<List<ObjectId>>();
            using (var tr = staging.TransactionManager.StartTransaction())
            {
                foreach (var item in prepared)
                {
                    var block = (BlockTableRecord)tr.GetObject(item.BlockDefinitionId, OpenMode.ForRead);
                    var order = (DrawOrderTable)tr.GetObject(block.DrawOrderTableId, OpenMode.ForRead);
                    drawOrders.Add(order.GetFullDrawOrder(0).Cast<ObjectId>().Where(id => !id.IsErased).ToList());
                }
                tr.Commit();
            }
            var all = new List<ObjectId>(); var content = new List<ObjectId>();
            using (var tr = target.TransactionManager.StartTransaction())
            {
                for (int j = 0; j < prepared.Count; j++)
                {
                    var map = new IdMapping(); imports.Add(map);
                    staging.WblockCloneObjects(new ObjectIdCollection(drawOrders[j].ToArray()), target.CurrentSpaceId, map, TargetCloning, false);
                    Fault("explode-clone:" + j);
                    var transform = place * Matrix3d.Displacement(new Vector3d(placements[j].X / factor, 0, 0));
                    var ids = new List<ObjectId>();
                    foreach (var sourceId in drawOrders[j])
                    {
                        var id = map[sourceId].Value;
                        // 사용자 도면의 같은 이름 레이어가 잠겨 있어도 이번에 만든 객체는 옮길 수 있게 엽니다(레이어 잠금은 그대로).
                        var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite, false, true);
                        entity.TransformBy(transform);
                        // 분해하면 선종류 무늬가 함께 커지지 않으므로 축척·도면 설정 차이만큼 선종류 축척을 곱해 템플릿과 같은 모양을 유지합니다.
                        // (블록 참조 안쪽은 도형과 함께 커지므로 건드리지 않습니다.)
                        if (!(entity is BlockReference) && Math.Abs(linetypeFactor - 1) > 1e-9) entity.LinetypeScale *= linetypeFactor;
                        ids.Add(id); all.Add(id);
                        if (!(entity is Wipeout)) content.Add(id);
                    }
                    placements[j].EntityIds = ids;
                    Fault("explode-transform:" + j);
                }
                Fault("explode-draworder");
                // 가림(WIPEOUT)은 범례 내용 아래로, 범례 전체는 기존 도면 위로 둡니다.
                if (content.Count > 0)
                {
                    var space = (BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForRead);
                    var order = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
                    order.MoveToTop(new ObjectIdCollection(content.ToArray()));
                }
                result.EntityIds = all;
                tr.Commit();
            }
        }

        private static LegendResult Import(Database target, TemplateManifest m, string sourcePath, IList<LegendRow> rows, IList<LegendSelection> selection, Point3d insertion, Matrix3d ucs, bool original)
        {
            if (target == null) throw new ArgumentNullException("target");
            var axes = CheckUcs(ucs);
            // 모든 도형 작업을 임시 데이터베이스에서 끝냅니다. WblockCloneObjects로 생긴 레이어 등은 일반 트랜잭션 취소로 되돌아가지 않기 때문입니다.
            using (var sources = new SourceCache())
            using (var staging = new Database(true, true))
            {
                var prepared = Build(staging, m, sources.Open(sourcePath, m), rows, selection, original);
                IdMapping finalMapping = null;
                try
                {
                    using (var tr = target.TransactionManager.StartTransaction())
                    {
                        var owner = (BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForWrite);
                        finalMapping = new IdMapping();
                        staging.WblockCloneObjects(new ObjectIdCollection(new[] { prepared.BlockDefinitionId }), target.BlockTableId, finalMapping, TargetCloning, false);
                        ObjectId finalBlock = finalMapping[prepared.BlockDefinitionId].Value;
                        var reference = new BlockReference(Point3d.Origin, finalBlock);
                        reference.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, insertion, axes.Xaxis, axes.Yaxis, axes.Zaxis));
                        var referenceId = owner.AppendEntity(reference); tr.AddNewlyCreatedDBObject(reference, true);
                        var retained = new Dictionary<string, ObjectId>();
                        foreach (var pair in prepared.OriginalEntityCopies)
                            if (!pair.Value.IsErased) retained.Add(pair.Key, finalMapping[pair.Value].Value);
                        var symbols = new Dictionary<string, IList<ObjectId>>();
                        if (prepared.RowSymbolCopies != null) foreach (var row in prepared.RowSymbolCopies) symbols.Add(row.Key, row.Value.Select(id => finalMapping[id].Value).ToList());
                        tr.Commit();
                        return new LegendResult { BlockDefinitionId = finalBlock, BlockReferenceId = referenceId, ItemCount = prepared.ItemCount,
                            Width = prepared.Width, Height = prepared.Height, ScaleFactor = 1, OriginalEntityCopies = retained, RowSymbolCopies = symbols };
                    }
                }
                catch (Exception failure)
                {
                    RollBackKeepingError(target, finalMapping == null ? new IdMapping[0] : new[] { finalMapping }, failure);
                    throw;
                }
            }
        }

        /// <summary>원본 객체를 복사해 공종 한 개의 표를 블록 정의로 완성합니다.</summary>
        private static LegendResult Build(Database staging, TemplateManifest m, SourceDocument source, IList<LegendRow> rows, IList<LegendSelection> selection, bool original)
        {
            var sourceIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            var ids = new ObjectIdCollection();
            foreach (string handle in m.AllHandles)
            {
                var id = source.Resolve(handle);
                ids.Add(id); sourceIds.Add(handle, id);
            }
            var labelChanges = selection == null ? null : selection.ToDictionary(s => s.ItemId);
            using (var transaction = staging.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(staging.BlockTableId, OpenMode.ForWrite);
                var block = new BlockTableRecord { Name = "STORM_LEGEND_" + Guid.NewGuid().ToString("N"), Origin = Point3d.Origin };
                var blockId = table.Add(block); transaction.AddNewlyCreatedDBObject(block, true);
                var mapping = new IdMapping();
                // 임시 도면은 비어 있으므로 템플릿 정의(레이어·선종류·문자 스타일 이름과 설정)를 그대로 가져옵니다. ("$0$" 이름 바꿈 없음)
                source.Database.WblockCloneObjects(ids, blockId, mapping, DuplicateRecordCloning.Replace, false);
                var copied = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in sourceIds) copied.Add(pair.Key, mapping[pair.Value].Value);
                double bottom = original ? m.SourceBottomY : m.RowsTopY - rows.Sum(r => r.Height);
                IDictionary<string, IList<ObjectId>> symbols = null;
                if (!original) symbols = Rearrange(staging, transaction, block, m, copied, rows, labelChanges, bottom);
                // 위로 올린 원본 좌표를 블록 기준점(표 왼쪽 위)으로 옮깁니다.
                var local = Matrix3d.Displacement(new Vector3d(-m.Origin.X, -m.Origin.Y, -m.Origin.Z));
                foreach (ObjectId id in block)
                {
                    if (id.IsErased) continue;
                    ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).TransformBy(local);
                }
                var drawOrder = (DrawOrderTable)transaction.GetObject(block.DrawOrderTableId, OpenMode.ForWrite);
                drawOrder.MoveToBottom(new ObjectIdCollection(new[] { copied[m.WipeoutHandle] }));
                transaction.Commit();
                return new LegendResult { BlockDefinitionId = blockId, ItemCount = rows.Count(r => !r.IsBlank), ScaleFactor = 1.0, Width = m.FrameWidth,
                    Height = m.Origin.Y - bottom + m.BottomMargin, OriginalEntityCopies = copied, RowSymbolCopies = symbols };
            }
        }

        private static IDictionary<string, IList<ObjectId>> Rearrange(Database target, Transaction tr, BlockTableRecord block, TemplateManifest m,
            IDictionary<string, ObjectId> copied, IList<LegendRow> rows, IDictionary<string, LegendSelection> labelChanges, double bottom)
        {
            var keep = new HashSet<ObjectId>(m.HeaderHandles.Select(h => copied[h]));
            var rowSymbols = new Dictionary<string, IList<ObjectId>>();
            double top = m.RowsTopY;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; double center = top - row.Height / 2;
                var symbols = new List<ObjectId>(); rowSymbols.Add(row.Id, symbols);
                if (!row.IsBlank)
                {
                    var change = labelChanges[row.Id];
                    foreach (var cell in row.Cells)
                    {
                        string label = cell.Column == 1 ? change.NameOverride : cell.Column == 2 ? change.SpecOverride : null;
                        if (!string.IsNullOrEmpty(label)) AddLabel(target, tr, block, row, cell, copied, label, center);
                        foreach (string handle in cell.Handles)
                        {
                            var sourceEntity = (Entity)tr.GetObject(copied[handle], OpenMode.ForRead);
                            if (label != null && (sourceEntity is DBText || sourceEntity is MText)) continue;
                            var entity = (Entity)sourceEntity.Clone();
                            entity.TransformBy(Matrix3d.Displacement(new Vector3d(0, center - row.SourceCenterOf(cell), 0)));
                            Add(tr, block, entity); if (cell.Column == 0) symbols.Add(entity.ObjectId);
                        }
                    }
                    if (m.HasQuantityColumn)
                    {
                        var quantity = (DBText)((DBText)tr.GetObject(copied[row.QuantityHandle], OpenMode.ForRead)).Clone();
                        quantity.TextString = LegendSelection.NormalizeQuantity(change.Quantity);
                        double x = quantity.AlignmentPoint.X;
                        quantity.TransformBy(Matrix3d.Displacement(new Vector3d(m.QuantityCenterX - x, center - row.QuantitySourceCenterY, 0)));
                        Add(tr, block, quantity); quantity.AdjustAlignment(target);
                        FitWidth(target, quantity, m.QuantityCellWidth);
                    }
                }
                top -= row.Height;
                if (i < rows.Count - 1 && !string.IsNullOrEmpty(m.FullSeparatorHandle))
                    AddTranslated(tr, block, (Entity)tr.GetObject(copied[m.FullSeparatorHandle], OpenMode.ForRead), top - m.FullSeparatorY);
            }
            // 표 높이가 원본과 다르면 세로선·외곽선·가림 영역의 아래 끝을 새 표 높이에 맞춥니다.
            bool resized = Math.Abs(bottom - m.SourceBottomY) > .01;
            foreach (string handle in m.VerticalBorderHandles.Concat(m.AuxBorderHandles).Concat(new[] { m.OuterBorderHandle }))
            {
                var border = (Entity)tr.GetObject(copied[handle], OpenMode.ForWrite);
                if (resized) MoveBottom(border, handle == m.OuterBorderHandle ? bottom - m.BottomMargin : bottom, handle);
                keep.Add(border.ObjectId);
            }
            var wipeout = (Wipeout)tr.GetObject(copied[m.WipeoutHandle], OpenMode.ForWrite);
            if (resized)
                wipeout.SetFrom(new Point2dCollection(new[] { new Point2d(m.TableLeftX, bottom), new Point2d(m.TableRightX, bottom), new Point2d(m.TableRightX, m.TitleTopY), new Point2d(m.TableLeftX, m.TitleTopY), new Point2d(m.TableLeftX, bottom) }), Vector3d.ZAxis);
            keep.Add(wipeout.ObjectId);
            foreach (var id in copied.Values) if (!keep.Contains(id)) tr.GetObject(id, OpenMode.ForWrite).Erase();
            return rowSymbols;
        }

        /// <summary>선의 가장 낮은 끝점(들)을 새 아래 위치로 옮깁니다. 표를 아래로 늘려 그린 템플릿도 맞춰집니다.</summary>
        private static void MoveBottom(Entity border, double newBottom, string handle)
        {
            var polyline = border as Polyline;
            if (polyline != null)
            {
                double low = Enumerable.Range(0, polyline.NumberOfVertices).Min(i => polyline.GetPoint2dAt(i).Y);
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    var p = polyline.GetPoint2dAt(i);
                    if (Math.Abs(p.Y - low) < .01) polyline.SetPointAt(i, new Point2d(p.X, newBottom));
                }
                return;
            }
            var line = border as Line;
            if (line == null) throw new InvalidDataException("지원하지 않는 원본 테두리 유형: " + handle);
            if (line.StartPoint.Y <= line.EndPoint.Y) line.StartPoint = new Point3d(line.StartPoint.X, newBottom, line.StartPoint.Z);
            else line.EndPoint = new Point3d(line.EndPoint.X, newBottom, line.EndPoint.Z);
        }

        private static void AddLabel(Database target, Transaction tr, BlockTableRecord block, LegendRow row, LegendSourceCell cell, IDictionary<string, ObjectId> copied, string label, double center)
        {
            // 원본 문자 칸의 글꼴·높이·색·레이어를 그대로 쓰고, 한 줄 가운데 정렬로 넣습니다.
            var prototype = cell.Handles.Select(h => tr.GetObject(copied[h], OpenMode.ForRead)).OfType<DBText>().FirstOrDefault()
                ?? row.Cells.SelectMany(c => c.Handles).Select(h => tr.GetObject(copied[h], OpenMode.ForRead)).OfType<DBText>().FirstOrDefault();
            DBText text;
            if (prototype != null) text = (DBText)prototype.Clone();
            else
            {
                var mtext = row.Cells.SelectMany(c => c.Handles).Select(h => tr.GetObject(copied[h], OpenMode.ForRead)).OfType<MText>().FirstOrDefault();
                text = new DBText { Height = mtext != null ? mtext.TextHeight : 150 };
                if (mtext != null) { text.SetPropertiesFrom(mtext); text.TextStyleId = mtext.TextStyleId; }
            }
            text.HorizontalMode = TextHorizontalMode.TextCenter; text.VerticalMode = TextVerticalMode.TextVerticalMid;
            text.TextString = label; text.Position = new Point3d(cell.CenterX, center, 0); text.AlignmentPoint = new Point3d(cell.CenterX, center, 0);
            Add(tr, block, text); text.AdjustAlignment(target);
            FitWidth(target, text, cell.Width);
        }

        private static void FitWidth(Database target, DBText text, double maxWidth)
        {
            if (maxWidth <= 0) return;
            var extents = text.GeometricExtents;
            double width = extents.MaxPoint.X - extents.MinPoint.X;
            if (width > maxWidth) { text.WidthFactor *= maxWidth / width; text.AdjustAlignment(target); }
        }

        private static void Add(Transaction tr, BlockTableRecord block, Entity entity)
        {
            block.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true);
        }

        private static void AddTranslated(Transaction tr, BlockTableRecord block, Entity template, double dy)
        {
            var copy = (Entity)template.Clone();
            copy.TransformBy(Matrix3d.Displacement(new Vector3d(0, dy, 0)));
            Add(tr, block, copy);
        }

        /// <summary>원본 템플릿 파일(DWG/DXF)을 한 번만 열어 여러 공종이 함께 씁니다.</summary>
        private sealed class SourceCache : IDisposable
        {
            private readonly Dictionary<string, SourceDocument> open = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase);
            public SourceDocument Open(string path, TemplateManifest manifest)
            {
                if (string.IsNullOrEmpty(path)) throw new ArgumentException("원본 템플릿 경로가 비어 있습니다.");
                path = Path.GetFullPath(path);
                manifest.Validate();
                SourceDocument document;
                if (!open.TryGetValue(path, out document))
                {
                    document = new SourceDocument(path);
                    open.Add(path, document);
                }
                if (!string.Equals(document.Sha256, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("원본 템플릿 파일이 변경되었습니다. LEGEND를 다시 실행하면 새 템플릿을 읽습니다.");
                return document;
            }
            public void Dispose() { foreach (var document in open.Values) document.Dispose(); open.Clear(); }
        }

        private sealed class SourceDocument : IDisposable
        {
            private readonly string path;
            private Database database;
            public string Sha256 { get; private set; }
            public SourceDocument(string path)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("원본 템플릿 파일을 찾을 수 없습니다.", path);
                this.path = path;
                Sha256 = TemplateManifest.ComputeSha256(path);
            }
            // 해시를 먼저 확인한 뒤에만 파일을 엽니다(변경된 파일은 읽지 않음).
            public Database Database { get { return database ?? (database = OpenSource(path)); } }
            public ObjectId Resolve(string handle)
            {
                var database = Database;
                long value;
                if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new InvalidDataException("잘못된 원본 핸들: " + handle);
                ObjectId id;
                try { id = database.GetObjectId(false, new Handle(value), 0); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { id = ObjectId.Null; }
                if (id.IsNull || id.IsErased) throw new InvalidDataException("원본 객체를 찾을 수 없습니다: " + handle);
                return id;
            }
            public void Dispose() { if (database != null) { database.Dispose(); database = null; } }
        }

        /// <summary>도면에서 선종류 무늬 길이에 곱해지는 값: LTSCALE × (모형 공간이고 MSLTSCALE=1이면 1/주석 축척).</summary>
        public static double LinetypeBasis(Database db, bool modelSpace)
        {
            double basis = db.Ltscale > 0 ? db.Ltscale : 1;
            try
            {
                if (modelSpace && db.MsLtScale && db.Cannoscale != null && db.Cannoscale.Scale > 0) basis /= db.Cannoscale.Scale;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return basis;
        }

        private static bool IsModelSpace(Database db)
        {
            try { return db.CurrentSpaceId == SymbolUtilityServices.GetBlockModelSpaceId(db); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return true; }
        }

        /// <summary>DWG/DXF를 화면에 열지 않고 읽기 전용으로 엽니다.</summary>
        public static Database OpenSource(string path)
        {
            var db = new Database(false, true);
            try
            {
                if (string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase)) db.DxfIn(path, null);
                else { db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null); db.CloseInput(true); }
                return db;
            }
            catch { db.Dispose(); throw; }
        }
    }
}
