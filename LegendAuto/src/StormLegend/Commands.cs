using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using AcCoreApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(StormLegend.Commands))]
[assembly: ExtensionApplication(typeof(StormLegend.PluginLoader))]
namespace StormLegend
{
    /// <summary>
    /// NETLOAD(또는 자동 로드) 때 한 번 실행됩니다.
    /// · 메뉴 막대 [Ahjooeng] → [범례] 하위 메뉴를 만듭니다(AutoCAD가 한가해진 직후 한 번).
    /// · FILEDIA/CMDDIA는 바꾸지 않습니다. 0이면 알리기만 하고, 되돌리기는 AHJOOENGDIA 명령으로 사용자가 고릅니다.
    /// </summary>
    public sealed class PluginLoader : IExtensionApplication
    {
        public void Initialize()
        {
            if (DialogSettings.IsCoreConsole) return; // 메뉴가 없는 검증용 콘솔
            AcCoreApplication.Idle += AddMenuOnce;
        }

        private static void AddMenuOnce(object sender, EventArgs e)
        {
            AcCoreApplication.Idle -= AddMenuOnce;
            Commands.AddMenu(false);
            DialogSettings.Report(null);
            Commands.WarnOtherCopies();
        }

        public void Terminate() { }
    }

    /// <summary>
    /// FILEDIA=0 / CMDDIA=0이면 열기·저장·NETLOAD 때 파일 선택창 대신 명령줄로 파일명을 묻습니다.
    /// 배치 작업이나 다른 자동화 도구가 일부러 0으로 둘 수 있으므로 이 플러그인은 값을 바꾸지 않습니다.
    /// 0인 것을 알려주고, 사용자가 AHJOOENGDIA를 실행했을 때만 1로 되돌립니다.
    /// </summary>
    internal static class DialogSettings
    {
        private static readonly string[] Names = { "FILEDIA", "CMDDIA" };

        public static bool IsCoreConsole
        {
            get { try { return string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "accoreconsole", StringComparison.OrdinalIgnoreCase); } catch (InvalidOperationException) { return false; } }
        }

        public static IList<string> ZeroValues()
        {
            var zero = new List<string>();
            try { foreach (var name in Names) { object value = AcCoreApplication.GetSystemVariable(name); if (value != null && Convert.ToInt32(value) == 0) zero.Add(name); } }
            catch (System.Exception) { }
            return zero;
        }

        /// <summary>0이면 알리기만 합니다(값은 그대로).</summary>
        public static void Report(Editor ed)
        {
            if (IsCoreConsole) return;
            var zero = ZeroValues(); if (zero.Count == 0) return;
            if (ed == null && AcCoreApplication.DocumentManager.MdiActiveDocument != null) ed = AcCoreApplication.DocumentManager.MdiActiveDocument.Editor;
            if (ed != null) ed.WriteMessage("\n※ " + string.Join(", ", zero) + " 값이 0이라 파일 선택창 대신 명령줄로 파일 이름을 묻습니다. 의도한 설정이 아니면 AHJOOENGDIA를 입력해 1로 되돌리세요.");
        }

