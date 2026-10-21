using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;

namespace StormLegend
{
    /// <summary>
    /// 사용자 도면으로 복사할 때의 정의(레이어·문자 스타일·선종류·블록) 처리와 실패 복구.
    /// · 이름이 같아도 내용이 다르면 알려주고, 설정에 따라 기존 정의를 쓰거나 범례 전용 이름으로 따로 넣습니다(기존 정의는 절대 덮어쓰지 않음).
    /// · 꺼짐·동결·잠금·출력 안 함 레이어를 알려줍니다.
    /// · 복사 도중 실패하면 이번에 들어간 객체와 정의만 지워 실패 전 상태로 되돌립니다.
    /// </summary>
    internal static class LegendDefinitions
    {
        public const string SeparateSuffix = "_범례";

        private sealed class Usage
        {
            public readonly HashSet<ObjectId> Layers = new HashSet<ObjectId>(), Linetypes = new HashSet<ObjectId>(), Styles = new HashSet<ObjectId>(), Blocks = new HashSet<ObjectId>();
        }

        /// <summary>범례 블록 정의들이 실제로 쓰는 정의를 모읍니다(중첩 블록 포함).</summary>
        private static Usage Collect(Transaction tr, IEnumerable<ObjectId> roots)
        {
            var usage = new Usage(); var pending = new Stack<ObjectId>(roots); var visited = new HashSet<ObjectId>();
            while (pending.Count > 0)
            {
                var blockId = pending.Pop();
                if (!visited.Add(blockId)) continue;
                var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                foreach (ObjectId id in block)
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
                    if (entity == null) continue;
                    usage.Layers.Add(entity.LayerId); usage.Linetypes.Add(entity.LinetypeId);
                    var text = entity as DBText; if (text != null) usage.Styles.Add(text.TextStyleId);
                    var mtext = entity as MText; if (mtext != null) usage.Styles.Add(mtext.TextStyleId);
                    var reference = entity as BlockReference;
                    if (reference != null) { usage.Blocks.Add(reference.BlockTableRecord); pending.Push(reference.BlockTableRecord); }
                }
            }
            foreach (var layerId in usage.Layers.ToList()) { var layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord; if (layer != null) usage.Linetypes.Add(layer.LinetypeObjectId); }
            foreach (var linetypeId in usage.Linetypes.ToList())
            {
                var linetype = tr.GetObject(linetypeId, OpenMode.ForRead) as LinetypeTableRecord; if (linetype == null) continue;
                for (int i = 0; i < linetype.NumDashes; i++) if (!linetype.ShapeStyleAt(i).IsNull) usage.Styles.Add(linetype.ShapeStyleAt(i));
            }
            usage.Layers.RemoveWhere(id => id.IsNull); usage.Linetypes.RemoveWhere(id => id.IsNull); usage.Styles.RemoveWhere(id => id.IsNull);
            return usage;
        }

        /// <summary>
        /// 복사 전에 같은 이름·다른 내용인 정의를 찾습니다. Separate면 임시 도면 쪽 정의 이름을 "이름_범례"로 바꿔 따로 들어가게 합니다.
        /// 사용자 도면은 읽기만 합니다.
        /// </summary>
        public static void Reconcile(Database staging, Database target, IEnumerable<ObjectId> roots, DefinitionConflict policy, IList<string> warnings)
        {
            using (var st = staging.TransactionManager.StartTransaction())
            using (var tt = target.TransactionManager.StartTransaction())
            {
                var usage = Collect(st, roots);
                // 참조하는 정의부터 처리합니다. 비교는 이름 대신 참조 내용까지 포함합니다.
                ReconcileTable("문자 스타일", usage.Styles, staging.TextStyleTableId, target.TextStyleTableId, st, tt, policy, warnings);
                ReconcileTable("선종류", usage.Linetypes, staging.LinetypeTableId, target.LinetypeTableId, st, tt, policy, warnings);
                ReconcileTable("레이어", usage.Layers, staging.LayerTableId, target.LayerTableId, st, tt, policy, warnings);
                ReconcileTable("블록", usage.Blocks, staging.BlockTableId, target.BlockTableId, st, tt, policy, warnings);
                tt.Commit(); st.Commit();
            }
        }

