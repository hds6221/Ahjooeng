// Ahjooeng 메뉴 코드 검사(AutoCAD 없이): AutoCAD COM 메뉴와 같은 모양의 가짜 객체로 Register 결과를 확인합니다.
// 실행: Shared/Ahjooeng/tests/run-menu-test.ps1
using System;
using System.Collections.Generic;
using Ahjooeng;

public class FakeItem
{
    public int Type { get; set; }
    public string Label { get; set; }
    public string Macro { get; set; }
    public string HelpString { get; set; }
    public FakeMenu SubMenu { get; set; }
    internal FakeMenu Owner;
    public void Delete() { Owner.Items.Remove(this); }
}

public class FakeMenu
{
    public string Name { get; set; }
    public bool OnMenuBar { get; set; }
    public readonly List<FakeItem> Items = new List<FakeItem>();
    public int Count { get { return Items.Count; } }
    public FakeItem Item(int i) { return Items[i]; }
    private FakeItem Insert(int i, FakeItem item) { if (i < 0 || i > Items.Count) throw new ArgumentOutOfRangeException("index " + i); item.Owner = this; Items.Insert(i, item); return item; }
    public FakeItem AddMenuItem(int i, string label, string macro) { return Insert(i, new FakeItem { Type = 0, Label = label, Macro = macro }); }
    public FakeItem AddSeparator(int i) { return Insert(i, new FakeItem { Type = 1, Label = "" }); }
    public FakeMenu AddSubMenu(int i, string label) { var sub = new FakeMenu { Name = label }; Insert(i, new FakeItem { Type = 2, Label = label, SubMenu = sub }); return sub; }
    public void InsertInMenuBar(int i) { OnMenuBar = true; }
}

public class FakeMenus
{
    public readonly List<FakeMenu> List = new List<FakeMenu>();
    public int Count { get { return List.Count; } }
    public FakeMenu Item(int i) { return List[i]; }
    public FakeMenu Add(string name) { var m = new FakeMenu { Name = name }; List.Add(m); return m; }
}

public class FakeGroup { public FakeMenus Menus = new FakeMenus(); }
public class FakeGroups { public FakeGroup Group = new FakeGroup(); public FakeGroup Item(int i) { return Group; } }
public class FakeBar { public int Count { get { return 12; } } }
public class FakeApp { public FakeGroups MenuGroups = new FakeGroups(); public FakeBar MenuBar = new FakeBar(); }

public static class MenuVerification
{
    static int failures, checks;
    static void Check(bool ok, string name) { checks++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failures++; }

    const string Expected = "범례 생성 (LGD)=LGD | --- | 도움말=AHJOOENGHELP | 프로그램 정보=AHJOOENGINFO";
    static readonly MenuCommand Legend = new MenuCommand("범례 생성 (LGD)", "LGD", "여러 공종을 골라 한 번에 범례를 그립니다");

    static string Macro(string c) { return "\u0003\u0003_" + c + " "; }

    public static int Main()
    {
        // 1) 처음 설치(메뉴 없음)
        var app = new FakeApp();
        Check(AhjooengMenu.Register(app, Legend) == null, "처음 등록 성공");
        Check(AhjooengMenu.Describe(app) == Expected, "처음: " + AhjooengMenu.Describe(app));
        Check(app.MenuGroups.Group.Menus.List[0].OnMenuBar, "메뉴 막대에 표시");
        Check(AhjooengMenu.Register(app, Legend) == null && AhjooengMenu.Describe(app) == Expected, "두 번 등록해도 한 줄씩");

        // 2) 배포 전 개발판 메뉴: 범례 ▸ 하위 메뉴 + 공통 항목
        app = new FakeApp(); var old = app.MenuGroups.Group.Menus.Add("Ahjooeng"); old.OnMenuBar = true;
        var sub = old.AddSubMenu(0, "범례"); sub.AddMenuItem(0, "범례 만들기… (LGD)", Macro("LGD")); sub.AddMenuItem(1, "범례 설정…", Macro("LEGENDSETTINGS"));
        old.AddSeparator(1); old.AddMenuItem(2, "Ahjooeng 도움말", Macro("LEGENDHELP")); old.AddMenuItem(3, "프로그램 정보", Macro("AHJOOENGINFO")); old.AddMenuItem(4, "파일 선택창 복구 (FILEDIA)", Macro("AHJOOENGDIA"));
        AhjooengMenu.Register(app, Legend);
        Check(AhjooengMenu.Describe(app) == Expected, "개발판 메뉴(범례 ▸) 정리: " + AhjooengMenu.Describe(app));

        // 3) 10-15 메뉴: 명령 + 도움말 ▸ 하위 메뉴
        app = new FakeApp(); old = app.MenuGroups.Group.Menus.Add("Ahjooeng"); old.OnMenuBar = true;
        old.AddMenuItem(0, "범례 생성 (LGD)", Macro("LGD")); old.AddSeparator(1); var help = old.AddSubMenu(2, "도움말"); help.AddMenuItem(0, "범례 생성 (LGD)", Macro("LEGENDHELP"));
        AhjooengMenu.Register(app, Legend);
        Check(AhjooengMenu.Describe(app) == Expected, "개발판 메뉴(도움말 ▸) 정리: " + AhjooengMenu.Describe(app));

        // 4) 나중에 명령 추가: 명령 목록 끝에 한 줄, 공통 항목은 맨 아래 한 번
        var second = new MenuCommand("시험 명령 (TST)", "TST");
        AhjooengMenu.Register(app, second);
        const string two = "범례 생성 (LGD)=LGD | 시험 명령 (TST)=TST | --- | 도움말=AHJOOENGHELP | 프로그램 정보=AHJOOENGINFO";
        Check(AhjooengMenu.Describe(app) == two, "두 번째 명령 추가: " + AhjooengMenu.Describe(app));
        AhjooengMenu.Register(app, Legend);
        Check(AhjooengMenu.Describe(app) == two, "첫 명령 다시 등록해도 순서 유지: " + AhjooengMenu.Describe(app));
        AhjooengMenu.Register(app, second);
        Check(AhjooengMenu.Describe(app) == two, "두 번째 명령 다시 등록해도 순서 유지");

        // 5) 메뉴 접근 불가
        Check(AhjooengMenu.Register(null, Legend) != null, "AutoCAD 없음 → 실패 이유 반환");

        Console.WriteLine(failures == 0 ? "SUCCESS: " + checks + " menu checks." : "FAILED: " + failures + " of " + checks);
        return failures;
    }
}
