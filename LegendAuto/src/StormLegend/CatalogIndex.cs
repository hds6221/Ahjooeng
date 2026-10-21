using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace StormLegend
{
    [DataContract] public sealed class CatalogEntry
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "manifestFile")] public string ManifestFile { get; set; }
        [DataMember(Name = "itemCount")] public int ItemCount { get; set; }
        public override string ToString() { return Name + "  ·  " + ItemCount + (Id == "watershed" ? "세트" : "개"); }
    }

    /// <summary>공종 목록(catalog-index.json). 공종 파일은 한 번만 읽어 보관합니다.</summary>
    [DataContract] public sealed class CatalogIndex
    {
        public const string FileName = "catalog-index.json";
        public const string PreviewFileName = "preview-catalog.json";

        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "defaultCategoryId")] public string DefaultCategoryId { get; set; }
        [DataMember(Name = "sourceDwg")] public string SourceDwg { get; set; }
        [DataMember(Name = "sourceDwgSha256")] public string SourceDwgSha256 { get; set; }
        [DataMember(Name = "categories")] public List<CatalogEntry> Categories { get; set; }

        private Dictionary<string, TemplateManifest> manifests;
        public string DirectoryPath { get; private set; }
        public string PreviewPath { get { return Path.Combine(DirectoryPath, PreviewFileName); } }

        public static CatalogIndex Load(string path)
        {
            CatalogIndex data;
            using (var stream = File.OpenRead(path)) data = (CatalogIndex)new DataContractJsonSerializer(typeof(CatalogIndex)).ReadObject(stream);
            if (data == null || data.SchemaVersion != 1 || data.Categories == null || data.Categories.Count == 0 || data.Categories.Any(c => c == null || string.IsNullOrEmpty(c.Id))
                || data.Categories.Select(c => c.Id).Distinct().Count() != data.Categories.Count || !data.Categories.Any(c => c.Id == data.DefaultCategoryId))
                throw new InvalidDataException("공종 목록이 잘못되었습니다.");
            data.DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(path));
            data.manifests = new Dictionary<string, TemplateManifest>(StringComparer.Ordinal);
            foreach (var entry in data.Categories) data.manifests.Add(entry.Id, data.ReadManifest(entry));
            return data;
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(CatalogIndex)).WriteObject(stream, this);
        }

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, KeyValuePair<string, CatalogIndex>> cache = new Dictionary<string, KeyValuePair<string, CatalogIndex>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>목록·공종 파일이 그대로면 앞서 읽은 결과를 다시 씁니다. 하나라도 바뀌면 새로 읽습니다.</summary>
        public static CatalogIndex LoadCached(string path)
        {
            string full = Path.GetFullPath(path);
            string stamp = Stamp(full);
            lock (cacheLock)
            {
                KeyValuePair<string, CatalogIndex> hit;
                if (cache.TryGetValue(full, out hit) && hit.Key == stamp) return hit.Value;
            }
            var index = Load(full);
            lock (cacheLock) cache[full] = new KeyValuePair<string, CatalogIndex>(stamp, index);
            return index;
        }

        private static string Stamp(string indexPath)
        {
            var parts = new List<string>();
            var folder = Path.GetDirectoryName(indexPath);
            foreach (var file in new[] { indexPath }.Concat(Directory.Exists(Path.Combine(folder, "categories")) ? Directory.GetFiles(Path.Combine(folder, "categories"), "*.json") : new string[0]))
            {
                var info = new FileInfo(file);
                parts.Add(info.Name + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks);
            }
            return string.Join("|", parts);
        }

        /// <summary>읽어 둔 공종 정보를 돌려줍니다. 같은 공종은 같은 객체입니다.</summary>
        public TemplateManifest LoadManifest(CatalogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            TemplateManifest manifest;
            if (manifests != null && manifests.TryGetValue(entry.Id, out manifest)) return manifest;
            return ReadManifest(entry);
        }

        public TemplateManifest Manifest(string categoryId) { return LoadManifest(Categories.Single(c => c.Id == categoryId)); }
        public IEnumerable<TemplateManifest> Manifests { get { return Categories.Select(LoadManifest); } }
        public string SourcePath(TemplateManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.SourceFile)) throw new InvalidDataException("원본 파일 경로가 비어 있습니다.");
            string full = Path.GetFullPath(Path.Combine(DirectoryPath, manifest.SourceFile));
            if (!full.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("원본 파일은 템플릿 폴더 안에 있어야 합니다.");
            return full;
        }

        private TemplateManifest ReadManifest(CatalogEntry entry)
        {
            string path = Path.GetFullPath(Path.Combine(DirectoryPath, entry.ManifestFile));
            if (!path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("공종 파일 경로가 잘못되었습니다.");
            var m = TemplateManifest.Load(path);
            if (m.CategoryId != entry.Id || m.Rows.Count != entry.ItemCount) throw new InvalidDataException("공종 목록과 항목이 일치하지 않습니다: " + entry.Name);
            return m;
        }
    }

    [DataContract] public sealed class PreviewPoint
    {
        [DataMember(Name = "x")] public double X { get; set; }
        [DataMember(Name = "y")] public double Y { get; set; }
    }

    /// <summary>미리보기용 단순 도형(선·다각형·문자). 실제 CAD 생성에는 쓰지 않습니다.</summary>
    [DataContract] public sealed class PreviewPrimitive
    {
        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "color")] public string Color { get; set; }
        [DataMember(Name = "dash")] public bool Dash { get; set; }
        [DataMember(Name = "points", EmitDefaultValue = false)] public List<PreviewPoint> Points { get; set; }
        [DataMember(Name = "contours", EmitDefaultValue = false)] public List<List<PreviewPoint>> Contours { get; set; }
        [DataMember(Name = "closed", EmitDefaultValue = false)] public bool Closed { get; set; }
        [DataMember(Name = "fill", EmitDefaultValue = false)] public string Fill { get; set; }
        /// <summary>해치 패턴 선(도면 단위, 각도·간격·축척이 이미 반영된 값). 없으면 예전처럼 사선으로 채웁니다.</summary>
        [DataMember(Name = "pattern", EmitDefaultValue = false)] public List<PreviewHatchLine> Pattern { get; set; }
        [DataMember(Name = "x", EmitDefaultValue = false)] public double X { get; set; }
        [DataMember(Name = "y", EmitDefaultValue = false)] public double Y { get; set; }
        [DataMember(Name = "text", EmitDefaultValue = false)] public string Text { get; set; }
        [DataMember(Name = "height", EmitDefaultValue = false)] public double Height { get; set; }
        [DataMember(Name = "widthFactor", EmitDefaultValue = false)] public double WidthFactor { get; set; }
        [DataMember(Name = "rotation", EmitDefaultValue = false)] public double Rotation { get; set; }
        [DataMember(Name = "horizontal", EmitDefaultValue = false)] public int Horizontal { get; set; }
        [DataMember(Name = "vertical", EmitDefaultValue = false)] public int Vertical { get; set; }
        public bool IsText { get { return Kind == "text"; } }
    }

    /// <summary>해치 패턴 한 줄 묶음: 기준점(X,Y)을 지나는 각도 Angle(도)의 평행선을 (OffsetX,OffsetY)마다 반복, Dashes는 + 선/− 틈/0 점.</summary>
    [DataContract] public sealed class PreviewHatchLine
    {
        [DataMember(Name = "a")] public double Angle { get; set; }
        [DataMember(Name = "x")] public double X { get; set; }
        [DataMember(Name = "y")] public double Y { get; set; }
        [DataMember(Name = "dx")] public double OffsetX { get; set; }
        [DataMember(Name = "dy")] public double OffsetY { get; set; }
        [DataMember(Name = "dash", EmitDefaultValue = false)] public List<double> Dashes { get; set; }
    }

    [DataContract] public sealed class PreviewEntity
    {
        [DataMember(Name = "handle")] public string Handle { get; set; }
        [DataMember(Name = "primitives")] public List<PreviewPrimitive> Primitives { get; set; }
    }

    [DataContract] public sealed class PreviewCatalogFile
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "sourceSha256")] public string SourceSha256 { get; set; }
        [DataMember(Name = "entities")] public List<PreviewEntity> Entities { get; set; }

        public static PreviewCatalogFile Load(string path)
        {
            using (var stream = File.OpenRead(path)) return (PreviewCatalogFile)new DataContractJsonSerializer(typeof(PreviewCatalogFile)).ReadObject(stream);
        }
        public void Save(string path)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(PreviewCatalogFile)).WriteObject(stream, this);
        }

        /// <summary>
        /// 미리보기 데이터 검사: 빈 목록·빈 핸들·중복 핸들·숫자가 아닌 좌표(NaN, 무한대, 비정상적으로 큰 값)·공종에 필요한 핸들 누락.
        /// 문제가 있으면 무엇이 문제인지 InvalidDataException으로 알려줍니다.
        /// </summary>
        public void Validate(IEnumerable<TemplateManifest> manifests)
        {
            if (SchemaVersion != 1) throw new InvalidDataException("미리보기 형식 번호가 맞지 않습니다(" + SchemaVersion + ").");
            if (Entities == null) throw new InvalidDataException("미리보기 목록이 비어 있습니다.");
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in Entities)
            {
                if (entity == null || string.IsNullOrWhiteSpace(entity.Handle)) throw new InvalidDataException("미리보기에 핸들이 없는 항목이 있습니다.");
                if (!handles.Add(entity.Handle)) throw new InvalidDataException("미리보기에 같은 핸들이 두 번 있습니다: " + entity.Handle);
                if (entity.Primitives == null) continue;
                foreach (var p in entity.Primitives)
                {
                    if (p == null) throw new InvalidDataException("미리보기 " + entity.Handle + "에 빈 도형이 있습니다.");
                    if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Height) || !Finite(p.WidthFactor) || !Finite(p.Rotation)) throw new InvalidDataException("미리보기 " + entity.Handle + "의 좌표·크기가 숫자가 아닙니다.");
                    if (p.Contours != null && p.Contours.Any(loop => loop == null || loop.Count < 3 || loop.Any(pt => pt == null || !Finite(pt.X) || !Finite(pt.Y))))
                        throw new InvalidDataException("미리보기 " + entity.Handle + "의 해치 경계가 잘못되었습니다.");
                    if (p.Pattern != null && (p.Pattern.Count > 64 || p.Pattern.Any(h => h == null || !Finite(h.Angle) || !Finite(h.X) || !Finite(h.Y) || !Finite(h.OffsetX) || !Finite(h.OffsetY)
                        || (h.Dashes != null && (h.Dashes.Count > 64 || h.Dashes.Any(d => !Finite(d)))))))
                        throw new InvalidDataException("미리보기 " + entity.Handle + "의 해치 패턴 값이 잘못되었습니다.");
                    if (p.Points != null && p.Points.Any(pt => pt == null || !Finite(pt.X) || !Finite(pt.Y))) throw new InvalidDataException("미리보기 " + entity.Handle + "의 점 좌표가 잘못되었습니다.");
                }
            }
            foreach (var manifest in manifests)
            {
                if (!string.Equals(SourceSha256, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("미리보기가 다른 템플릿용입니다(" + manifest.CategoryName + ").");
                var missing = manifest.AllHandles.FirstOrDefault(h => !handles.Contains(h));
                if (missing != null) throw new InvalidDataException("미리보기에 " + manifest.CategoryName + " 원본 객체(" + missing + ")가 없습니다.");
            }
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1e12; }
    }
}