        private static void ReconcileTable(string kind, IEnumerable<ObjectId> ids, ObjectId sourceTableId, ObjectId targetTableId,
            Transaction st, Transaction tt, DefinitionConflict policy, IList<string> warnings)
        {
            var sourceTable = (SymbolTable)st.GetObject(sourceTableId, OpenMode.ForRead);
            var targetTable = (SymbolTable)tt.GetObject(targetTableId, OpenMode.ForRead);
            foreach (var id in ids)
            {
                var mine = (SymbolTableRecord)st.GetObject(id, OpenMode.ForRead);
                string name = mine.Name;
                var block = mine as BlockTableRecord;
                if (string.IsNullOrEmpty(name) || name == "Defpoints" || (mine is LinetypeTableRecord && IsBuiltIn(name))
                    || (block != null && (block.IsLayout || block.IsAnonymous || name.StartsWith("STORM_LEGEND_") || name.StartsWith("LEGEND_BATCH_"))) || !targetTable.Has(name)) continue;
                if (Equivalent(st, mine, tt, tt.GetObject(targetTable[name], OpenMode.ForRead))) continue;
                if (policy == DefinitionConflict.Separate && !(mine is LayerTableRecord && name == "0"))
                {
                    string next = name + SeparateSuffix; int n = 2;
                    while (sourceTable.Has(next) || (targetTable.Has(next) && !Equivalent(st, mine, tt, tt.GetObject(targetTable[next], OpenMode.ForRead))))
                        next = name + SeparateSuffix + n++;
                    bool reused = targetTable.Has(next);
                    mine.UpgradeOpen(); mine.Name = next;
                    warnings.Add(kind + " '" + name + "': 도면 정의와 내용이 달라(색·형상·글꼴·선종류 등) '" + next + "'" + (reused ? "을 재사용합니다." : "(으)로 따로 넣습니다."));
                }
                else warnings.Add(kind + " '" + name + "'이(가) 도면에 이미 있고 내용이 다릅니다(색·형상·글꼴·선종류 또는 참조 정의). 도면 정의를 그대로 써서 템플릿과 다르게 보일 수 있습니다. 설정에서 범례 전용 이름을 선택할 수 있습니다.");
            }
        }

