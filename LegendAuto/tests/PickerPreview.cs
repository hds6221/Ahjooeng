using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using StormLegend;

/// <summary>AutoCAD 없이 숨긴 창으로 LEGEND 화면을 검사하고 화면 이미지를 저장합니다(verify-picker.ps1).</summary>
public static class PickerPreview
{
    private static readonly List<string> log = new List<string>();
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); log.Add("PASS: " + name); }

    [STAThread] public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { string file = new AssemblyName(e.Name).Name + ".dll"; string path = Path.Combine(args[0], file); if (!File.Exists(path)) path = Path.Combine(args.Length > 3 ? args[3] : @"C:\Program Files\Autodesk\AutoCAD 2020", file); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        try { Run(args); } catch (Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "picker-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }

    private static T Find<T>(Control root, string name) where T : Control { return (T)root.Controls.Find(name, true)[0]; }
    private static void Click(Control root, string name) { Find<Button>(root, name).PerformClick(); Application.DoEvents(); }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string[] args)
    {
        Application.EnableVisualStyles(); Directory.CreateDirectory(args[1]); Directory.CreateDirectory(args[2]);
        if (args.Length > 4) { helpDir = args[4]; Directory.CreateDirectory(helpDir); }
        var index = CatalogIndex.Load(Path.Combine(args[0], "Template", CatalogIndex.FileName)); Check(index.Categories.Count == 7 && index.Categories.Sum(c => c.ItemCount) == 94, "7 categories and 94 items");
        var watch = Stopwatch.StartNew();
        using (var form = new MultiLegendForm(index, args[2]))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            log.Add("INFO: LEGEND window ready in " + watch.ElapsedMilliseconds + " ms (preview data read once, category screens built on first use)");
            File.WriteAllText(Path.Combine(args[1], "ui-environment.txt"), "WinForms preview DeviceDpi=" + form.DeviceDpi + "; normal size=" + form.Size + "; minimum size=" + form.MinimumSize + ".");
            Check(form.ActivePicker.Model.Count == 22, "default storm has 22 items");
            Check(form.BackColor.GetBrightness() < .3F && form.ActivePicker.BackColor.GetBrightness() < .3F, "dark tool-window background");
            Check(Find<Button>(form, "EditTemplate").Visible, "template edit button is on the main window");
            Check(form.ClientSize.Width <= 1140 && form.ClientSize.Height <= 600, "window 1120x585: " + form.ClientSize);
            var screen = Screen.FromControl(form).WorkingArea;
            Check(Math.Abs(form.Left + form.Width / 2 - (screen.Left + screen.Width / 2)) <= 2 && Math.Abs(form.Top + form.Height / 2 - (screen.Top + screen.Height / 2)) <= 2, "window opens in the middle of the screen: " + form.Bounds + " in " + screen);
            Check(Find<CadUi.CadTabStrip>(form, "TabStrip").Tabs.Count == 7, "category tab strip");
            Check(new[] { "OpenPreset", "SaveDefault", "ClearSelection", "RemoveRow", "MoveUp", "MoveDown", "AddBlankRow" }.All(n => Find<Button>(form, n).Text.Length > 0), "every toolbar button shows a text label (불러오기·저장·비우기·빼기·위로·아래로·빈 행 추가)");
            Check(form.Controls.Find("ResetLabels", true).Length == 0 && form.Controls.Find("AddBlankEnd", true).Length == 0, "toolbar keeps only essential buttons");
            Capture(form, Path.Combine(args[1], "picker-dark-main.png"));
            foreach (var entry in index.Categories)
            {
                form.SelectCategory(entry.Id); Application.DoEvents(); var picker = form.ActivePicker; var m = index.LoadManifest(entry);
                Check(picker.Model.Count == (entry.Id == index.DefaultCategoryId ? entry.ItemCount : 0), entry.Id + " intentional initial selection"); picker.Model.SelectAll(); Application.DoEvents();
                var grid = Find<DataGridView>(picker, "SelectedItems"); Check(grid.Columns["Quantity"].Visible == m.HasQuantityColumn, entry.Id + " quantity policy");
                Check(grid.Rows.Count == m.Rows.Count, entry.Id + " grid lists every selected row");
                Check(grid.Rows.Cast<DataGridViewRow>().All(row => new[] { "Facility", "Spec", "Quantity" }.All(col => Contrast(row.Cells[col].InheritedStyle.ForeColor, row.Cells[col].InheritedStyle.BackColor) >= 4.5)), entry.Id + " every text cell is readable (contrast >= 4.5)");
                Check(grid.Columns["Facility"].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleLeft && grid.Columns["Spec"].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleLeft && grid.Columns["Quantity"].DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight, entry.Id + " names/specs left, quantities right");
                Check(Contrast(grid.DefaultCellStyle.SelectionForeColor, grid.DefaultCellStyle.SelectionBackColor) >= 4.5, entry.Id + " selected cell contrast");
                Check(Find<Button>(form, "AddBlankRow").Enabled == m.SupportsBlankRows, entry.Id + " blank row button policy");
                if (entry.Id == "paving")
                {
                    grid.CurrentCell = grid.Rows[0].Cells["Facility"]; grid.BeginEdit(true); ((DataGridViewTextBoxEditingControl)grid.EditingControl).Text = "프로젝트 아스콘포장"; Check(grid.EndEdit(), "name edit commits");
                    grid.CurrentCell = grid.Rows[0].Cells["Spec"]; grid.BeginEdit(true); ((DataGridViewTextBoxEditingControl)grid.EditingControl).Text = "T=600"; Check(grid.EndEdit(), "spec edit commits");
                    Check(picker.Model.Selection[0].NameOverride == "프로젝트 아스콘포장" && picker.Model.Selection[0].SpecOverride == "T=600", "name/spec edits reach model");
                    new PresetStore(m, Path.Combine(args[2], "edited-paving.json")).SaveDefault("프로젝트", picker.Model.Selection);
                    Check(new PresetStore(m, Path.Combine(args[2], "edited-paving.json")).Load().Presets[0].Items[0].SpecOverride == "T=600", "edited labels persist");
                    Capture(form, Path.Combine(args[1], "picker-paving-edited.png"));
                    Annotate(form, "help-edit.png", grid.Parent.Parent, grid, 1);
                    grid.CurrentCell = grid.Rows[0].Cells["Facility"]; picker.ResetCurrent(); Application.DoEvents();
                    Check(picker.Model.Selection[0].NameOverride == null && picker.Model.Selection[0].SpecOverride == null, "reset button restores original name/spec");
                }
                if (entry.Id == "storm")
                {
                    grid.CurrentCell = grid.Rows[2].Cells["Facility"]; grid.BeginEdit(true); picker.AddBlankAfter(); Application.DoEvents();
                    Check(!grid.IsCurrentCellInEditMode && grid.CurrentRow.Index == 3, "blank-after while a cell is being edited: no reentrancy error, new blank row selected");
                    Check(picker.Model.Selection[3].IsBlank && grid.Rows[3].Cells["Facility"].Value.Equals("(빈 행)") && grid.Rows[3].ReadOnly, "blank row inserted below the chosen row (middle)");
                    picker.AddBlankEnd(); Application.DoEvents(); Check(picker.Model.Selection.Last().IsBlank && picker.Model.BlankCount == 2, "blank row added at the bottom");
                    string blank = picker.Model.Selection[3].ItemId; Click(form, "MoveUp"); Check(!picker.Model.Selection.Last().IsBlank && picker.Model.Selection[picker.Model.RowCount - 2].IsBlank, "move up shifts the current (bottom blank) row");
                    grid.CurrentCell = grid.Rows[3].Cells["Facility"]; Click(form, "MoveDown"); Check(picker.Model.Selection[4].ItemId == blank, "move down shifts the blank row");
                    Capture(form, Path.Combine(args[1], "picker-blank-rows.png"));
                    Annotate(form, "help-blank.png", null, Find<Control>(form, "AddBlankRow"), 1, grid, 2);
                    Check(Find<Label>(form, "Summary").Text.Contains("빈 행 2"), "summary counts blank rows");
                    foreach (var id in picker.Model.Selection.Where(x => x.IsBlank).Select(x => x.ItemId).ToList())
                    { grid.CurrentCell = grid.Rows.Cast<DataGridViewRow>().Single(r => (string)r.Tag == id).Cells["Facility"]; Click(form, "RemoveRow"); }
                    Check(picker.Model.BlankCount == 0 && picker.Model.Count == 22, "blank rows removed");
                }
                var compact = Find<FlowLayoutPanel>(picker, "Cards");
                Check(compact.Controls.Cast<Control>().All(c => c.Height >= 32 && c.Height <= 36), entry.Id + " list rows tall enough for a readable symbol (32~36px)");
                if (entry.Id == "storm")
                {
                    // 기호 크게 보기: 마우스를 올리면 포커스를 뺏지 않는 큰 그림 창이 뜨고, 벗어나면 닫힘
                    var card = compact.Controls.Cast<Control>().First(); var hover = typeof(Control).GetMethod("OnMouseEnter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var active = Form.ActiveForm;
                    hover.Invoke(card, new object[] { EventArgs.Empty });
                    // 300ms 타이머 뒤에 뜹니다. PC가 바쁠 수 있어 최대 3초까지 기다립니다.
                    Form zoomWindow = null; var wait = DateTime.Now;
                    while (zoomWindow == null && (DateTime.Now - wait).TotalMilliseconds < 3000)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(20); zoomWindow = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Name == "SymbolZoom" && f.Visible); }
                    Check(zoomWindow != null && zoomWindow.Width >= 300 && zoomWindow.Height >= 180, "hovering a symbol opens the large symbol view");
                    Check(zoomWindow == null || (Form.ActiveForm != zoomWindow && !zoomWindow.ContainsFocus), "large symbol view does not take focus");
                    if (zoomWindow != null)
                    {
                        Capture(zoomWindow, Path.Combine(args[1], "picker-symbol-zoom.png"));
                        using (var image = new Bitmap(zoomWindow.Width, zoomWindow.Height))
                        {
                            zoomWindow.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            int ink = 0; for (int y = 12; y < 140; y += 2) for (int x = 12; x < 308; x += 2) { var c = image.GetPixel(x, y); if (c.R + c.G + c.B < 600) ink++; }
                            Check(ink > 60, "large symbol view draws the symbol (" + ink + " dark samples)");
                        }
                    }
                    typeof(Control).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(card, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                    Check(!Application.OpenForms.Cast<Form>().Any(f => f.Name == "SymbolZoom" && f.Visible), "large symbol view closes when the mouse leaves");
                    var symbolCell = grid.Rows.Count > 0 ? grid.Rows[0].Cells["Symbol"].Value as Bitmap : null;
                    Check(symbolCell != null && symbolCell.Width >= 70 && symbolCell.Height >= 24 && grid.Rows[0].Height >= 32, "selected-items symbols are drawn larger (" + (symbolCell == null ? "none" : symbolCell.Width + "x" + symbolCell.Height) + ")");
                }
                if (entry.Id == "auxiliary")
                {
                    Check(compact.Controls.Cast<Control>().Select(c => c.Left).Distinct().Count() == 1 && compact.Controls.Cast<Control>().All(c => c.Height >= 24), "auxiliary 34 items listed one per line in a single column (scrolls when needed)"); Capture(form, Path.Combine(args[1], "picker-compact-auxiliary.png"));
                    var previous = form.Size; form.Size = form.MinimumSize; Application.DoEvents(); Capture(form, Path.Combine(args[1], "picker-minimum.png"));
                    Check(compact.Controls.Cast<Control>().All(c => c.Right <= compact.ClientSize.Width), "rows fit the list width at minimum window size");
                    form.Size = previous; Application.DoEvents();
                }
                Capture(form, Path.Combine(args[1], "picker-" + entry.Id + ".png"));
                picker.ApplyPreset(picker.State.Presets[1]); Application.DoEvents();
                Check(picker.Model.Count == Math.Min(6, m.Rows.Count), entry.Id + " sample preset");
                var store = new PresetStore(m, Path.Combine(args[2], entry.Id + ".json")); store.SaveDefault("내 구성", picker.Model.Selection); store.SaveDefault("내 구성", picker.Model.Selection); Check(store.Load().Presets.Count == 1, entry.Id + " atomic preset update");
                string first = m.Rows[0].Id; Click(picker, "Card_" + first); Check(!picker.Model.Contains(first), entry.Id + " card removes"); Click(picker, "Card_" + first);
                Check(picker.Model.Selection[0].ItemId == first, entry.Id + " re-added item returns to its template position");
                if (m.HasQuantityColumn)
                {
                    int n = grid.Rows.Cast<DataGridViewRow>().Single(r => (string)r.Tag == first).Index; grid.CurrentCell = grid.Rows[n].Cells["Quantity"]; grid.BeginEdit(true);
                    Check(Contrast(grid.EditingControl.ForeColor, grid.EditingControl.BackColor) >= 4.5, entry.Id + " quantity editor contrast");
                    ((DataGridViewTextBoxEditingControl)grid.EditingControl).Text = "12.5";
                    if (entry.Id == "storm") Capture(form, Path.Combine(args[1], "picker-dark-editing.png"));
                    Check(grid.EndEdit() && picker.Model.Selection.Single(x => x.ItemId == first).Quantity == "12.5", entry.Id + " quantity editing");
                }
                var before = picker.Model.Count; bool rejected = false; try { picker.Model.Apply(new[] { new PresetItem { ItemId = "unknown", Quantity = "-" } }); } catch (ArgumentException) { rejected = true; } Check(rejected && picker.Model.Count == before, entry.Id + " invalid preset changes nothing");
            }
            form.SelectCategory("storm"); Application.DoEvents(); Check(form.ActivePicker.Model.Count == 6 && form.ActivePicker.Model.Selection[0].Quantity == "12.5", "category switch preserves selection and quantity");
            var storm = index.Manifest("storm");
            var search = Find<TextBox>(form.ActivePicker, "SearchBox");
            Check(search.BorderStyle == BorderStyle.None && search.Parent is CadUi.CadTextField && search.Bottom < search.Parent.Height - 1, "search box: inner text has no own border and fits inside the frame (frame drawn on all four sides)"); search.Text = "D450"; Application.DoEvents(); var cards = Find<FlowLayoutPanel>(form.ActivePicker, "Cards");
            Check(cards.Controls.Cast<Control>().Count(c => c.Visible) > 0 && cards.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Text.Contains("D450")), "search matches source specification"); search.Text = "";
            var keyboard = Find<Button>(form.ActivePicker, "Card_" + storm.Rows[6].Id); bool picked = form.ActivePicker.Model.Contains(storm.Rows[6].Id); keyboard.Focus();
            typeof(Button).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(keyboard, new object[] { new KeyEventArgs(Keys.Space) });
            typeof(Button).GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(keyboard, new object[] { new KeyEventArgs(Keys.Space) });
            Check(form.ActivePicker.Model.Contains(storm.Rows[6].Id) != picked, "space key toggles focused row"); keyboard.PerformClick();
            Capture(form, Path.Combine(args[1], "picker-dark-focused.png"));
            Annotate(form, "help-window.png", null,
                Find<Control>(form, "TabStrip"), 1, Find<Control>(form.ActivePicker, "Cards").Parent.Parent, 2, Find<Control>(form.ActivePicker, "SelectedItems").Parent.Parent, 3,
                Find<Control>(form.ActivePicker, "LegendPreview").Parent.Parent, 4, Find<Control>(form, "ScaleDenominator"), 5, Find<Control>(form, "GenerateBatch"), 6);
            Annotate(form, "help-toolbar.png", Find<Control>(form, "Toolbar"),
                Find<Control>(form, "ScaleDenominator"), 1, Find<Control>(form, "OpenPreset"), 2, Find<Control>(form, "SaveDefault"), 3, Find<Control>(form, "ClearSelection"), 4,
                Find<Control>(form, "RemoveRow"), 5, Find<Control>(form, "MoveUp"), 6, Find<Control>(form, "MoveDown"), 7, Find<Control>(form, "AddBlankRow"), 8);
            Annotate(form, "help-footer.png", Find<Control>(form, "Footer"),
                Find<Control>(form, "EditTemplate"), 1, Find<Control>(form, "Settings"), 2, Find<Control>(form, "Help"), 3, Find<Control>(form, "GenerateBatch"), 4);
            var scale = Find<Control>(form, "ScaleDenominator"); var generate = Find<Button>(form, "GenerateBatch");
            Check(scale.Text == "1:100", "default scale 1:100"); scale.Text = "0"; Check(!generate.Enabled, "zero scale rejected"); scale.Text = "abc"; Check(!generate.Enabled, "non-numeric scale rejected");
            scale.Text = "1:300"; Check(generate.Enabled && Find<Label>(form, "Summary").Text.Contains("3배"), "scale 1:300 shows three times"); scale.Text = "300"; Check(generate.Enabled && Find<Label>(form, "Summary").Text.Contains("1:300"), "plain 300 also accepted");
            Capture(form, Path.Combine(args[1], "picker-batch-scale300.png"));
            generate.PerformClick(); Check(form.DialogResult == DialogResult.OK && form.BatchSelection.Count == 7 && form.BatchSelection.Sum(i => i.Selection.Count) == 37 && form.ScaleDenominator == 300, "one confirmation returns seven categories and scale 300");
        }
        watch.Restart();
        using (var form = new MultiLegendForm(index, args[2]))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents(); Check(form.ActivePicker.Model.Count == 6, "saved preset restores per category");
            log.Add("INFO: second LEGEND window ready in " + watch.ElapsedMilliseconds + " ms (preview data reused from cache)");
            Check(form.Settings.CategoryGap == 300 && !form.Settings.OutputAsBlock && !form.Settings.RememberScale && form.Settings.SelectAfterCreate, "default settings");
            var changed = form.Settings.Clone(); changed.OutputAsBlock = true; changed.CategoryGap = 600; changed.RememberScale = true; form.ApplySettings(changed);
            Check(LegendSettings.Load(Path.Combine(args[2], "settings.json")).CategoryGap == 600 && LegendSettings.Load(Path.Combine(args[2], "settings.json")).OutputAsBlock, "settings are saved to file");
            using (var dialog = new SettingsForm(form.Settings))
            {
                dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
                Check(((RadioButton)dialog.Controls.Find("OutputBlock", true)[0]).Checked && ((CadUi.CadNumberField)dialog.Controls.Find("CategoryGap", true)[0]).Value == 600, "settings window shows current values");
                dialog.Opacity = 1; Capture(dialog, Path.Combine(args[1], "settings-window.png"));
                Annotate(dialog, "help-settings.png", null, Find<Control>(dialog, "OutputExploded"), 1, Find<Control>(dialog, "CategoryGap"), 2, Find<Control>(dialog, "RememberScale"), 3, Find<Control>(dialog, "SelectAfterCreate"), 4);
                ((Button)dialog.Controls.Find("ResetSettings", true)[0]).PerformClick();
                Check(((RadioButton)dialog.Controls.Find("OutputExploded", true)[0]).Checked && ((CadUi.CadNumberField)dialog.Controls.Find("CategoryGap", true)[0]).Value == 300, "settings 'default' button restores defaults");
            }
            var grid = Find<DataGridView>(form.ActivePicker, "SelectedItems"); grid.CurrentCell = grid.Rows[0].Cells["Quantity"]; grid.BeginEdit(true); ((DataGridViewTextBoxEditingControl)grid.EditingControl).Text = "-1";
            form.SelectCategory("construction"); Check(form.ActiveCategoryId == "storm" && Find<Label>(form, "StatusMessage").Text.Contains("수량"), "invalid edit blocks category switch and explains why");
            Capture(form, Path.Combine(args[1], "picker-invalid-quantity.png"));
            Click(form, "CancelWindow"); Check(form.DialogResult == DialogResult.Cancel && form.BatchSelection == null, "cancel during invalid editing");
        }
        using (var form = new MultiLegendForm(index, args[2]))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            var scale = Find<Control>(form, "ScaleDenominator"); scale.Text = "1:250"; Application.DoEvents(); Click(form, "GenerateBatch");
            Check(form.DialogResult == DialogResult.OK && LegendSettings.Load(Path.Combine(args[2], "settings.json")).LastScale == 250, "remember-scale setting stores the last scale");
        }
        using (var form = new MultiLegendForm(index, args[2]))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            Check(Find<Control>(form, "ScaleDenominator").Text == "1:250", "next window starts at the remembered scale");
            var reset = new LegendSettings(); form.ApplySettings(reset);
        }
        using (var form = new MultiLegendForm(index, Path.Combine(args[2], "empty-case")))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            form.ClearAll(); Application.DoEvents(); Check(!Find<Button>(form, "GenerateBatch").Enabled, "all-empty batch disables generation");
            var emptyPreview = Find<Control>(form.ActivePicker, "LegendPreview");
            Check(emptyPreview.Controls.Cast<Control>().All(c => c.Left >= 0 && c.Right <= emptyPreview.ClientSize.Width && c.Bottom <= emptyPreview.ClientSize.Height), "empty preview stays inside its viewport");
            Capture(form, Path.Combine(args[1], "picker-dark-disabled.png"));
            form.SelectCategory("paving"); var m = index.Manifest("paving"); form.ActivePicker.Model.Toggle(m.Rows[0].Id);
            form.ActivePicker.Model.SetLabels(m.Rows[0].Id, "프로젝트 포장", "T=600");
            Application.DoEvents(); form.SelectCategory("storm"); Check(Find<Button>(form, "GenerateBatch").Enabled, "inactive category selection still enables generation");
            Click(form, "GenerateBatch"); Check(form.BatchSelection.Count == 1 && form.BatchSelection[0].Manifest.CategoryId == "paving" && form.BatchSelection[0].Selection[0].SpecOverride == "T=600", "empty categories omitted, edited selection kept");
        }
        // 검토 4: 설정 파일 저장이 실패해도(잠김·권한) 확정한 설정이 이번 생성 요청에 그대로 들어감
        string blockedDir = Path.Combine(args[2], "blocked"); Directory.CreateDirectory(blockedDir);
        string blockedSettings = Path.Combine(blockedDir, "settings.json"); Directory.CreateDirectory(blockedSettings); // 같은 이름 폴더 → 파일 저장 불가
        using (var form = new MultiLegendForm(index, args[2], null, blockedSettings))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            var wanted = form.Settings.Clone(); wanted.OutputAsBlock = true; wanted.CategoryGap = 750; wanted.SelectAfterCreate = false; wanted.Conflicts = DefinitionConflict.Separate;
            form.ApplySettings(wanted);
            Check(Find<Label>(form, "StatusMessage").Text.Contains("저장하지 못했습니다") && Find<Label>(form, "StatusMessage").Text.Contains("이번 생성에는"), "설정 저장 실패 안내: 이번 생성에는 적용");
            Click(form, "GenerateBatch");
            var confirmed = form.ConfirmedSettings;
            Check(form.DialogResult == DialogResult.OK && confirmed != null && confirmed.OutputAsBlock && confirmed.CategoryGap == 750 && !confirmed.SelectAfterCreate && confirmed.Conflicts == DefinitionConflict.Separate, "저장 실패 후 즉시 생성: 확정한 설정 사본이 생성 요청에 포함");
            wanted.CategoryGap = 100; Check(confirmed.CategoryGap == 750, "확정 뒤 설정을 바꿔도(다른 프로세스 변경 등) 요청 사본은 그대로");
        }

        // 검토 5: 미리보기 데이터가 망가져도 선택창은 열리고 항목 선택·생성은 됨
        string brokenTemplate = Path.Combine(args[2], "broken-preview", "Template"); Directory.CreateDirectory(Path.Combine(brokenTemplate, "categories"));
        foreach (var file in Directory.GetFiles(Path.Combine(args[0], "Template"), "*.json", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(brokenTemplate, file.Substring(Path.Combine(args[0], "Template").Length + 1)), true);
        File.WriteAllText(Path.Combine(brokenTemplate, CatalogIndex.PreviewFileName), "{ \"schemaVersion\": 1, \"entities\": [ {\"handle\": null} ] }");
        var brokenIndex = CatalogIndex.Load(Path.Combine(brokenTemplate, CatalogIndex.FileName));
        using (var form = new MultiLegendForm(brokenIndex, Path.Combine(args[2], "broken-preview-presets")))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            Check(Find<Label>(form, "StatusMessage").Text.Contains("미리보기") && Find<Label>(form, "StatusMessage").Text.Contains("그대로 할 수 있습니다"), "미리보기 손상: 비활성 상태와 원인을 표시");
            Check(form.ActivePicker.Model.Count == 22 && Find<Button>(form, "GenerateBatch").Enabled, "미리보기 손상: 항목 선택·생성은 가능");
            Capture(form, Path.Combine(args[1], "picker-preview-unavailable.png"));
            Click(form, "GenerateBatch"); Check(form.DialogResult == DialogResult.OK && form.BatchSelection.Count == 1, "미리보기 손상: 생성 요청 정상");
        }

        var a = index.Manifest("construction"); var b = index.Manifest("storm"); string collision = Path.Combine(args[2], "collision.json"); new PresetStore(a, collision).SaveDefault("공사", a.Rows.Select(r => new LegendSelection(r.Id, "-")).ToList()); bool mismatch = false; try { new PresetStore(b, collision).Load(); } catch (InvalidDataException) { mismatch = true; } Check(mismatch, "category presets cannot cross load");
        // 수정 회귀: 빈 문자열, 아이콘 이름, 종료 직전 예약된 UI 갱신
        for (int i = 0; i < 8; i++)
        using (var form = new MultiLegendForm(index, Path.Combine(args[2], "close-" + i)))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            var picker = form.ActivePicker;
            var id = picker.Manifest.Rows[0].Id;
            picker.Model.SetLabels(id, "", ""); Application.DoEvents();
            Check(picker.Model.Selection.Single(x => x.ItemId == id).NameOverride == "", "empty label remains intentionally empty " + i);
            Check(new[] { "ZoomOut", "ZoomIn", "FitPreview" }.All(n => !string.IsNullOrWhiteSpace(Find<Button>(picker, n).AccessibleName)), "preview icon accessibility " + i);
            var grid = Find<DataGridView>(picker, "SelectedItems");
            grid.CurrentCell = grid.Rows[0].Cells["Facility"]; grid.BeginEdit(true);
            ((DataGridViewTextBoxEditingControl)grid.EditingControl).Text = "닫기 전 수정";
            if (i % 2 == 0) { form.SelectCategory("paving"); form.SelectCategory("storm"); }
            else picker.CancelEdits();
            picker.Model.Toggle(picker.Manifest.Rows[1].Id); // 예약 갱신을 남긴 채 닫기
            form.Close();
        }
        Application.DoEvents();
        Check(true, "pending refresh after edit/tab/cancel/close does not throw");
        string emptyStart = Path.Combine(args[2], "default-off"); Directory.CreateDirectory(emptyStart);
        new PresetStore(index.Manifest("storm"), Path.Combine(emptyStart, "storm.json")).ClearDefault();
        using (var form = new MultiLegendForm(index, emptyStart)) Check(form.ModelOf("storm").Count == 0, "cleared default opens with empty selection");
        // 미리보기의 해치 구멍: 내부는 흰색, 외부는 채움이어야 합니다.
        string hatchFile = Path.Combine(args[2], "hatch-preview.json");
        var previewData = PreviewCatalogFile.Load(index.PreviewPath);
        var hatchRow = index.Manifest("storm").Rows.First(r => r.SymbolHandles.Count == 1);
        Func<double, double, PreviewPoint> point = (x, y) => new PreviewPoint { X = x, Y = y };
        var outer = new List<PreviewPoint> { point(0,0), point(100,0), point(100,100), point(0,100) };
        var inner = new List<PreviewPoint> { point(30,30), point(70,30), point(70,70), point(30,70) };
        previewData.Entities.Single(e => e.Handle == hatchRow.SymbolHandles[0]).Primitives = new List<PreviewPrimitive> {
            new PreviewPrimitive { Kind = "polyline", Color = "#000000", Closed = true, Fill = "solid", Points = outer, Contours = new List<List<PreviewPoint>> { outer, inner } } };
        previewData.Save(hatchFile);
        using (var image = PreviewCatalog.Load(hatchFile, index.Manifests).RenderSymbol(hatchRow, 120, 120))
        {
            Check(image.GetPixel(60,60).GetBrightness() > .9 && image.GetPixel(25,60).GetBrightness() < .2, "hatch island remains a hole");
            image.Save(Path.Combine(args[1], "review-hatch-hole.png"));
        }
        // 해치 패턴 미리보기: 실제 패턴 선 정보가 있으면 각도·간격대로 선을 그림(예전: 항상 같은 사선)
        var patterned = new PreviewPrimitive { Kind = "polyline", Color = "#000000", Closed = true, Fill = "hatch", Points = outer, Contours = new List<List<PreviewPoint>> { outer },
            Pattern = new List<PreviewHatchLine> { new PreviewHatchLine { Angle = 0, X = 0, Y = 0, OffsetX = 0, OffsetY = 20 } } };
        previewData.Entities.Single(e => e.Handle == hatchRow.SymbolHandles[0]).Primitives = new List<PreviewPrimitive> { patterned };
        previewData.Save(hatchFile);
        using (var image = PreviewCatalog.Load(hatchFile, index.Manifests).RenderSymbol(hatchRow, 120, 120))
        {
            // 0° 패턴(가로선)만 있어야 함: 세로 방향으로 어두운 줄이 반복, 한 줄 안에서는 가로로 이어짐
            int dark = Enumerable.Range(12, 96).Count(y => image.GetPixel(60, y).GetBrightness() < .5);
            int rows = Enumerable.Range(13, 95).Count(y => image.GetPixel(60, y).GetBrightness() < .5 && image.GetPixel(60, y - 1).GetBrightness() >= .5);
            int diagonal = Enumerable.Range(20, 80).Count(i => image.GetPixel(i, i).GetBrightness() < .5);
            Check(rows >= 3 && dark < 60 && diagonal < 30, "hatch preview draws the real pattern (horizontal lines: " + rows + " rows, " + dark + " dark px; diagonal px " + diagonal + ")");
            image.Save(Path.Combine(args[1], "review-hatch-pattern.png"));
        }
        ScaledLayout(index, args);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name.StartsWith("StormLegend"))) log.Add("LOADED: " + assembly.GetName().Name + " " + assembly.Location);
        log.Add("SUCCESS: " + log.Count(l => l.StartsWith("PASS")) + " picker checks."); File.WriteAllLines(Path.Combine(args[1], "picker-verification.txt"), log);
    }

    /// <summary>
    /// 화면 배율 125~200% 모의 검사: 창 크기와 모든 글꼴을 배율만큼 키운 뒤 버튼 글자가 잘리지 않는지,
    /// 컨트롤이 부모 밖으로 나가지 않는지, 도구 막대 버튼이 겹치지 않는지 확인합니다.
    /// (이 PC는 96 DPI라 실제 DeviceDpi 변화는 재현하지 못합니다 — 실제 배율은 별도 수동 확인.)
    /// </summary>
    private static void ScaledLayout(CatalogIndex index, string[] args)
    {
        foreach (float factor in new[] { 1.25F, 1.5F, 1.75F, 2F })
        {
            var fonts = new List<Font>();
            using (var form = new MultiLegendForm(index, args[2]))
            {
                form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                form.SuspendLayout();
                form.Scale(new SizeF(factor, factor));
                ScaleFonts(form, factor, fonts);
                form.ResumeLayout(true); form.PerformLayout(); Application.DoEvents();
                var clipped = new List<string>(); var outside = new List<string>(); var overlaps = new List<string>();
                foreach (var control in All(form).Where(c => c.Visible && c.Width > 2 && c.Height > 2))
                {
                    var button = control as Button;
                    if (button != null && !button.AutoSize && button.Text.Length > 0)
                    {
                        int text = TextRenderer.MeasureText(button.Text.Replace("▾", "").Trim(), button.Font).Width;
                        if (text > button.Width) clipped.Add(button.Name + "(" + text + ">" + button.Width + ")");
                    }
                    var parent = control.Parent;
                    if (parent == null || parent is Form || parent is FlowLayoutPanel && ((FlowLayoutPanel)parent).AutoScroll || parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll || control.Name == "CancelWindow") continue;
                    var area = parent.ClientRectangle;
                    if (control.Left < area.Left - 1 || control.Top < area.Top - 1 || control.Right > area.Right + 1 || control.Bottom > area.Bottom + 1)
                        outside.Add((control.Name.Length > 0 ? control.Name : control.GetType().Name) + control.Bounds + " in " + (parent.Name.Length > 0 ? parent.Name : parent.GetType().Name) + area);
                }
                foreach (var grid in All(form).OfType<DataGridView>().Where(g => g.Visible))
                {
                    int need = TextRenderer.MeasureText("가Ag", grid.DefaultCellStyle.Font ?? grid.Font).Height + 4;
                    int needHeader = TextRenderer.MeasureText("가Ag", grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Height + 4;
                    if (grid.RowTemplate.Height < need || grid.Rows.Cast<DataGridViewRow>().Any(r => r.Height < need)) clipped.Add(grid.Name + " rows " + grid.RowTemplate.Height + "<" + need);
                    if (grid.ColumnHeadersVisible && grid.ColumnHeadersHeight < needHeader) clipped.Add(grid.Name + " header " + grid.ColumnHeadersHeight + "<" + needHeader + " [grid " + grid.Font.SizeInPoints + "pt, header style " + (grid.ColumnHeadersDefaultCellStyle.Font == null ? "null" : grid.ColumnHeadersDefaultCellStyle.Font.SizeInPoints + "pt") + ", row " + grid.RowTemplate.Height + "]");
                }
                var toolbar = Find<FlowLayoutPanel>(form, "Toolbar").Controls.Cast<Control>().Where(c => c.Visible).ToList();
                for (int i = 0; i < toolbar.Count; i++) for (int j = i + 1; j < toolbar.Count; j++) if (toolbar[i].Bounds.IntersectsWith(toolbar[j].Bounds)) overlaps.Add(toolbar[i].Name + "/" + toolbar[j].Name);
                Check(clipped.Count == 0, "scaled " + (int)(factor * 100) + "%: button labels and table rows/headers fit their text (" + string.Join(", ", clipped.Take(6)) + ")");
                Check(outside.Count == 0, "scaled " + (int)(factor * 100) + "%: controls stay inside their parents (" + string.Join(", ", outside.Take(6)) + ")");
                Check(overlaps.Count == 0, "scaled " + (int)(factor * 100) + "%: toolbar buttons do not overlap (" + string.Join(", ", overlaps.Take(6)) + ")");
                Capture(form, Path.Combine(args[1], "picker-scaled-" + (int)(factor * 100) + ".png"));
                form.Close();
            }
            foreach (var font in fonts) font.Dispose();
        }
    }

    private static IEnumerable<Control> All(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (var d in All(c)) yield return d; } }

    private static void ScaleFonts(Control root, float factor, List<Font> fonts)
    {
        // 부모 글꼴을 먼저 바꾸면 자식이 물려받은 글꼴을 다시 키우게 되므로, 원래 글꼴을 먼저 모두 기록합니다.
        var original = new[] { root }.Concat(All(root)).Select(c => new { Control = c, Font = c.Font }).ToList();
        foreach (var item in original)
        {
            var font = new Font(item.Font.FontFamily, item.Font.SizeInPoints * factor, item.Font.Style, GraphicsUnit.Point); fonts.Add(font);
            item.Control.Font = font;
        }
    }

    private static double Luminance(Color c)
    {
        Func<byte, double> linear = v => { double s = v / 255.0; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); };
        return .2126 * linear(c.R) + .7152 * linear(c.G) + .0722 * linear(c.B);
    }
    private static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    private static string helpDir;

    /// <summary>설명서용 그림: 컨트롤마다 번호 표시(①②…)와 강조 테두리를 그리고, crop이 있으면 그 부분만 잘라 저장합니다.</summary>
    private static void Annotate(Form form, string name, Control crop, params object[] marks)
    {
        if (helpDir == null) return;
        Application.DoEvents(); form.PerformLayout(); form.Refresh();
        using (var full = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(full, new Rectangle(0, 0, full.Width, full.Height));
            Func<Control, Rectangle> rect = c => { var p = c.PointToScreen(Point.Empty); return new Rectangle(p.X - form.Left, p.Y - form.Top, c.Width, c.Height); };
            using (var g = Graphics.FromImage(full))
            using (var pen = new Pen(Color.FromArgb(245, 166, 35), 2.5F))
            using (var badge = new SolidBrush(Color.FromArgb(229, 72, 77)))
            using (var font = new Font("맑은 고딕", 10F, FontStyle.Bold))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                for (int i = 0; i + 1 < marks.Length; i += 2)
                {
                    var r = rect((Control)marks[i]); r.Inflate(2, 2); g.DrawRectangle(pen, r);
                    var circle = new Rectangle(Math.Max(2, r.Left - 9), Math.Max(2, r.Top - 9), 22, 22);
                    g.FillEllipse(badge, circle); g.DrawEllipse(Pens.White, circle);
                    TextRenderer.DrawText(g, marks[i + 1].ToString(), font, circle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            var area = crop == null ? new Rectangle(0, 0, full.Width, full.Height) : Rectangle.Intersect(Rectangle.Inflate(rect(crop), 14, 14), new Rectangle(0, 0, full.Width, full.Height));
            using (var image = full.Clone(area, full.PixelFormat)) image.Save(Path.Combine(helpDir, name), System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static void Capture(Form form, string path) { Application.DoEvents(); form.PerformLayout(); using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
}
