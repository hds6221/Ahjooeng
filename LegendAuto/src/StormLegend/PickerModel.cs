using System;
using System.Collections.Generic;
using System.Linq;

namespace StormLegend
{
    /// <summary>
    /// 한 공종의 선택 상태. 행 순서(빈 행 포함)를 그대로 보관하며 이 순서대로 생성됩니다.
    /// 새로 고른 항목은 원본 표 순서 위치에 끼워 넣고, 빈 행과 사용자가 옮긴 행은 자리를 지킵니다.
    /// </summary>
    public sealed class PickerModel
    {
        private readonly TemplateManifest manifest;
        private readonly List<PresetItem> entries = new List<PresetItem>();
        private int nextBlank = 1;
        public event EventHandler Changed;

        public PickerModel(TemplateManifest manifest, bool selectAll = true)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            this.manifest = manifest;
            if (selectAll) SelectAll();
        }

        public TemplateManifest Manifest { get { return manifest; } }
        /// <summary>실제 항목 수(빈 행 제외).</summary>
        public int Count { get { return entries.Count(e => !LegendSelection.IsBlankId(e.ItemId)); } }
        public int BlankCount { get { return entries.Count(e => LegendSelection.IsBlankId(e.ItemId)); } }
        /// <summary>생성될 전체 행 수(빈 행 포함).</summary>
        public int RowCount { get { return entries.Count; } }
        public bool Contains(string id) { return IndexOf(id) >= 0; }

        public IList<LegendSelection> Selection
        {
            get { return entries.Select(e => new LegendSelection(e.ItemId, e.Quantity, e.NameOverride, e.SpecOverride)).ToList(); }
        }

        public static string NormalizeQuantity(string quantity) { return LegendSelection.NormalizeQuantity(quantity); }

        public void Toggle(string id)
        {
            if (manifest.FindRow(id) == null) throw new ArgumentException("알 수 없는 항목입니다.");
            int index = IndexOf(id);
            if (index >= 0) entries.RemoveAt(index);
            else entries.Insert(InsertPosition(id), new PresetItem { ItemId = id, Quantity = "-" });
            Notify();
        }

        public void Remove(string id) { int index = IndexOf(id); if (index >= 0) { entries.RemoveAt(index); Notify(); } }

        public void SetQuantity(string id, string quantity)
        {
            var entry = Require(id);
            entry.Quantity = NormalizeQuantity(quantity); Notify();
        }

        public void SetLabels(string id, string name, string spec)
        {
            var entry = Require(id);
            var row = manifest.FindRow(id);
            string n = LegendSelection.NormalizeLabel(name), p = LegendSelection.NormalizeLabel(spec);
            entry.NameOverride = n == row.Name ? null : n; entry.SpecOverride = p == row.Spec ? null : p; Notify();
        }

        public void ResetLabels(string id)
        {
            int index = IndexOf(id);
            if (index < 0 || LegendSelection.IsBlankId(id)) return;
            entries[index].NameOverride = null; entries[index].SpecOverride = null; Notify();
        }

        /// <summary>빈 행을 넣습니다. afterId가 null이면 맨 아래에 넣습니다. 새 빈 행 ID를 돌려줍니다.</summary>
        public string AddBlank(string afterId = null)
        {
            if (!manifest.SupportsBlankRows) throw new InvalidOperationException(manifest.CategoryName + " 표에는 빈 행을 넣을 수 없습니다.");
            string id = LegendSelection.Blank(nextBlank++).ItemId;
            int after = afterId == null ? -1 : IndexOf(afterId);
            entries.Insert(after < 0 ? entries.Count : after + 1, new PresetItem { ItemId = id, Quantity = "" });
            Notify();
            return id;
        }

        public bool CanMove(string id, int direction) { int index = IndexOf(id); int target = index + direction; return index >= 0 && target >= 0 && target < entries.Count; }

        public void Move(string id, int direction)
        {
            if (!CanMove(id, direction)) return;
            int index = IndexOf(id); var entry = entries[index];
            entries.RemoveAt(index); entries.Insert(index + direction, entry); Notify();
        }

        public void Clear() { entries.Clear(); Notify(); }

        public void SelectAll() { Apply(manifest.Rows.Select(r => new PresetItem { ItemId = r.Id, Quantity = "-" })); }

        /// <summary>목록 전체를 바꿉니다. 잘못된 항목이 하나라도 있으면 아무것도 바꾸지 않습니다.</summary>
        public void Apply(IEnumerable<PresetItem> items)
        {
            if (items == null) throw new ArgumentNullException("items");
            var next = new List<PresetItem>(); var seen = new HashSet<string>(StringComparer.Ordinal); int blank = 1;
            foreach (var item in items)
            {
                if (item == null || item.ItemId == null || !seen.Add(item.ItemId)) throw new ArgumentException("프리셋 항목이 잘못되었습니다.");
                if (LegendSelection.IsBlankId(item.ItemId))
                {
                    if (!manifest.SupportsBlankRows) throw new ArgumentException("이 공종에는 빈 행을 넣을 수 없습니다.");
                    next.Add(new PresetItem { ItemId = LegendSelection.Blank(blank++).ItemId, Quantity = "" });
                    continue;
                }
                if (manifest.FindRow(item.ItemId) == null) throw new ArgumentException("프리셋 항목이 잘못되었습니다.");
                next.Add(new PresetItem { ItemId = item.ItemId, Quantity = NormalizeQuantity(item.Quantity), NameOverride = LegendSelection.NormalizeLabel(item.NameOverride), SpecOverride = LegendSelection.NormalizeLabel(item.SpecOverride) });
            }
            entries.Clear(); entries.AddRange(next); nextBlank = blank; Notify();
        }

        private int IndexOf(string id) { return entries.FindIndex(e => e.ItemId == id); }

        private PresetItem Require(string id)
        {
            int index = IndexOf(id);
            if (index < 0 || LegendSelection.IsBlankId(id)) throw new ArgumentException("선택되지 않은 항목입니다.");
            return entries[index];
        }

        private int InsertPosition(string id)
        {
            int order = manifest.IndexOf(id), lastReal = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (LegendSelection.IsBlankId(entries[i].ItemId)) continue;
                if (manifest.IndexOf(entries[i].ItemId) > order) return i;
                lastReal = i;
            }
            // 뒤에 올 항목이 없으면 마지막 실제 항목 바로 뒤(맨 아래 빈 행보다는 위)에 둡니다.
            return lastReal + 1;
        }

        private void Notify() { var handler = Changed; if (handler != null) handler(this, EventArgs.Empty); }
    }
}