        /// <summary>사용자가 요청했을 때만 1로 되돌립니다.</summary>
        public static IList<string> RestoreNow()
        {
            var changed = new List<string>();
            foreach (var name in ZeroValues()) { AcCoreApplication.SetSystemVariable(name, 1); changed.Add(name); }
            return changed;
        }
    }

    /// <summary>
    /// LEGEND / LGD    : 여러 공종을 골라 한 번에 범례 생성(화면)
    /// LEGENDALL       : 한 공종의 전체 항목 생성(명령줄)
    /// LEGENDORIGINAL  : 한 공종 원본 표 그대로 복사(명령줄)
    /// LEGENDTEMPLATE  : 범례 템플릿 DWG 열기(항목 추가·수정)
    /// LEGENDSETTINGS  : 범례 설정 창
    /// LEGENDHELP      : 범례 사용 설명서(그림 포함) 열기
    /// AHJOOENGHELP    : Ahjooeng 도움말 홈(명령 목록) 열기 — 메뉴 [도움말]
    /// AHJOOENGINFO    : 프로그램 정보(버전·설치 위치·템플릿 상태)
    /// AHJOOENGMENU    : 메뉴 막대 [Ahjooeng]을 다시 만들기
    /// AHJOOENGDIA     : FILEDIA·CMDDIA가 0이면 1로 되돌리기(파일 선택창 복구)
    /// </summary>
    public sealed class Commands
    {
        private static string BaseDirectory { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        private static string TemplatePath { get { return Path.GetFullPath(TemplateRepository.TemplateDwgPath(BaseDirectory)); } }

        [CommandMethod("LEGEND", CommandFlags.Modal | CommandFlags.Redraw)] public void Legend() { Create(true, false); }
        /// <summary>LEGEND 줄임 명령.</summary>
        [CommandMethod("LGD", CommandFlags.Modal | CommandFlags.Redraw)] public void Lgd() { Create(true, false); }

        public static string HelpPath { get { return Path.Combine(BaseDirectory, "Help", "index.html"); } }

        [CommandMethod("LEGENDHELP", CommandFlags.Session)]
        public void Help()
        {
            if (!File.Exists(HelpPath)) { Write("\n사용 설명서를 찾을 수 없습니다: " + HelpPath); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HelpPath) { UseShellExecute = true }); Write("\n범례 사용 설명서를 열었습니다."); }
            catch (System.Exception ex) { Write("\n사용 설명서를 열지 못했습니다: " + ex.Message); }
        }

        /// <summary>Ahjooeng 도움말 홈(명령 목록). 설치본: Contents\Help\index.html. 없으면(개발 폴더) 범례 설명서.</summary>
        public static string HelpHomePath
        {
            get
            {
                string home = Path.GetFullPath(Path.Combine(BaseDirectory, "..", "Help", "index.html"));
                return File.Exists(home) ? home : HelpPath;
            }
        }

        [CommandMethod("AHJOOENGHELP", CommandFlags.Session)]
        public void HelpHome()
        {
            string path = HelpHomePath;
            if (!File.Exists(path)) { Write("\n도움말을 찾을 수 없습니다: " + path); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); Write("\nAhjooeng 도움말을 열었습니다. 명령을 누르면 사용 방법이 나옵니다."); }
            catch (System.Exception ex) { Write("\n도움말을 열지 못했습니다: " + ex.Message); }
        }

        [CommandMethod("LEGENDSETTINGS", CommandFlags.Modal)]
        public void Settings() { try { ShowSettings(); } catch (System.Exception ex) { Write("\n설정 창을 열지 못했습니다: " + ex.Message); } }

        [CommandMethod("AHJOOENGINFO", CommandFlags.Modal)]
        public void Info() { try { ShowInfo(); } catch (System.Exception ex) { Write("\n프로그램 정보를 열지 못했습니다: " + ex.Message); } }

        [CommandMethod("AHJOOENGMENU", CommandFlags.Session)]
        public void Menu() { AddMenu(true); }

        [CommandMethod("AHJOOENGDIA", CommandFlags.Modal)]
        public void RestoreDialogs()
        {
            var changed = DialogSettings.RestoreNow();
            Write(changed.Count == 0 ? "\nFILEDIA·CMDDIA가 이미 1입니다(파일 선택창 사용)." : "\n" + string.Join(", ", changed) + "를 1로 되돌렸습니다. 이제 파일 선택창이 열립니다.");
        }

        private static void Write(string text)
        {
            var doc = AcCoreApplication.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.Editor.WriteMessage(text);
        }

        public const string MenuLabel = "범례 생성 (LGD)";

        /// <summary>설치본 위치: %APPDATA%\Autodesk\ApplicationPlugins\Ahjooeng.bundle\Contents\LegendAuto</summary>
        public static string InstalledDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "Ahjooeng.bundle", "Contents", "LegendAuto"); }
        }

        /// <summary>
        /// 다른 위치의 범례 플러그인(예: 개발 폴더 bin\Release를 NETLOAD)이 함께 쓰이면 알립니다.
        /// 예전 DLL을 쓰면 메뉴·도움말·템플릿 분석이 예전 그대로이고, 템플릿 수정도 그 폴더에 저장되어 설치본에 반영되지 않습니다.
        /// </summary>
        public static IList<string> OtherCopies()
        {
            string mine = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            var notes = new List<string>();
            foreach (var other in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (other.IsDynamic || other == Assembly.GetExecutingAssembly() || other.GetName().Name != Assembly.GetExecutingAssembly().GetName().Name) continue;
                string location; try { location = other.Location; } catch (NotSupportedException) { continue; }
                if (!string.IsNullOrEmpty(location) && !string.Equals(Path.GetFullPath(location), mine, StringComparison.OrdinalIgnoreCase))
                    notes.Add("범례 플러그인이 두 곳에서 불러와져 있습니다: " + mine + " / " + location + " (v" + other.GetName().Version + "). 예전 것이 메뉴·명령을 차지할 수 있습니다. AutoCAD를 다시 켜고 NETLOAD는 하지 마세요.");
            }
            string installed = Path.Combine(InstalledDirectory, "StormLegend.dll");
            if (File.Exists(installed) && !string.Equals(Path.GetFullPath(installed), mine, StringComparison.OrdinalIgnoreCase))
                notes.Add("지금 쓰는 범례 플러그인은 설치본이 아닙니다(" + mine + "). 템플릿 수정 내용도 이 폴더에 저장되어 설치본에는 반영되지 않습니다. AutoCAD를 다시 켜서 설치본(메뉴 Ahjooeng)을 쓰세요.");
            return notes;
        }

        public static void WarnOtherCopies()
        {
            try { foreach (string note in OtherCopies()) Write("\n※ " + note); }
            catch (System.Exception ex) when (!(ex is OutOfMemoryException)) { }
        }

        /// <summary>메뉴 막대 [Ahjooeng] → [범례 생성 (LGD)], [도움말 ▸ 범례 생성 (LGD)]. 실패해도 명령은 그대로 쓸 수 있습니다.</summary>
        public static void AddMenu(bool report)
        {
            try
            {
                string error = RegisterMenu();
                WriteStartupRecord(error);
                if (error != null) Write("\n" + error + " (명령줄에 LEGEND 또는 LGD를 입력해 쓸 수 있습니다)");
                else if (report) Write("\n메뉴 막대에 [Ahjooeng] 메뉴를 만들었습니다: " + Ahjooeng.AhjooengMenu.Describe(AcApplication.AcadApplication));
                object bar = AcCoreApplication.GetSystemVariable("MENUBAR");
                if (bar != null && Convert.ToInt32(bar) == 0) Write("\n※ 메뉴 막대가 꺼져 있습니다. 명령줄에 MENUBAR 입력 → 1을 입력하면 [Ahjooeng] 메뉴가 보입니다.");
            }
            catch (System.Exception ex) { Write("\nAhjooeng 메뉴를 만들지 못했습니다: " + ex.Message); }
        }

        /// <summary>
        /// 진단 기록: 마지막으로 불러온 시각·DLL 위치·메뉴 모양을 %LOCALAPPDATA%\StormLegend\startup.txt에 남깁니다
        /// (자동 로드·메뉴 문제를 물어볼 때 이 파일을 보면 됩니다). 기록 실패는 무시합니다.
        /// </summary>
        private static void WriteStartupRecord(string menuError)
        {
            try
            {
                string menu;
                try { menu = menuError ?? Ahjooeng.AhjooengMenu.Describe(AcApplication.AcadApplication); } catch (System.Exception ex) { menu = "메뉴 읽기 실패: " + ex.Message; }
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormLegend");
                Directory.CreateDirectory(folder);
                File.WriteAllLines(Path.Combine(folder, "startup.txt"), new[]
                {
                    "loaded: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    "dll: " + Assembly.GetExecutingAssembly().Location,
                    "version: " + Assembly.GetExecutingAssembly().GetName().Version,
                    "menu: " + menu
                }.Concat(OtherCopies().Select(n => "warning: " + n)).ToArray(), new System.Text.UTF8Encoding(false));
            }
            catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string RegisterMenu()
        {
            // 메뉴: 명령 줄(누르면 바로 LGD) + 맨 아래 [도움말](명령 목록 홈) · [프로그램 정보].
            // (LEGENDALL·LEGENDTEMPLATE·LEGENDSETTINGS·AHJOOENGINFO·AHJOOENGDIA 등은 명령줄과 범례 창 버튼으로 그대로 씁니다.)
            return Ahjooeng.AhjooengMenu.Register(AcApplication.AcadApplication,
                new Ahjooeng.MenuCommand(MenuLabel, "LGD", "여러 공종을 골라 한 번에 범례를 그립니다"));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ShowSettings()
        {
            using (var form = new SettingsForm(LegendSettings.Load()))
            {
                if (AcApplication.ShowModalDialog(null, form, false) != System.Windows.Forms.DialogResult.OK) return;
                form.Result.Save();
                Write("\n범례 설정을 저장했습니다. 다음 생성부터 적용됩니다.");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ShowInfo()
        {
            string status;
            try
            {
                var bundle = TemplateRepository.Resolve(BaseDirectory);
                status = bundle.Describe() + " · " + string.Join(", ", bundle.Index.Categories.Select(c => c.Name + " " + c.ItemCount));
            }
            catch (System.Exception ex) { status = "템플릿을 읽지 못했습니다: " + ex.Message; }
            using (var form = new InfoForm(Assembly.GetExecutingAssembly().GetName().Version.ToString(3), BaseDirectory, TemplatePath, status, LegendSettings.DefaultPath, HelpHomePath))
                AcApplication.ShowModalDialog(null, form, false);
        }
        [CommandMethod("STORMLEGEND", CommandFlags.Modal | CommandFlags.Redraw)] public void StormLegendCommand() { Create(true, false); }
        [CommandMethod("LEGENDALL", CommandFlags.Modal | CommandFlags.Redraw)] public void LegendAll() { Create(false, false); }
        [CommandMethod("LEGENDORIGINAL", CommandFlags.Modal | CommandFlags.Redraw)] public void LegendOriginal() { Create(false, true); }

        private static void Create(bool showPicker, bool original)
        {
            var doc = AcCoreApplication.DocumentManager.MdiActiveDocument; if (doc == null) return; var ed = doc.Editor;
            try
            {
                if (IsTemplate(doc))
                {
                    ed.WriteMessage("\n지금 열린 도면은 범례 템플릿입니다. 템플릿을 저장한 뒤 작업 도면으로 전환해서 LEGEND를 실행하세요.");
                    return;
                }
                WarnUnsavedTemplate(ed);
                var bundle = TemplateRepository.Resolve(BaseDirectory);
                if (bundle.Notice != null) ed.WriteMessage("\n" + bundle.Notice);
                if (bundle.IsRecovered) ed.WriteMessage("\n※ 템플릿 상태: " + bundle.Describe() + " — 템플릿을 고친 뒤 다시 저장하세요.");
                foreach (var warning in bundle.Warnings.Take(8)) ed.WriteMessage("\n  · 템플릿 주의: " + warning);
                if (bundle.Warnings.Count > 8) ed.WriteMessage("\n  · 그 밖의 주의 " + (bundle.Warnings.Count - 8) + "건은 템플릿 분석 보고서(analysis-report.txt)를 확인하세요.");
                var index = bundle.Index;

                IList<LegendBatchItem> items; int denominator = 100;
                // 이 명령이 쓸 설정 사본. 다른 AutoCAD 창이 도중에 설정 파일을 바꿔도 이번 생성은 이 값 그대로입니다.
                LegendSettings settings = LegendSettings.Load();
                if (showPicker)
                {
                    var request = ShowPicker(index, bundle.Notice);
                    if (request == null) return;
                    if (request.EditTemplate) { doc.SendStringToExecute("_.LEGENDTEMPLATE ", true, false, false); return; }
                    items = request.Items; denominator = request.ScaleDenominator;
                    if (request.Settings != null) settings = request.Settings; // 창에서 확정한 설정(저장 실패해도 이번 생성에 적용)
                }
                else
                {
                    string choices = string.Join("/", index.Categories.Select((c, i) => (i + 1) + c.Name));
                    int defaultNumber = Math.Max(1, index.Categories.FindIndex(c => c.Id == index.DefaultCategoryId) + 1);
                    var options = new PromptIntegerOptions("\n공종 번호 [" + choices + "] <" + defaultNumber + ">: ") { LowerLimit = 1, UpperLimit = index.Categories.Count, DefaultValue = defaultNumber, UseDefaultValue = true };
                    var chosen = ed.GetInteger(options); if (chosen.Status != PromptStatus.OK) return;
                    var manifest = index.LoadManifest(index.Categories[chosen.Value - 1]);
                    items = new[] { new LegendBatchItem(manifest, manifest.Rows.Select(r => new LegendSelection(r.Id, "-")).ToList(), index.SourcePath(manifest)) };
                    int start = settings.StartScale; // 설정 '마지막 축척으로 시작'을 명령줄 명령에도 똑같이 적용
                    var scale = ed.GetInteger(new PromptIntegerOptions("\n축척 분모 <" + start + "> (300=3배): ") { LowerLimit = LegendComposer.MinScale, UpperLimit = LegendComposer.MaxScale, DefaultValue = start, UseDefaultValue = true });
                    if (scale.Status != PromptStatus.OK) return; denominator = scale.Value;
                    if (settings.RememberScale && settings.LastScale != denominator)
                    {
                        var saved = settings.Clone(); saved.LastScale = denominator;
                        try { saved.Save(); } catch (System.Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                    }
                }
                var point = ed.GetPoint("\n범례 왼쪽 위 위치를 클릭하세요: "); if (point.Status != PromptStatus.OK) return;
                var ucs = ed.CurrentUserCoordinateSystem; var insertion = point.Value.TransformBy(ucs);
                LegendBatchResult result;
                // 도면 전체 REGEN은 하지 않습니다(큰 도면에서 수 초씩 멈춤). 새 객체는 트랜잭션 확정 때 바로 화면에 나타납니다.
                using (doc.LockDocument())
                    result = LegendComposer.ComposeBatch(doc.Database, items, insertion, ucs, denominator, original, !settings.OutputAsBlock, settings.CategoryGap, settings.Conflicts);
                ed.WriteMessage("\n범례 생성 완료: {0}개 공종, {1}개 항목{2}, 축척 1:{3} ({4:0.##}배). {5}",
                    items.Count, result.ItemCount, result.BlankRowCount > 0 ? " + 빈 행 " + result.BlankRowCount + "개" : "", denominator, result.ScaleFactor,
                    result.Exploded ? "블록이 아닌 개별 객체 " + result.EntityIds.Count + "개로 그렸습니다." : "블록 하나로 넣었습니다(고치려면 EXPLODE).");
                foreach (var warning in result.Warnings.Take(15)) ed.WriteMessage("\n  · 확인: " + warning);
                if (result.Warnings.Count > 15) ed.WriteMessage("\n  · 그 밖의 확인 사항 " + (result.Warnings.Count - 15) + "건");
                if (settings.SelectAfterCreate)
                {
                    try
                    {
                        ed.SetImpliedSelection(result.Exploded ? result.EntityIds.ToArray() : new[] { result.BlockReferenceId });
                        ed.WriteMessage("\n생성한 범례가 선택되어 있습니다. 바로 MOVE(M)로 옮길 수 있습니다.");
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n범례 생성 실패: {0}", ex.Message);
                if (ex.Data.Contains("cleanup"))
                    ed.WriteMessage("\n주의: {0}{1} 도면을 확인하고 필요하면 UNDO로 되돌리세요.", ex.Data["cleanup"],
                        ex.Data.Contains("remaining") ? " — 이번 범례에서 남은 객체·정의 " + ex.Data["remaining"] + "개." : " — 일부 객체가 남아 있을 수 있습니다.");
            }
        }

        [CommandMethod("LEGENDTEMPLATE", CommandFlags.Session)]
        public void OpenTemplate()
        {
            var docs = AcApplication.DocumentManager; var active = docs.MdiActiveDocument;
            string path = TemplatePath;
            try
            {
                if (!File.Exists(path)) { AcApplication.ShowAlertDialog("범례 템플릿을 찾을 수 없습니다:\n" + path); return; }
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                {
                    AcApplication.ShowAlertDialog("범례 템플릿이 읽기 전용이라 저장할 수 없습니다.\n파일 속성에서 '읽기 전용'을 해제한 뒤 다시 실행하세요.\n\n" + path); return;
                }
                var open = docs.Cast<Document>().FirstOrDefault(IsTemplate);
                if (open == null) open = DocumentCollectionExtension.Open(docs, path, false);
                docs.MdiActiveDocument = open;
                open.Editor.WriteMessage("\n범례 템플릿을 열었습니다: " + path + "\n수정 후 저장(Ctrl+S)하고 작업 도면에서 LEGEND를 실행하면 반영됩니다.");
                AcApplication.ShowAlertDialog(
                    "범례 템플릿을 열었습니다.\n\n" +
                    "[항목 추가 방법]\n" +
                    "1. 넣을 공종 표의 맨 아래 행을 COPY로 450 아래에 복사합니다.\n" +
                    "   (표에 비어 있는 마지막 행이 있으면 그 행을 바로 써도 됩니다.)\n" +
                    "2. 기호를 그리거나 바꾸고, 명칭·규격·단위·수량(-) 글자를 고칩니다.\n" +
                    "   글자는 해당 칸 안에, 기호는 '구분' 칸 안에 두세요.\n" +
                    "3. 테두리·바탕은 그대로 두어도 됩니다. 생성할 때 자동으로 맞춥니다.\n" +
                    "4. 저장(Ctrl+S)한 뒤 작업 도면에서 LEGEND를 실행하면 새 항목이 목록에 나타납니다.\n\n" +
                    "※ 표의 열 구분선·제목 줄과 레이어 이름(C-공종-범례…)은 바꾸지 마세요.");
            }
            catch (System.Exception ex)
            {
                if (active != null) active.Editor.WriteMessage("\n템플릿을 열지 못했습니다: " + ex.Message);
                else AcApplication.ShowAlertDialog("템플릿을 열지 못했습니다: " + ex.Message);
            }
        }

        private static bool IsTemplate(Document document)
        {
            try { return document != null && !string.IsNullOrEmpty(document.Name) && string.Equals(Path.GetFullPath(document.Name), TemplatePath, StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; } catch (NotSupportedException) { return false; }
        }

        /// <summary>템플릿 도면이 열려 있고 저장하지 않은 변경이 있으면 알려줍니다(저장해야 반영됨).</summary>
        private static void WarnUnsavedTemplate(Editor ed)
        {
            foreach (Document document in AcCoreApplication.DocumentManager)
            {
                if (!IsTemplate(document)) continue;
                try
                {
                    if (!IsSaved(document)) ed.WriteMessage("\n※ 열려 있는 범례 템플릿에 저장하지 않은 변경이 있습니다. 템플릿을 저장해야 새 항목이 반영됩니다.");
                }
                catch (System.Exception) { } // AutoCAD Core Console 등 COM이 없는 환경
            }
        }

        // AcMgd(COM) 참조를 이 메서드에만 두어 Core Console에서도 LEGENDALL이 동작하게 합니다.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool IsSaved(Document document)
        {
            var com = DocumentExtension.GetAcadDocument(document);
            var saved = com.GetType().InvokeMember("Saved", BindingFlags.GetProperty, null, com, null);
            return !(saved is bool) || (bool)saved;
        }

        private sealed class Request { public IList<LegendBatchItem> Items; public int ScaleDenominator; public bool EditTemplate; public LegendSettings Settings; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Request ShowPicker(CatalogIndex index, string notice)
        {
            using (var form = new MultiLegendForm(index, null, notice))
            {
                // 마지막 창 크기를 기억하지 않습니다(기억하면 예전 큰 창 크기로 계속 열림). 항상 기본 크기로 화면 가운데에 엽니다.
                var answer = AcApplication.ShowModalDialog(null, form, false);
                if (form.EditTemplateRequested) return new Request { EditTemplate = true };
                if (answer != System.Windows.Forms.DialogResult.OK) return null;
                return new Request { Items = form.BatchSelection, ScaleDenominator = form.ScaleDenominator, Settings = form.ConfirmedSettings };
            }
        }
    }
}
