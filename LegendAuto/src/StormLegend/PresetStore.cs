using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace StormLegend
{
    [DataContract] public sealed class PresetItem
    {
        [DataMember(Name = "itemId")] public string ItemId { get; set; }
        [DataMember(Name = "nameOverride")] public string NameOverride { get; set; }
        [DataMember(Name = "specOverride")] public string SpecOverride { get; set; }
        [DataMember(Name = "quantity")] public string Quantity { get; set; }
        // ── 저장 당시 원본 항목의 정체(템플릿이 바뀌어 행 번호가 밀려도 같은 항목을 다시 찾는 근거)
        /// <summary>원본 객체 핸들 묶음(LegendRow.Key). 1순위.</summary>
        [DataMember(Name = "sourceKey", EmitDefaultValue = false)] public string SourceKey { get; set; }
        /// <summary>기호 객체 핸들 묶음(LegendRow.SymbolKey). 2순위.</summary>
        [DataMember(Name = "sourceSymbol", EmitDefaultValue = false)] public string SourceSymbol { get; set; }
        [DataMember(Name = "sourceName", EmitDefaultValue = false)] public string SourceName { get; set; }
        [DataMember(Name = "sourceSpec", EmitDefaultValue = false)] public string SourceSpec { get; set; }
        [DataMember(Name = "sourceUnit", EmitDefaultValue = false)] public string SourceUnit { get; set; }
        [DataMember(Name = "sourceRemark", EmitDefaultValue = false)] public string SourceRemark { get; set; }

        internal static PresetItem From(LegendRow row, string quantity, string nameOverride, string specOverride)
        {
            return new PresetItem { ItemId = row.Id, Quantity = quantity, NameOverride = nameOverride, SpecOverride = specOverride,
                SourceKey = row.Key, SourceSymbol = row.SymbolKey, SourceName = row.Name, SourceSpec = row.Spec, SourceUnit = row.Unit ?? "", SourceRemark = row.Remark ?? "" };
        }

        internal string Describe()
        {
            string text = (SourceName ?? NameOverride ?? ItemId) + (SourceSpec != null ? " " + SourceSpec : "");
            return string.IsNullOrEmpty(SourceRemark) ? text : text + " · " + SourceRemark;
        }
    }

    [DataContract] public sealed class LegendPreset
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "items")] public List<PresetItem> Items { get; set; }
        [DataMember(Name = "sourceSha256", EmitDefaultValue = false)] public string SourceSha256 { get; set; }
        // 사용자가 다시 저장해도 자동 연결에 실패한 원본 항목은 복구용으로 남깁니다.
        [DataMember(Name = "unresolvedItems", EmitDefaultValue = false)] public List<PresetItem> UnresolvedItems { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>프리셋을 현재 템플릿에 다시 연결하지 못한 항목 하나(무엇이, 왜).</summary>
    public sealed class PresetIssue
    {
        public string PresetName { get; internal set; }
        public string Item { get; internal set; }
        public string Reason { get; internal set; }
        public override string ToString() { return "'" + PresetName + "' 프리셋의 '" + Item + "': " + Reason; }
    }

    [DataContract] public sealed class PresetDocument
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [DataMember(Name = "categoryId")] public string CategoryId { get; set; }
        [DataMember(Name = "sourceSha256")] public string SourceSha256 { get; set; }
        [DataMember(Name = "defaultPresetId")] public string DefaultPresetId { get; set; }
        [DataMember(Name = "startEmpty", EmitDefaultValue = false)] public bool StartEmpty { get; set; }
        [DataMember(Name = "presets")] public List<LegendPreset> Presets { get; set; } = new List<LegendPreset>();
        /// <summary>현재 템플릿에 다시 연결하지 못해 뺀 항목(저장하지 않음).</summary>
        public IList<PresetIssue> Issues { get; internal set; } = new List<PresetIssue>();
        /// <summary>다시 저장할 때 연결하지 못해 복구용으로 보관 중인 원본 항목 수(경고하지 않음, 파일에는 그대로 남김).</summary>
        public int ArchivedItems { get; internal set; }
        /// <summary>이번에 모든 항목을 확실히 다시 연결한 프리셋 ID(이 프리셋만 파일에 반영).</summary>
        internal HashSet<string> CleanPresetIds { get; set; } = new HashSet<string>();
        /// <summary>행 번호가 바뀌어 다른 ID로 다시 연결한 항목 수.</summary>
        public int RelinkedItems { get; internal set; }
        [OnDeserializing] private void Init(StreamingContext context) { Issues = new List<PresetIssue>(); Presets = new List<LegendPreset>(); CleanPresetIds = new HashSet<string>(); SchemaVersion = 1; }
    }

    /// <summary>
    /// 공종별 "내 기본" 프리셋 저장소(%LOCALAPPDATA%\StormLegend\presets-공종.json).
    /// 템플릿을 고쳐 행 번호가 밀려도 원본 객체 핸들 → 기호 → 명칭·규격·단위·비고 순서로 같은 항목을 다시 찾고,
    /// 후보가 여러 개이거나 확인할 수 없으면 임의로 고르지 않고 '다시 선택 필요'로 알려줍니다.
    /// </summary>
    public sealed class PresetStore
    {
        private readonly string path;
        private readonly TemplateManifest manifest;

        public PresetStore(TemplateManifest manifest, string path = null)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            this.manifest = manifest;
            this.path = path ?? DefaultPath(manifest.CategoryId);
        }

        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormLegend"); } }
        public static string DefaultPath(string categoryId) { return Path.Combine(Folder, "presets-" + (categoryId ?? "legacy") + ".json"); }

        private PresetDocument ReadRaw(out string file)
        {
            file = File.Exists(path) ? path : LegacyPath();
            if (file == null) return new PresetDocument { SourceSha256 = manifest.SourceSha256, CategoryId = manifest.CategoryId };
            PresetDocument data;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                data = (PresetDocument)new DataContractJsonSerializer(typeof(PresetDocument)).ReadObject(stream);
            if (data == null || data.SchemaVersion != 1 || data.CategoryId != manifest.CategoryId || data.Presets == null)
                throw new InvalidDataException("저장된 프리셋이 이 공종과 맞지 않습니다. 원본 파일을 복구한 뒤 저장하세요.");
            return data;
        }

        public PresetDocument Load()
        {
            using (FileGate.Enter(path))
            {
                string file; var data = ReadRaw(out file);
                bool changed = Reconcile(data);
                if (file != null && changed) TrySaveClean(data, file);
                return data;
            }
        }

        private static string CheckName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40 || name.Any(char.IsControl)) throw new ArgumentException("프리셋 이름은 줄바꿈 없이 1~40자로 입력하세요.");
            return name;
        }

        public LegendPreset SaveDefault(string name, IList<LegendSelection> selection)
        {
            name = CheckName(name);
            if (selection == null || selection.Count == 0) throw new ArgumentException("저장할 항목을 선택하세요.");
            var items = selection.Select(s =>
            {
                if (s == null) throw new ArgumentException("선택 항목이 비어 있습니다.");
                if (s.IsBlank) return new PresetItem { ItemId = s.ItemId, Quantity = "" };
                var row = manifest.FindRow(s.ItemId);
                if (row == null) throw new ArgumentException("알 수 없는 항목입니다: " + s.ItemId);
                return PresetItem.From(row, PickerModel.NormalizeQuantity(s.Quantity), s.NameOverride, s.SpecOverride);
            }).ToList();
            new PickerModel(manifest, false).Apply(items);
            using (FileGate.Enter(path))
            {
                string file; var data = ReadRaw(out file); // 화면에서 제외한 항목이 아닌 원본을 읽습니다.
                var matches = data.Presets.Where(p => p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 1) throw new InvalidDataException("같은 이름의 프리셋이 여러 개입니다. 다른 이름으로 저장하세요.");
                var preset = matches.FirstOrDefault();
                if (preset == null) { preset = new LegendPreset { Id = Guid.NewGuid().ToString("N"), Name = name }; data.Presets.Add(preset); }
                if (string.IsNullOrWhiteSpace(preset.Id) || data.Presets.Count(p => p != null && p.Id == preset.Id) > 1)
                    throw new InvalidDataException("프리셋 ID가 잘못되었습니다. 다른 이름으로 저장하세요.");
                var unresolved = new List<PresetItem>(preset.UnresolvedItems ?? new List<PresetItem>());
                foreach (var item in preset.Items ?? new List<PresetItem>())
                {
                    string reason; bool migrated;
                    var resolved = Resolve(item, (preset.SourceSha256 ?? data.SourceSha256) == manifest.SourceSha256, out reason, out migrated);
                    if (resolved == null || !IsValid(resolved)) unresolved.Add(item);
                }
                preset.UnresolvedItems = unresolved.Count == 0 ? null : unresolved;
                preset.Items = items; preset.SourceSha256 = manifest.SourceSha256;
                data.DefaultPresetId = preset.Id; data.StartEmpty = false;
                // 다른 프리셋의 항목·저장 당시 해시는 그대로 유지합니다.
                SaveRaw(data, file);
                return preset;
            }
        }

        public void Rename(string id, string name)
        {
            name = CheckName(name);
            UpdateRaw(data =>
            {
                if (data.Presets.Any(p => p != null && p.Id != id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("이미 같은 이름이 있습니다.");
                RequirePreset(data, id).Name = name;
            });
        }

        public void Delete(string id)
        {
            UpdateRaw(data => { data.Presets.Remove(RequirePreset(data, id)); if (data.DefaultPresetId == id) { data.DefaultPresetId = null; data.StartEmpty = true; } });
        }

        public void ClearDefault() { UpdateRaw(data => { data.DefaultPresetId = null; data.StartEmpty = true; }); }

        private static LegendPreset RequirePreset(PresetDocument data, string id)
        {
            var matches = data.Presets.Where(p => p != null && p.Id == id).ToList();
            if (matches.Count != 1) throw new InvalidDataException("프리셋을 확실히 찾을 수 없습니다. 창을 다시 열어 주세요.");
            return matches[0];
        }

        private void UpdateRaw(Action<PresetDocument> update)
        {
            using (FileGate.Enter(path)) { string file; var data = ReadRaw(out file); update(data); SaveRaw(data, file); }
        }

        private void SaveRaw(PresetDocument data, string file)
        {
            if (file != null) File.Copy(file, file + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Write(data, path);
            if (file != null) PruneBackups(file);
        }

        /// <summary>현재 템플릿 기준으로 항목을 다시 연결합니다. 파일 내용을 바꿔야 하면 true.</summary>
        private bool Reconcile(PresetDocument data)
        {
            bool changed = !string.Equals(data.SourceSha256, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase);
            var valid = new List<LegendPreset>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ids = new HashSet<string>();
            foreach (var preset in data.Presets)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name) || preset.Items == null || !names.Add(preset.Name) || !ids.Add(preset.Id))
                { data.Issues.Add(new PresetIssue { PresetName = preset == null ? "(빈 프리셋)" : preset.Name, Item = "프리셋 전체", Reason = "이름·ID·항목 목록이 잘못되었거나 중복됩니다. 원본 파일은 보존합니다." }); continue; }
                bool presetSame = string.Equals(preset.SourceSha256 ?? data.SourceSha256, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase);
                // 사용자가 다시 저장하며 이미 확인한 항목이라 매번 경고하지 않습니다(파일에는 복구용으로 남음).
                data.ArchivedItems += (preset.UnresolvedItems ?? new List<PresetItem>()).Count;
                var items = new List<PresetItem>(); var used = new HashSet<string>(); bool clean = true;
                foreach (var item in preset.Items)
                {
                    string reason; bool migrated;
                    var resolved = Resolve(item, presetSame, out reason, out migrated);
                    if (resolved != null && !used.Add(resolved.ItemId)) { resolved = null; reason = "같은 프리셋 안에서 다른 항목과 같은 행으로 연결되어 뺐습니다."; }
                    if (resolved != null && !IsValid(resolved)) { resolved = null; reason = "수량이나 고친 문구 형식이 잘못되었습니다."; }
                    if (resolved == null)
                    {
                        data.Issues.Add(new PresetIssue { PresetName = preset.Name, Item = item == null ? "(빈 항목)" : item.Describe(), Reason = reason });
                        clean = false;
                        continue;
                    }
                    if (migrated) changed = true;
                    if (!LegendSelection.IsBlankId(resolved.ItemId) && item.ItemId != resolved.ItemId) data.RelinkedItems++;
                    items.Add(resolved);
                }
                preset.Items = items; preset.SourceSha256 = manifest.SourceSha256; valid.Add(preset);
                if (clean) data.CleanPresetIds.Add(preset.Id);
            }
            data.Presets = valid;
            if (!string.IsNullOrEmpty(data.DefaultPresetId) && !valid.Any(p => p.Id == data.DefaultPresetId)) data.DefaultPresetId = null;
            if (changed) data.SourceSha256 = manifest.SourceSha256;
            return changed;
        }

        private PresetItem Resolve(PresetItem item, bool sameTemplate, out string reason, out bool migrated)
        {
            reason = null; migrated = false;
            if (item == null || string.IsNullOrEmpty(item.ItemId)) { reason = "항목 정보가 비어 있습니다."; return null; }
            if (LegendSelection.IsBlankId(item.ItemId))
            {
                if (manifest.SupportsBlankRows) return item;
                reason = "이 공종은 빈 행을 넣을 수 없습니다."; return null;
            }
            var byId = manifest.FindRow(item.ItemId);
            bool hasIdentity = item.SourceKey != null || item.SourceName != null;
            if (sameTemplate && byId != null)
            {
                // 저장할 때와 같은 템플릿: ID가 그대로 맞습니다. 근거가 없는 구버전 항목은 이번에 채워 둡니다.
                if (item.SourceKey == null) { migrated = true; return Relink(item, byId); }
                if (item.SourceKey == byId.Key) return item;
            }
            if (!hasIdentity)
            {
                reason = "이전 버전에서 저장한 항목이라 템플릿이 바뀐 뒤에는 같은 항목인지 확인할 수 없습니다. 다시 골라 저장하세요.";
                return null;
            }
            // 1) 원본 객체 핸들이 같은 행(행을 끼워 넣거나 옮겨도 유지)
            if (item.SourceKey != null)
            {
                var keyMatches = manifest.Rows.Where(r => r.Key == item.SourceKey).ToList();
                if (keyMatches.Count == 1) { migrated = true; return Relink(item, keyMatches[0]); }
            }
            // 2) 기호가 같은 행(글자만 다시 쓴 경우). 여러 행이 함께 쓰는 기호는 근거가 되지 않습니다.
            if (!string.IsNullOrEmpty(item.SourceSymbol))
            {
                var symbolMatches = manifest.Rows.Where(r => r.SymbolKey == item.SourceSymbol).ToList();
                if (symbolMatches.Count == 1) { migrated = true; return Relink(item, symbolMatches[0]); }
            }
            // 3) 명칭·규격(+단위·비고)이 모두 같은 행이 딱 하나일 때만
            var textMatches = manifest.Rows.Where(r => r.Name == item.SourceName && r.Spec == item.SourceSpec
                && (item.SourceUnit == null || (r.Unit ?? "") == item.SourceUnit) && (item.SourceRemark == null || (r.Remark ?? "") == item.SourceRemark)).ToList();
            if (textMatches.Count == 1) { migrated = true; return Relink(item, textMatches[0]); }
            if (textMatches.Count > 1)
            {
                reason = "명칭·규격이 같은 항목이 " + textMatches.Count + "개(" + string.Join(", ", textMatches.Select(r => string.IsNullOrEmpty(r.Remark) ? r.Id : r.Remark)) + ")라 자동으로 고르지 않았습니다. 다시 골라 저장하세요.";
                return null;
            }
            var sameName = manifest.Rows.Where(r => r.Name == item.SourceName && r.Spec == item.SourceSpec).ToList();
            reason = sameName.Count > 0
                ? "명칭·규격은 같지만 단위·비고가 달라졌습니다(" + string.Join(", ", sameName.Select(r => r.Describe())) + "). 다시 골라 저장하세요."
                : "현재 템플릿에서 찾을 수 없습니다(지워졌거나 기호·명칭·규격이 모두 바뀜).";
            return null;
        }

        private static PresetItem Relink(PresetItem item, LegendRow row)
        {
            var linked = PresetItem.From(row, item.Quantity, item.NameOverride, item.SpecOverride);
            return linked;
        }

        private static bool IsValid(PresetItem item)
        {
            try
            {
                if (LegendSelection.IsBlankId(item.ItemId)) return true;
                LegendSelection.NormalizeQuantity(item.Quantity); LegendSelection.NormalizeLabel(item.NameOverride); LegendSelection.NormalizeLabel(item.SpecOverride);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// 템플릿이 바뀐 뒤 확실히 다시 연결한 프리셋만 파일에 반영합니다. 문제가 있는 프리셋·잘못된 프리셋은 원본 그대로 둡니다
        /// (화면에서 뺀 항목이 파일에서 지워지지 않게). 문서 전체 해시는 모든 프리셋이 정상일 때만 바꿉니다.
        /// </summary>
        private void TrySaveClean(PresetDocument display, string readFrom)
        {
            try
            {
                string file; var raw = ReadRaw(out file);
                var byId = display.Presets.Where(p => display.CleanPresetIds.Contains(p.Id)).ToDictionary(p => p.Id);
                bool any = false;
                for (int i = 0; i < raw.Presets.Count; i++)
                {
                    var preset = raw.Presets[i]; LegendPreset fixedPreset;
                    if (preset == null || preset.Id == null || !byId.TryGetValue(preset.Id, out fixedPreset)) continue;
                    if (raw.Presets.Count(p => p != null && p.Id == preset.Id) != 1) continue;
                    raw.Presets[i] = fixedPreset; any = true;
                }
                bool allClean = display.Issues.Count == 0;
                if (allClean) raw.SourceSha256 = manifest.SourceSha256;
                if (!any && !allClean) return;
                if (!string.Equals(Path.GetFullPath(readFrom), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) || File.Exists(path)) Backup(readFrom, ".bak");
                Write(raw, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException) { } // 다음에 다시 시도
        }

        public const int KeepBackups = 10;

        /// <summary>바꾸기 전 백업(.backup-시각)은 최근 10개만 남깁니다.</summary>
        private static void PruneBackups(string file)
        {
            try
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(file));
                foreach (var old in new DirectoryInfo(folder).GetFiles(Path.GetFileName(file) + ".backup-*").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(KeepBackups))
                    try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void Backup(string file, string suffix)
        {
            if (File.Exists(file) && !File.Exists(file + suffix)) File.Copy(file, file + suffix);
        }

        private static void Write(PresetDocument data, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(PresetDocument)).WriteObject(stream, data);
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        /// <summary>이전 버전 파일(presets-공종-해시.json) 중 가장 최근 것을 읽습니다.</summary>
        private string LegacyPath()
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(folder) || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath(manifest.CategoryId)), StringComparison.OrdinalIgnoreCase)) return null;
            return new DirectoryInfo(folder).GetFiles("presets-" + manifest.CategoryId + "-*.json").OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).FirstOrDefault();
        }
    }
}