        private static bool IsBuiltIn(string name)
        {
            return new[] { "ByLayer", "ByBlock", "Continuous" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        internal static bool Equivalent(Transaction a, DBObject left, Transaction b, DBObject right)
        {
            try
            {
                string first = Signature(a, left, new HashSet<ObjectId>()), second = Signature(b, right, new HashSet<ObjectId>());
                return first != null && second != null && first == second;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static string Pack(params object[] parts)
        {
            return string.Concat(parts.Select(p =>
            {
                string value;
                if (p is Point3d) { var v = (Point3d)p; value = Pack(v.X, v.Y, v.Z); }
                else if (p is Vector3d) { var v = (Vector3d)p; value = Pack(v.X, v.Y, v.Z); }
                else if (p is Point2d) { var v = (Point2d)p; value = Pack(v.X, v.Y); }
                else if (p is Vector2d) { var v = (Vector2d)p; value = Pack(v.X, v.Y); }
                else if (p is double) value = Math.Round((double)p, 9).ToString("R", CultureInfo.InvariantCulture);
                else value = p == null ? "" : Convert.ToString(p, CultureInfo.InvariantCulture);
                return value.Length + ":" + value;
            }));
        }
        private static string Ref(Transaction tr, ObjectId id, HashSet<ObjectId> visiting)
        {
            if (id.IsNull) return "null";
            var signature = Signature(tr, tr.GetObject(id, OpenMode.ForRead, false, true), visiting);
            if (signature == null) throw new NotSupportedException("동일성 확인 불가");
            return signature;
        }
        private static string Color(Autodesk.AutoCAD.Colors.Color c) { return Pack(c.ColorMethod, c.ColorIndex, c.Red, c.Green, c.Blue); }

        // Transparency.Alpha는 ByLayer/ByBlock이면 예외(eNotApplicable)를 냅니다. 템플릿 객체 대부분이 ByLayer라 종류부터 구분합니다.
        private static string Transparency(Autodesk.AutoCAD.Colors.Transparency t)
        {
            if (t.IsByLayer) return "bylayer";
            if (t.IsByBlock) return "byblock";
            return t.IsByAlpha ? "alpha" + t.Alpha : "other";
        }

        // TextAt은 문자 구간이 아니면 예외(eNotApplicable)를 냅니다. 문자 구간이 아니면 빈 문자열입니다.
        private static string DashText(LinetypeTableRecord lt, int i)
        {
            if (lt.ShapeStyleAt(i).IsNull || lt.ShapeNumberAt(i) != 0) return "";
            try { return lt.TextAt(i) ?? ""; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return ""; }
        }

        /// <summary>그리기 순서대로의 객체. 그리기 순서표가 없거나(새로 만든 블록) 열 수 없으면 블록 안 순서 그대로.</summary>
        private static IEnumerable<ObjectId> DrawOrder(Transaction tr, BlockTableRecord block)
        {
            ObjectId orderId = ObjectId.Null;
            try { orderId = block.DrawOrderTableId; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            if (!orderId.IsNull && orderId.IsValid && !orderId.IsErased)
                try { return ((DrawOrderTable)tr.GetObject(orderId, OpenMode.ForRead)).GetFullDrawOrder(0).Cast<ObjectId>().ToList(); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return block.Cast<ObjectId>().ToList();
        }

        private static string HatchSignature(Hatch hatch, string common)
        {
            if (hatch.IsGradient) return Pack(common, "gradient", hatch.GradientName, hatch.GradientAngle, hatch.GradientShift, hatch.ShadeTintValue, hatch.GradientOneColorMode, Loops(hatch));
            return Pack(common, hatch.IsSolidFill, hatch.PatternType, hatch.PatternName, hatch.PatternAngle, hatch.PatternScale, hatch.PatternSpace, hatch.PatternDouble,
                hatch.HatchStyle, hatch.Normal, hatch.Elevation, Loops(hatch));
        }

        private static string Loops(Hatch hatch)
        {
            var parts = new List<object> { hatch.NumberOfLoops };
            for (int i = 0; i < hatch.NumberOfLoops; i++)
            {
                var loop = hatch.GetLoopAt(i);
                parts.Add((int)loop.LoopType);
                if (loop.IsPolyline)
                    foreach (BulgeVertex v in loop.Polyline) parts.Add(Pack(v.Vertex, v.Bulge));
                else
                    foreach (Curve2d curve in loop.Curves)
                        parts.Add(Pack(curve.GetType().Name, Pack(curve.GetSamplePoints(5).Cast<object>().ToArray())));
            }
            return Pack(parts.ToArray());
        }

        // 객체 종류/개수/범위만으로 동일 판정을 하지 않습니다. 지원하지 않는 객체는 보수적으로 충돌 처리합니다.
        private static string Signature(Transaction tr, DBObject obj, HashSet<ObjectId> visiting)
        {
            if (!obj.ObjectId.IsNull && !visiting.Add(obj.ObjectId)) throw new NotSupportedException("순환 정의");
            try
            {
                var style = obj as TextStyleTableRecord;
                if (style != null) return Pack("style", (style.FileName ?? "").ToUpperInvariant(), (style.BigFontFileName ?? "").ToUpperInvariant(),
                    style.Font.TypeFace, style.Font.Bold, style.Font.Italic, style.Font.CharacterSet, style.Font.PitchAndFamily,
                    style.TextSize, style.XScale, style.ObliquingAngle, style.IsVertical, style.IsShapeFile);
                var lt = obj as LinetypeTableRecord;
                if (lt != null)
                {
                    var parts = new List<object> { "linetype", lt.PatternLength, lt.NumDashes };
                    for (int i = 0; i < lt.NumDashes; i++) parts.Add(Pack(lt.DashLengthAt(i), lt.ShapeNumberAt(i), DashText(lt, i),
                        lt.ShapeScaleAt(i), lt.ShapeRotationAt(i), lt.ShapeOffsetAt(i), lt.ShapeIsUcsOrientedAt(i), Ref(tr, lt.ShapeStyleAt(i), visiting)));
                    return Pack(parts.ToArray());
                }
                var layer = obj as LayerTableRecord;
                if (layer != null) return Pack("layer", Color(layer.Color), layer.LineWeight, Transparency(layer.Transparency), layer.IsOff, layer.IsFrozen,
                    layer.IsLocked, layer.IsPlottable, Ref(tr, layer.LinetypeObjectId, visiting));
                var block = obj as BlockTableRecord;
                if (block != null)
                {
                    if (block.IsDynamicBlock || block.IsFromExternalReference) throw new NotSupportedException("동적/외부 블록");
                    var parts = new List<object> { "block", block.Origin, block.Units, block.Explodable };
                    foreach (ObjectId id in DrawOrder(tr, block))
                    {
                        if (id.IsErased) continue;
                        string content = Ref(tr, id, visiting); if (content == null) return null; parts.Add(content);
                    }
                    return Pack(parts.ToArray());
                }
                var e = obj as Entity; if (e == null) return null;
                string common = Pack(e.GetRXClass().DxfName, Color(e.Color), e.LineWeight, e.LinetypeScale, Transparency(e.Transparency), e.Visible,
                    Ref(tr, e.LayerId, visiting), Ref(tr, e.LinetypeId, visiting));
                var line = e as Line; if (line != null) return Pack(common, line.StartPoint, line.EndPoint, line.Normal, line.Thickness);
                var circle = e as Circle; if (circle != null) return Pack(common, circle.Center, circle.Radius, circle.Normal, circle.Thickness);
                var arc = e as Arc; if (arc != null) return Pack(common, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle, arc.Normal, arc.Thickness);
                var ellipse = e as Ellipse; if (ellipse != null) return Pack(common, ellipse.Center, ellipse.MajorAxis, ellipse.Normal, ellipse.RadiusRatio, ellipse.StartParam, ellipse.EndParam);
                var poly = e as Polyline;
                if (poly != null)
                {
                    var parts = new List<object> { common, poly.Closed, poly.Elevation, poly.Normal, poly.Thickness };
                    for (int i = 0; i < poly.NumberOfVertices; i++) parts.Add(Pack(poly.GetPoint2dAt(i), poly.GetBulgeAt(i), poly.GetStartWidthAt(i), poly.GetEndWidthAt(i)));
                    return Pack(parts.ToArray());
                }
                var text = e as DBText;
                if (text != null)
                {
                    if (e is AttributeDefinition || e is AttributeReference) throw new NotSupportedException("속성 문자");
                    return Pack(common, text.TextString, text.Position, text.AlignmentPoint, text.Height, text.WidthFactor, text.Rotation,
                        text.Oblique, text.Normal, text.Thickness, text.HorizontalMode, text.VerticalMode, text.IsMirroredInX, text.IsMirroredInY, Ref(tr, text.TextStyleId, visiting));
                }
                var mtext = e as MText;
                if (mtext != null) return Pack(common, mtext.Contents, mtext.Location, mtext.Direction, mtext.Normal, mtext.TextHeight, mtext.Width,
                    mtext.Attachment, mtext.LineSpacingFactor, mtext.LineSpacingStyle, mtext.BackgroundFill, Ref(tr, mtext.TextStyleId, visiting));
                var reference = e as BlockReference;
                if (reference != null)
                {
                    if (reference.AttributeCollection.Count > 0 || reference.IsDynamicBlock) throw new NotSupportedException("속성/동적 참조");
                    string nested = Ref(tr, reference.BlockTableRecord, visiting); if (nested == null) return null;
                    return Pack(common, Pack(reference.BlockTransform.ToArray().Cast<object>().ToArray()), nested);
                }
                var hatch = e as Hatch; if (hatch != null) return HatchSignature(hatch, common);
                var solid = e as Solid;
                if (solid != null) return Pack(common, solid.GetPointAt(0), solid.GetPointAt(1), solid.GetPointAt(2), solid.GetPointAt(3), solid.Normal, solid.Thickness);
                var point = e as DBPoint; if (point != null) return Pack(common, point.Position, point.Normal, point.Thickness);
                // 프록시·외부 참조 등 완전한 동일성을 확인할 수 없는 형식은 기존 도면 정의로 대체하지 않습니다.
                return null;
            }
            finally { if (!obj.ObjectId.IsNull) visiting.Remove(obj.ObjectId); }
        }

        /// <summary>범례가 쓰는 레이어 중 꺼짐·동결·잠금·출력 안 함 상태를 알려줍니다(상태는 바꾸지 않음).</summary>
        public static void ReportLayerStates(Database target, IEnumerable<string> layerNames, IList<string> warnings)
        {
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(target.LayerTableId, OpenMode.ForRead);
                var lines = new List<string>();
                foreach (var name in layerNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal))
                {
                    if (!table.Has(name)) continue;
                    var layer = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                    var states = new List<string>();
                    if (layer.IsOff) states.Add("꺼짐");
                    if (layer.IsFrozen) states.Add("동결");
                    if (layer.IsLocked) states.Add("잠김");
                    if (!layer.IsPlottable && name != "Defpoints") states.Add("출력 안 함");
                    if (states.Count > 0) lines.Add("레이어 '" + name + "': " + string.Join(" · ", states) + (layer.IsOff || layer.IsFrozen ? " — 이 레이어의 범례 부분이 보이지 않습니다." : layer.IsLocked ? " — 범례는 그렸지만 이 레이어 객체는 고칠 수 없습니다." : " — 출력할 때 빠집니다."));
                }
                foreach (var line in lines.Take(12)) warnings.Add(line);
                if (lines.Count > 12) warnings.Add("그 밖에 상태가 다른 레이어 " + (lines.Count - 12) + "개");
                tr.Commit();
            }
        }

        public static IEnumerable<string> LayersOf(Database db, IEnumerable<ObjectId> roots)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var usage = Collect(tr, roots);
                var names = usage.Layers.Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).ToList();
                tr.Commit();
                return names;
            }
        }

        /// <summary>
        /// 복사 도중 실패했을 때: 이번 복사로 생긴 객체(도면 공간에 들어간 것)를 지우고, 남은 새 정의(레이어·블록 등)를 참조가 없어질 때까지 정리합니다.
        /// 기존 객체·정의는 IdMapping에서 "새로 복제됨"이 아닌 것이라 건드리지 않습니다.
        /// </summary>
        public static void RollBack(Database target, IEnumerable<IdMapping> imports)
        {
            var cloned = new List<ObjectId>();
            foreach (var mapping in imports)
                foreach (IdPair pair in mapping)
                    if (pair.IsCloned && pair.Value.IsValid && !pair.Value.IsErased) cloned.Add(pair.Value);
            if (cloned.Count == 0) return;
            var errors = new List<string>();
            // 1) 도면 공간(모형·배치)에 들어간 객체. 잠긴 레이어의 객체도 지울 수 있게 강제로 엽니다.
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var layouts = new HashSet<ObjectId>();
                foreach (ObjectId id in (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForRead))
                    if (((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).IsLayout) layouts.Add(id);
                foreach (var id in cloned)
                {
                    if (id.IsErased) continue;
                    try
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
                        if (entity == null || !layouts.Contains(entity.OwnerId)) continue;
                        // 잠긴 레이어: 읽기로 연 뒤 UpgradeOpen하면 eOnLockedLayer가 납니다. 처음부터 쓰기로 강제로 엽니다.
                        ((Entity)tr.GetObject(id, OpenMode.ForWrite, false, true)).Erase();
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { errors.Add(id.Handle + ": " + ex.ErrorStatus); }
                }
                tr.Commit();
            }
            // 2) 남은 새 정의(레이어·선종류·문자 스타일·블록 등)는 이번 복사 전에는 도면에 없던 것이라 기존 객체가 참조할 수 없습니다.
            //    블록 정의를 먼저 지우면 그 안의 객체도 함께 지워집니다. Defpoints 레이어는 AutoCAD가 지우지 못하게 합니다.
            using (var tr = target.TransactionManager.StartTransaction())
            {
                var remaining = new List<DBObject>();
                foreach (var id in cloned.Where(x => x.IsValid && !x.IsErased))
                    try { remaining.Add(tr.GetObject(id, OpenMode.ForRead, false, true)); }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { errors.Add(id.Handle + ": " + ex.ErrorStatus); }
                foreach (var obj in remaining.OfType<BlockTableRecord>().Cast<DBObject>().Concat(remaining.Where(o => !(o is BlockTableRecord))))
                {
                    if (obj.IsErased || IsDefpoints(obj)) continue;
                    try
                    {
                        var block = obj as BlockTableRecord;
                        if (block != null)
                            foreach (ObjectId inner in block.Cast<ObjectId>().ToList())
                                if (!inner.IsErased) ((Entity)tr.GetObject(inner, OpenMode.ForWrite, false, true)).Erase();
                        tr.GetObject(obj.ObjectId, OpenMode.ForWrite, false, true).Erase();
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { errors.Add(obj.ObjectId.Handle + ": " + ex.ErrorStatus); }
                }
                tr.Commit();
            }
            // 3) 실제로 남은 것을 다시 셉니다(오류가 없어도 확인).
            int left = cloned.Count(id => id.IsValid && !id.IsErased && !IsDefpoints(id));
            if (errors.Count > 0 || left > 0)
            {
                var failure = new InvalidOperationException("되돌리지 못한 객체·정의 " + left + "개" + (errors.Count > 0 ? " (오류 " + errors.Count + "건: " + string.Join(" / ", errors.Take(8)) + ")" : ""));
                failure.Data["remaining"] = left;
                throw failure;
            }
        }

        private static bool IsDefpoints(DBObject obj) { var layer = obj as LayerTableRecord; return layer != null && string.Equals(layer.Name, "Defpoints", StringComparison.OrdinalIgnoreCase); }

        private static bool IsDefpoints(ObjectId id)
        {
            if (id.ObjectClass == null || !id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(LayerTableRecord)))) return false;
            using (var tr = id.Database.TransactionManager.StartOpenCloseTransaction())
                return IsDefpoints(tr.GetObject(id, OpenMode.ForRead, false, true));
        }
    }
}
