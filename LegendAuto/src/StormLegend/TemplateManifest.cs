using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace StormLegend
{
    /// <summary>한 공종 표(예: 우수)의 원본 객체 연결 정보. JSON(categories/*.json)으로 저장됩니다.</summary>
    [DataContract]
    public sealed class TemplateManifest
    {
        public const int SupportedSchema = 2;

        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "categoryId")] public string CategoryId { get; set; }
        [DataMember(Name = "categoryName")] public string CategoryName { get; set; }
        [DataMember(Name = "frameWidth")] public double FrameWidth { get; set; }
        [DataMember(Name = "frameBottomY")] public double FrameBottomY { get; set; }
        [DataMember(Name = "bottomMargin")] public double BottomMargin { get; set; }
        [DataMember(Name = "hasQuantityColumn")] public bool HasQuantityColumn { get; set; }
        [DataMember(Name = "quantityCenterX")] public double QuantityCenterX { get; set; }
        [DataMember(Name = "quantityCellWidth")] public double QuantityCellWidth { get; set; }
        [DataMember(Name = "auxBorderHandles")] public List<string> AuxBorderHandles { get; set; }
        [DataMember(Name = "sourceFile")] public string SourceFile { get; set; }
        [DataMember(Name = "sourceSha256")] public string SourceSha256 { get; set; }
        [DataMember(Name = "baseScaleDenominator")] public int BaseScaleDenominator { get; set; }
        [DataMember(Name = "origin")] public ManifestPoint Origin { get; set; }
        [DataMember(Name = "rowHeight")] public double RowHeight { get; set; }
        [DataMember(Name = "tableWidth")] public double TableWidth { get; set; }
        [DataMember(Name = "tableLeftX")] public double TableLeftX { get; set; }
        [DataMember(Name = "tableRightX")] public double TableRightX { get; set; }
        [DataMember(Name = "titleTopY")] public double TitleTopY { get; set; }
        [DataMember(Name = "headerTopY")] public double HeaderTopY { get; set; }
        [DataMember(Name = "rowsTopY")] public double RowsTopY { get; set; }
        [DataMember(Name = "sourceBottomY")] public double SourceBottomY { get; set; }
        [DataMember(Name = "headerHandles")] public List<string> HeaderHandles { get; set; }
        [DataMember(Name = "verticalBorderHandles")] public List<string> VerticalBorderHandles { get; set; }
        [DataMember(Name = "outerBorderHandle")] public string OuterBorderHandle { get; set; }
        [DataMember(Name = "wipeoutHandle")] public string WipeoutHandle { get; set; }
        [DataMember(Name = "fullSeparatorHandle")] public string FullSeparatorHandle { get; set; }
        [DataMember(Name = "fullSeparatorY")] public double FullSeparatorY { get; set; }
        [DataMember(Name = "splitSeparatorHandles")] public List<string> SplitSeparatorHandles { get; set; }
        [DataMember(Name = "splitSeparatorY")] public double SplitSeparatorY { get; set; }
        [DataMember(Name = "allHandles")] public List<string> AllHandles { get; set; }
        [DataMember(Name = "rowBoundaryHandles")] public List<string> RowBoundaryHandles { get; set; }
        [DataMember(Name = "groups")] public List<LegendGroup> Groups { get; set; }
        [DataMember(Name = "rows")] public List<LegendRow> Rows { get; set; }

        private Dictionary<string, int> rowIndex;

        /// <summary>빈 행을 넣을 수 있는 표인지 여부. 유역처럼 한 세트로만 넣는 표는 불가합니다.</summary>
        public bool SupportsBlankRows { get { return HasQuantityColumn; } }

        public static TemplateManifest Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var manifest = (TemplateManifest)new DataContractJsonSerializer(typeof(TemplateManifest)).ReadObject(stream);
                manifest.Validate();
                return manifest;
            }
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(TemplateManifest)).WriteObject(stream, this);
        }

        public void Validate()
        {
            string name = CategoryName ?? CategoryId ?? "?";
            if (SchemaVersion != SupportedSchema) throw new InvalidDataException(name + ": 지원하지 않는 템플릿 형식입니다.");
            if (BaseScaleDenominator != 100 || Origin == null || FrameWidth <= 0 || RowHeight <= 0 || Rows == null || Rows.Count == 0 || AllHandles == null || AllHandles.Count == 0 || string.IsNullOrWhiteSpace(CategoryId))
                throw new InvalidDataException(name + ": 공종 템플릿 정보가 잘못되었습니다.");
            if (Rows.Any(r => r == null || string.IsNullOrEmpty(r.Id)) || Rows.Select(r => r.Id).Distinct().Count() != Rows.Count || AllHandles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != AllHandles.Count)
                throw new InvalidDataException(name + ": 중복 항목 또는 핸들입니다.");
            if (Rows.Any(r => r.Cells == null || r.SymbolHandles == null || r.Cells.Any(c => c == null || c.Handles == null)) || HeaderHandles == null || VerticalBorderHandles == null || AuxBorderHandles == null)
                throw new InvalidDataException(name + ": 공종의 원본 연결이 누락되었습니다.");
            var known = new HashSet<string>(AllHandles, StringComparer.OrdinalIgnoreCase);
            var used = HeaderHandles.Concat(VerticalBorderHandles).Concat(AuxBorderHandles).Concat(new[] { OuterBorderHandle, WipeoutHandle })
                .Concat(Rows.SelectMany(r => r.Cells.SelectMany(c => c.Handles).Concat(r.SymbolHandles)));
            if (HasQuantityColumn) used = used.Concat(Rows.Select(r => r.QuantityHandle));
            if (!string.IsNullOrEmpty(FullSeparatorHandle)) used = used.Concat(new[] { FullSeparatorHandle });
            if (used.Any(h => string.IsNullOrEmpty(h) || !known.Contains(h)) || Rows.Any(r => r.Height <= 0 || r.Cells.Any(c => c.Column == 0 && c.Handles.Count == 0)) || Rows.Any(r => LegendSelection.IsBlankId(r.Id)))
                throw new InvalidDataException(name + ": 공종의 원본 객체 연결이 잘못되었습니다.");
            if (new[] { FrameWidth, FrameBottomY, BottomMargin, QuantityCenterX, QuantityCellWidth, Origin.X, Origin.Y, Origin.Z,
                RowHeight, TableWidth, TableLeftX, TableRightX, TitleTopY, HeaderTopY, RowsTopY, SourceBottomY, FullSeparatorY, SplitSeparatorY }.Any(v => !Finite(v))
                || Rows.Any(r => !Finite(r.Height) || !Finite(r.SourceTopY) || !Finite(r.QuantitySourceCenterY)
                    || r.Cells.Any(c => !Finite(c.CenterX) || !Finite(c.Width) || !Finite(c.SourceCenterY) || c.Width < 0 || c.Column < 0 || c.Column > 5)))
                throw new InvalidDataException(name + ": 좌표·크기는 유한한 숫자여야 하고 절댓값은 100억 미만이어야 합니다.");
            if (Rows.Count > 10000 || AllHandles.Count > 1000000 || BottomMargin < 0 || TableRightX <= TableLeftX)
                throw new InvalidDataException(name + ": 표 범위 또는 항목 수가 잘못되었습니다.");
            rowIndex = null;
        }

        // 실제 측량 좌표(mm 단위 수억)에 놓인 표도 받되, 넘침을 일으키는 값은 막습니다.
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 1e10; }

        public LegendRow FindRow(string id)
        {
            if (id == null) return null;
            if (rowIndex == null)
            {
                var map = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < Rows.Count; i++) map[Rows[i].Id] = i;
                rowIndex = map;
            }
            int index;
            return rowIndex.TryGetValue(id, out index) ? Rows[index] : null;
        }

        /// <summary>표 안 원래 순서. 없는 항목은 -1입니다.</summary>
        public int IndexOf(string id)
        {
            var row = FindRow(id);
            return row == null ? -1 : rowIndex[id];
        }

        /// <summary>선택 순서대로 실제 행 목록을 만듭니다. 빈 행은 원본 행 높이의 빈 칸으로 만듭니다.</summary>
        public IList<LegendRow> ResolveRows(IEnumerable<LegendSelection> selection)
        {
            var rows = new List<LegendRow>();
            foreach (var item in selection)
            {
                if (item == null) throw new ArgumentException("선택 항목이 비어 있습니다.");
                if (item.IsBlank)
                {
                    if (!SupportsBlankRows) throw new ArgumentException(CategoryName + " 표에는 빈 행을 넣을 수 없습니다.");
                    rows.Add(LegendRow.CreateBlank(item.ItemId, RowHeight));
                    continue;
                }
                var row = FindRow(item.ItemId);
                if (row == null) throw new ArgumentException("알 수 없는 항목입니다: " + item.ItemId);
                rows.Add(row);
            }
            return rows;
        }

        public void VerifySource(string path)
        {
            Validate();
            if (!string.Equals(ComputeSha256(path), SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("원본 템플릿 파일이 변경되었습니다. LEGEND를 다시 실행하면 새 템플릿을 읽습니다.");
        }

        public static string ComputeSha256(string path)
        {
            // 열려 있는(AutoCAD가 사용 중인) 파일도 읽을 수 있도록 공유 모드로 엽니다.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }

    [DataContract] public sealed class ManifestPoint
    {
        [DataMember(Name = "x")] public double X { get; set; }
        [DataMember(Name = "y")] public double Y { get; set; }
        [DataMember(Name = "z")] public double Z { get; set; }
    }

    [DataContract] public sealed class LegendGroup
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "nameHandle")] public string NameHandle { get; set; }
        [DataMember(Name = "nameSourceCenterY")] public double NameSourceCenterY { get; set; }
    }

    [DataContract] public sealed class LegendRow
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "spec")] public string Spec { get; set; }
        [DataMember(Name = "quantity")] public string Quantity { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }
        [DataMember(Name = "groupId")] public string GroupId { get; set; }
        [DataMember(Name = "sourceTopY")] public double SourceTopY { get; set; }
        [DataMember(Name = "contentHandles")] public List<string> ContentHandles { get; set; }
        [DataMember(Name = "symbolHandles")] public List<string> SymbolHandles { get; set; }
        [DataMember(Name = "quantityHandle")] public string QuantityHandle { get; set; }
        [DataMember(Name = "quantitySourceCenterY")] public double QuantitySourceCenterY { get; set; }
        [DataMember(Name = "height")] public double Height { get; set; }
        [DataMember(Name = "remark")] public string Remark { get; set; }
        [DataMember(Name = "cells")] public List<LegendSourceCell> Cells { get; set; }

        /// <summary>명칭 칸이 비어 있어 목록 이름만 위 행에서 빌려 온 행(도면의 명칭 칸은 템플릿처럼 비워 둠).</summary>
        [DataMember(Name = "nameFromAbove", EmitDefaultValue = false)] public bool NameFromAbove { get; set; }

        public bool IsBlank { get { return LegendSelection.IsBlankId(Id); } }

        /// <summary>
        /// 행 번호와 상관없는 항목 식별값: 이 행을 이루는 원본 객체 핸들(기호·명칭·규격·단위·비고) 묶음.
        /// DWG의 핸들은 행을 끼워 넣거나 지우거나 옮겨도 바뀌지 않으므로, 프리셋이 같은 항목을 다시 찾는 1순위 근거입니다.
        /// </summary>
        public string Key { get { return "c:" + string.Join("+", (ContentHandles ?? new List<string>()).Select(h => h.ToUpperInvariant()).OrderBy(h => h, StringComparer.Ordinal)); } }

        /// <summary>기호 객체 핸들 묶음(기호를 그대로 두고 글자만 고친 경우 다시 찾는 근거). 기호가 없으면 빈 문자열.</summary>
        public string SymbolKey { get { return string.Join("+", (SymbolHandles ?? new List<string>()).Select(h => h.ToUpperInvariant()).OrderBy(h => h, StringComparer.Ordinal)); } }

        /// <summary>사람이 읽는 항목 이름(예: "U형측구 300x300 · 무소음트렌치").</summary>
        public string Describe() { return Name + " " + Spec + (string.IsNullOrEmpty(Remark) ? "" : " · " + Remark); }

        /// <summary>
        /// 칸 내용을 새 행으로 옮길 때 기준이 되는 원본 높이.
        /// 자기 행 안에 있는 기호는 원본 행의 가운데를 기준으로 그대로 옮겨, 기호에 글자가 섞여 있어도(U1-TYPE 등) 템플릿과 같은 위치에 둡니다.
        /// 여러 행이 함께 쓰는 병합 칸의 기호·글자는 각 행 가운데로 모읍니다.
        /// </summary>
        public double SourceCenterOf(LegendSourceCell cell) { return cell.Column == 0 && cell.KeepRowOffset ? SourceTopY - Height / 2 : cell.SourceCenterY; }

        internal static LegendRow CreateBlank(string id, double height)
        {
            return new LegendRow { Id = id, Name = "", Spec = "", Unit = "", Remark = "", Quantity = "", GroupId = id, Height = height,
                Cells = new List<LegendSourceCell>(), SymbolHandles = new List<string>(), ContentHandles = new List<string>() };
        }
    }

    [DataContract] public sealed class LegendSourceCell
    {
        [DataMember(Name = "centerX")] public double CenterX { get; set; }
        [DataMember(Name = "width")] public double Width { get; set; }
        [DataMember(Name = "column")] public int Column { get; set; }
        [DataMember(Name = "handles")] public List<string> Handles { get; set; }
        [DataMember(Name = "sourceCenterY")] public double SourceCenterY { get; set; }
        /// <summary>기호 칸이 원본 행 안에 온전히 들어 있으면 true: 원본 행 안의 위치를 그대로 유지합니다.</summary>
        [DataMember(Name = "keepRowOffset", EmitDefaultValue = false)] public bool KeepRowOffset { get; set; }
    }

    public sealed class LegendBatchItem
    {
        public TemplateManifest Manifest { get; private set; }
        public IList<LegendSelection> Selection { get; private set; }
        public string SourcePath { get; private set; }
        public LegendBatchItem(TemplateManifest manifest, IList<LegendSelection> selection, string sourcePath)
        { Manifest = manifest; Selection = selection; SourcePath = sourcePath; }
    }

    /// <summary>생성할 한 행. 빈 행은 ItemId가 "__blank__:번호" 형식입니다.</summary>
    public sealed class LegendSelection
    {
        public const string BlankPrefix = "__blank__:";
        public const int MaxLabelLength = 80;

        public string ItemId { get; private set; }
        public string Quantity { get; private set; }
        public string NameOverride { get; private set; }
        public string SpecOverride { get; private set; }
        public bool IsBlank { get { return IsBlankId(ItemId); } }

        public LegendSelection(string itemId, string quantity, string nameOverride = null, string specOverride = null)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentException("항목 ID가 비어 있습니다.");
            ItemId = itemId;
            if (IsBlank) { Quantity = ""; return; }
            Quantity = quantity; NameOverride = NormalizeLabel(nameOverride); SpecOverride = NormalizeLabel(specOverride);
        }

        public static LegendSelection Blank(int number) { return new LegendSelection(BlankPrefix + number, null); }
        public static bool IsBlankId(string id) { return id != null && id.StartsWith(BlankPrefix, StringComparison.Ordinal); }

        private static readonly System.Text.RegularExpressions.Regex QuantityPattern =
            new System.Text.RegularExpressions.Regex(@"^(-|[0-9]{1,8}(\.[0-9]{1,3})?)$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>수량: 비우면 "-", 그 외는 0 이상 숫자(정수 8자리, 소수 3자리까지).</summary>
        public static string NormalizeQuantity(string quantity)
        {
            string value = string.IsNullOrWhiteSpace(quantity) ? "-" : quantity.Trim();
            if (!QuantityPattern.IsMatch(value)) throw new ArgumentException("수량은 '-' 또는 0 이상 숫자(정수 8자리, 소수 3자리까지)로 입력하세요.");
            return value;
        }

        public static string NormalizeLabel(string value)
        {
            if (value == null) return null; // null=원본, 빈 문자열=의도적으로 비우기
            value = value.Trim();
            if (value.Length > MaxLabelLength || value.Any(char.IsControl)) throw new ArgumentException("명칭과 규격은 줄바꿈 없이 80자 이내로 입력하세요.");
            return value;
        }
    }
}
