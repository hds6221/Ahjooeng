using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StormLegend
{
    /// <summary>지금 쓰는 템플릿 연결 정보가 어디서 왔는지.</summary>
    public enum TemplateSource
    {
        /// <summary>배포 Template 폴더의 미리 만든 연결 정보(템플릿을 고치지 않음).</summary>
        Shipped,
        /// <summary>고친 템플릿을 자동 분석한 결과.</summary>
        Changed,
        /// <summary>지금 템플릿을 읽지 못해, 같은 설치 위치의 마지막 정상 분석본(또는 배포본)으로 대신 연 상태.</summary>
        Recovered
    }

    public sealed class TemplateBundle
    {
        public CatalogIndex Index { get; internal set; }
        /// <summary>명령줄·화면에 보여줄 안내(템플릿 변경 반영·복구 등). 없으면 null.</summary>
        public string Notice { get; internal set; }
        public IList<string> Warnings { get; internal set; }
        public TemplateSource Source { get; internal set; }
        /// <summary>복구본으로 연 이유(Source == Recovered일 때).</summary>
        public string RecoveryReason { get; internal set; }
        public bool FromChangedTemplate { get { return Source == TemplateSource.Changed; } }
        public bool IsRecovered { get { return Source == TemplateSource.Recovered; } }

        public string Describe()
        {
            switch (Source)
            {
                case TemplateSource.Changed: return "수정본(고친 템플릿을 자동 분석해 사용 중)";
                case TemplateSource.Recovered: return "복구본(지금 템플릿을 읽지 못해 마지막 정상 분석본 사용 중 — " + RecoveryReason + ")";
                default: return "배포본(템플릿을 고치지 않은 상태)";
            }
        }
    }

    /// <summary>
    /// 범례 템플릿 위치와 분석 결과를 관리합니다.
    /// · 배포 Template\범례템플릿.dwg가 배포 분석본과 같으면 배포된 연결 정보를 씁니다.
    /// · 사용자가 템플릿을 고쳐 저장했으면 다시 분석해 %LOCALAPPDATA%\StormLegend\templates\(버전-해시)에 보관합니다.
    ///   이 분석본은 DWG 내용(해시) 기준이라 여러 설치본이 같이 써도 안전합니다.
    /// · 템플릿을 읽지 못하면 "이 설치 위치의 템플릿"이 마지막으로 성공한 분석본만 대신 씁니다(templates\history\).
    ///   다른 프로젝트·설치본의 분석본으로는 절대 복구하지 않습니다.
    /// </summary>
    public static class TemplateRepository
    {
        public const string TemplateFolder = "Template";
        public const string TemplateFileName = "범례템플릿.dwg";
        public const string SnapshotFileName = "source.dwg";
        private const int KeepCaches = 8, KeepHistory = 5;
        /// <summary>분석 규칙이 바뀌면 올립니다. 이전 분석본은 자동으로 다시 만듭니다.</summary>
        public const string AnalyzerVersion = "a6";

        public static string TemplateDwgPath(string baseDirectory) { return Path.Combine(baseDirectory, TemplateFolder, TemplateFileName); }

        public static string CacheRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormLegend", "templates"); }
        }

        public static TemplateBundle Resolve(string baseDirectory) { return Resolve(baseDirectory, CacheRoot); }

        public static TemplateBundle Resolve(string baseDirectory, string cacheRoot)
        {
            using (FileGate.Enter(cacheRoot)) return ResolveLocked(baseDirectory, cacheRoot);
        }

        private static TemplateBundle ResolveLocked(string baseDirectory, string cacheRoot)
        {
            string shippedPath = Path.Combine(baseDirectory, TemplateFolder, CatalogIndex.FileName);
            string dwg = Path.GetFullPath(TemplateDwgPath(baseDirectory));
            string shippedProblem;
            CatalogIndex shipped = ValidShipped(shippedPath, out shippedProblem);
            if (!File.Exists(dwg))
            {
                return Recover(cacheRoot, dwg, shipped, "템플릿 DWG 파일이 없습니다.", new FileNotFoundException("범례 템플릿을 찾을 수 없습니다.", dwg));
            }
            string sha;
            try { sha = TemplateManifest.ComputeSha256(dwg); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Recover(cacheRoot, dwg, shipped, "템플릿 파일을 읽을 수 없습니다: " + ex.Message, ex); }

            // 배포본과 같은 DWG이고 배포 연결 정보(공종·미리보기)가 온전하면 그대로 씁니다.
            if (shipped != null && string.Equals(shipped.SourceDwgSha256, sha, StringComparison.OrdinalIgnoreCase))
                return new TemplateBundle { Index = shipped, Warnings = new string[0], Source = TemplateSource.Shipped };

            string cacheDir = Path.Combine(cacheRoot, AnalyzerVersion + "-" + sha.Substring(0, 16));
            string problem;
            var cached = Validate(cacheDir, sha, out problem);
            if (cached != null) { Remember(cacheRoot, dwg, cacheDir); return Changed(cached, ReadWarnings(cacheDir), shipped, false); }
            try
            {
                var analysis = AnalyzeIntoCache(dwg, sha, cacheDir);
                var index = Validate(cacheDir, sha, out problem);
                if (index == null) throw new InvalidDataException("분석 결과를 검증하지 못했습니다: " + problem);
                Remember(cacheRoot, dwg, cacheDir);
                PruneCaches(cacheRoot, cacheDir);
                // 배포 DWG와 같은데 배포 연결 정보가 손상돼 다시 만든 경우는 배포본과 같은 내용입니다.
                bool sameAsShipped = shippedProblem != null && ShippedSha(shippedPath) == sha;
                var bundle = Changed(index, analysis.Warnings, shipped, true);
                if (sameAsShipped) { bundle.Source = TemplateSource.Shipped; bundle.Notice = "배포 연결 정보가 손상되어 템플릿 원본으로 다시 만들었습니다(" + shippedProblem + ")."; }
                return bundle;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Recover(cacheRoot, dwg, shipped, ex.Message, ex);
            }
        }

        /// <summary>이 템플릿 경로의 마지막 정상 분석본 → 배포본 순서로 대신 엽니다.</summary>
        private static TemplateBundle Recover(string cacheRoot, string dwg, CatalogIndex shipped, string reason, Exception error)
        {
            foreach (var dir in History(cacheRoot, dwg))
            {
                string problem;
                var index = Validate(dir, null, out problem);
                if (index == null) continue;
                return new TemplateBundle { Index = index, Warnings = new string[0], Source = TemplateSource.Recovered, RecoveryReason = reason,
                    Notice = "⚠ 템플릿을 읽지 못해 이 템플릿의 마지막 정상 분석본으로 엽니다(새로 고친 내용은 빠져 있음). 원인: " + reason };
            }
            if (shipped != null)
                return new TemplateBundle { Index = shipped, Warnings = new string[0], Source = TemplateSource.Recovered, RecoveryReason = reason,
                    Notice = "⚠ 템플릿을 읽지 못해 처음 배포된 템플릿 정보로 엽니다(고친 내용은 빠져 있음). 원인: " + reason };
            throw new InvalidDataException("범례 템플릿을 읽지 못했습니다: " + reason, error);
        }

        private static TemplateBundle Changed(CatalogIndex index, IList<string> warnings, CatalogIndex shipped, bool fresh)
        {
            string notice = null;
            if (fresh)
            {
                var parts = index.Categories.Select(c =>
                {
                    var before = shipped == null ? null : shipped.Categories.FirstOrDefault(x => x.Id == c.Id);
                    return before == null || before.ItemCount == c.ItemCount ? c.Name + " " + c.ItemCount : c.Name + " " + before.ItemCount + "→" + c.ItemCount;
                });
                notice = "템플릿 변경을 반영했습니다: " + string.Join(", ", parts);
                var failed = (warnings ?? new string[0]).Where(w => w.Contains("표를 읽지 못해 제외")).ToList();
                var missing = shipped == null ? new List<string>() : shipped.Categories.Where(c => !index.Categories.Any(x => x.Id == c.Id)).Select(c => c.Name).ToList();
                if (failed.Count > 0 || missing.Count > 0)
                    notice = "⚠ 템플릿 일부만 반영했습니다. 빠진 공종: " + (missing.Count > 0 ? string.Join(", ", missing) : "-") + " / " + string.Join(" / ", failed) + "  ·  반영된 공종: " + string.Join(", ", parts);
            }
            return new TemplateBundle { Index = index, Warnings = warnings ?? new string[0], Notice = notice, Source = TemplateSource.Changed };
        }

        /// <summary>템플릿 DWG를 스냅샷으로 복사한 뒤 분석합니다. 분석 중 원본이 바뀌어도 결과가 섞이지 않습니다.</summary>
        public static TemplateAnalysis AnalyzeIntoCache(string dwg, string expectedSha, string cacheDir)
        {
            using (FileGate.Enter(Path.GetDirectoryName(Path.GetFullPath(cacheDir))))
            {
                string problem;
                var existing = Validate(cacheDir, expectedSha, out problem);
                if (existing != null) return new TemplateAnalysis { Index = existing, Manifests = existing.Manifests.ToList(),
                    Preview = PreviewCatalogFile.Load(existing.PreviewPath), Warnings = ReadWarnings(cacheDir) };
                return AnalyzeLocked(dwg, expectedSha, cacheDir);
            }
        }

        private static TemplateAnalysis AnalyzeLocked(string dwg, string expectedSha, string cacheDir)
        {
            string work = cacheDir + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Directory.CreateDirectory(work);
            try
            {
                string snapshot = Path.Combine(work, SnapshotFileName);
                using (var input = new FileStream(dwg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var output = File.Create(snapshot)) input.CopyTo(output);
                string sha = TemplateManifest.ComputeSha256(snapshot);
                if (expectedSha != null && !string.Equals(sha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("템플릿을 읽는 중에 파일이 바뀌었습니다. 저장이 끝난 뒤 다시 실행하세요.");
                TemplateAnalysis analysis;
                using (var db = LegendComposer.OpenSource(snapshot))
                    analysis = TemplateAnalyzer.Analyze(db, SnapshotFileName, sha, TemplateFileName, sha);
                analysis.WriteTo(work);
                string problem;
                if (ValidateFolder(work, sha, out problem) == null) throw new InvalidDataException("분석 결과를 다시 읽어 검증하지 못했습니다: " + problem);
                try
                {
                    // 잠금 안에서 검증에 실패한 캐시만 격리합니다. 정상 캐시는 위에서 재사용합니다.
                    if (Directory.Exists(cacheDir)) Directory.Move(cacheDir, cacheDir + ".invalid-" + Guid.NewGuid().ToString("N"));
                    MoveDirectory(work, cacheDir);
                }
                catch (IOException)
                {
                    // 다른 AutoCAD 창이 같은 템플릿을 동시에 분석해 먼저 저장한 경우: 그 결과가 온전하면 그대로 씁니다.
                    if (Validate(cacheDir, sha, out problem) == null) throw;
                }
                return analysis;
            }
            finally { if (Directory.Exists(work)) try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        /// <summary>
        /// 방금 만든 폴더는 백신·검색 색인이 잠깐 잡고 있어 이동이 거부될 수 있습니다. 몇 번 다시 시도하고, 그래도 안 되면 복사 후 지웁니다.
        /// </summary>
        private static void MoveDirectory(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { Directory.Move(from, to); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 6 && !Directory.Exists(to))
                { System.Threading.Thread.Sleep(100 * attempt); }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && !Directory.Exists(to))
                {
                    string staging = to + ".copy-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    try
                    {
                    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                    {
                        string target = Path.Combine(staging, file.Substring(from.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
                    }
                    Directory.Move(staging, to); // 같은 폴더 안 이름 바꾸기
                    }
                    finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    try { Directory.Delete(from, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- 검증

        private static CatalogIndex ValidShipped(string indexPath, out string problem)
        {
            problem = null;
            if (!File.Exists(indexPath)) { problem = "배포 연결 정보 없음"; return null; }
            try
            {
                var index = CatalogIndex.LoadCached(indexPath);
                ValidatePreview(index);
                foreach (var manifest in index.Manifests.GroupBy(m => m.SourceFile, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    manifest.VerifySource(index.SourcePath(manifest));
                return index;
            }
            catch (Exception ex) when (IsDataError(ex)) { problem = ex.Message; return null; }
        }

        private static string ShippedSha(string indexPath)
        {
            try
            {
                using (var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var raw = (CatalogIndex)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(CatalogIndex)).ReadObject(stream);
                    return raw == null ? null : raw.SourceDwgSha256;
                }
            }
            catch (Exception ex) when (IsDataError(ex)) { return null; }
        }

        /// <summary>분석본 폴더 검증: 목록·공종 JSON, source.dwg 존재와 해시, 미리보기까지 모두 맞아야 씁니다.</summary>
        private static CatalogIndex Validate(string cacheDir, string sha, out string problem)
        {
            problem = null;
            if (!Directory.Exists(cacheDir)) { problem = "분석본 없음"; return null; }
            return ValidateFolder(cacheDir, sha, out problem);
        }

        private static CatalogIndex ValidateFolder(string folder, string sha, out string problem)
        {
            problem = null;
            try
            {
                var index = CatalogIndex.LoadCached(Path.Combine(folder, CatalogIndex.FileName));
                if (sha != null && !string.Equals(index.SourceDwgSha256, sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("다른 템플릿의 분석본입니다.");
                string source = Path.Combine(folder, SnapshotFileName);
                if (!File.Exists(source)) throw new FileNotFoundException("분석본의 원본(source.dwg)이 없습니다.");
                if (!string.Equals(TemplateManifest.ComputeSha256(source), index.SourceDwgSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("분석본의 원본(source.dwg)이 바뀌었습니다.");
                foreach (var manifest in index.Manifests)
                    if (!string.Equals(manifest.SourceSha256, index.SourceDwgSha256, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(manifest.SourceFile, SnapshotFileName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("공종 연결 정보가 분석본의 원본과 다릅니다.");
                ValidatePreview(index);
                return index;
            }
            catch (Exception ex) when (IsDataError(ex)) { problem = ex.Message; return null; }
        }

        private static void ValidatePreview(CatalogIndex index)
        {
            PreviewCatalogFile.Load(index.PreviewPath).Validate(index.Manifests);
        }

        private static bool IsDataError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is UnauthorizedAccessException
                || ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException;
        }

        // ---------------------------------------------------------------- 템플릿별 정상 분석 이력

        private static string HistoryFile(string cacheRoot, string dwg)
        {
            using (var sha = SHA256.Create())
            {
                string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(dwg).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
                return Path.Combine(cacheRoot, "history", key + ".txt");
            }
        }

        /// <summary>이 템플릿 경로에서 정상으로 쓴 분석본(최근 것부터).</summary>
        public static IList<string> History(string cacheRoot, string dwg)
        {
            string file = HistoryFile(cacheRoot, dwg);
            try
            {
                if (!File.Exists(file)) return new string[0];
                return File.ReadAllLines(file).Skip(1).Where(l => l.Length > 0 && l.IndexOfAny(Path.GetInvalidFileNameChars()) < 0).Select(l => Path.Combine(cacheRoot, l)).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new string[0]; }
        }

        private static void Remember(string cacheRoot, string dwg, string cacheDir)
        {
            string file = HistoryFile(cacheRoot, dwg);
            try
            {
                string name = Path.GetFileName(cacheDir);
                var entries = History(cacheRoot, dwg).Select(Path.GetFileName).Where(n => n != name).ToList();
                entries.Insert(0, name);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllLines(temp, new[] { Path.GetFullPath(dwg) }.Concat(entries.Take(KeepHistory)));
                    if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
                }
                finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static IList<string> ReadWarnings(string cacheDir)
        {
            string report = Path.Combine(cacheDir, "analysis-report.txt");
            if (!File.Exists(report)) return new string[0];
            try { return File.ReadAllLines(report).Where(l => l.StartsWith("  - ", StringComparison.Ordinal)).Select(l => l.Substring(4)).ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new[] { "분석 보고서를 읽지 못했습니다: " + ex.Message }; }
        }

        private static IEnumerable<DirectoryInfo> CacheDirectories(string cacheRoot)
        {
            if (!Directory.Exists(cacheRoot)) return new DirectoryInfo[0];
            return new DirectoryInfo(cacheRoot).GetDirectories().Where(d => !d.Name.Contains(".tmp-") && !d.Name.Contains(".copy-") && !d.Name.Contains(".invalid-") && d.Name.Contains("-") && d.Name != "history").OrderByDescending(d => d.LastWriteTimeUtc);
        }

        /// <summary>오래된 분석본을 지웁니다. 어느 템플릿이든 복구 이력에 남아 있는 분석본은 지우지 않습니다.</summary>
        private static void PruneCaches(string cacheRoot, string keep)
        {
            var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileName(keep) };
            string historyDir = Path.Combine(cacheRoot, "history");
            if (Directory.Exists(historyDir))
                foreach (var file in Directory.GetFiles(historyDir, "*.txt"))
                    try { foreach (var line in File.ReadAllLines(file).Skip(1)) protectedNames.Add(line); } catch (IOException) { }
            foreach (var directory in CacheDirectories(cacheRoot).Skip(KeepCaches))
            {
                if (protectedNames.Contains(directory.Name)) continue;
                try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            // 검증 실패로 격리한 폴더(.invalid-)와 중단된 작업 폴더(.tmp-/.copy-)는 1시간이 지나면 지웁니다.
            // (잠금 안에서 실행되므로 지금 다른 창이 쓰는 작업 폴더는 아닙니다. 잠금이 없는 이전 버전을 위해 시간 여유를 둡니다.)
            if (Directory.Exists(cacheRoot))
                foreach (var directory in new DirectoryInfo(cacheRoot).GetDirectories())
                {
                    if (!(directory.Name.Contains(".invalid-") || directory.Name.Contains(".tmp-") || directory.Name.Contains(".copy-"))) continue;
                    if (DateTime.UtcNow - directory.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
                    try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
        }
    }
}
