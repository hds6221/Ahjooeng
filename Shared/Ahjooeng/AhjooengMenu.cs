// Ahjooeng 메뉴 막대 — Autocad_auto 공용(각 명령 DLL에 소스로 함께 빌드).
// 메뉴 모양(명령이 늘어나면 각 명령 DLL이 자기 줄을 하나씩 더 넣습니다):
//   [Ahjooeng]
//     범례 생성 (LGD)     ← 누르면 바로 명령 실행
//     (다음 명령 …)
//     ─────────
//     도움말              ← 도움말 홈(명령 목록)을 바로 엽니다. 명령을 눌러야 그 명령의 설명서가 보입니다.
//     프로그램 정보
// 사용자의 acad.cuix 파일은 고치지 않고, AutoCAD가 실행 중일 때만 메뉴를 만듭니다(COM 메뉴 API).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ahjooeng
{
    /// <summary>메뉴에 넣을 명령 하나: 메뉴 이름과 실행할 명령.</summary>
    public sealed class MenuCommand
    {
        public string Label { get; private set; }
        public string Command { get; private set; }
        public string Description { get; private set; }
        public MenuCommand(string label, string command, string description = null)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(command)) throw new ArgumentException("메뉴 이름과 명령이 필요합니다.");
            Label = label.Trim(); Command = command.Trim(); Description = description;
        }
    }

    public static class AhjooengMenu
    {
        public const string MenuName = "Ahjooeng";
        public const string HelpLabel = "도움말", InfoLabel = "프로그램 정보";
        public const string HelpCommand = "AHJOOENGHELP", InfoCommand = "AHJOOENGINFO";
        private const int ItemTypeSeparator = 1, ItemTypeSubMenu = 2; // AcMenuItemType

        /// <summary>맨 아래 공통 항목(모든 명령이 함께 씀). 등록할 때마다 맨 아래에 한 번만 다시 만듭니다.</summary>
        private static readonly string[][] Footer =
        {
            new[] { HelpLabel, HelpCommand, "도움말을 엽니다. 명령을 누르면 그 명령의 사용 방법이 나옵니다" },
            new[] { InfoLabel, InfoCommand, "버전·설치 위치·템플릿 상태" },
        };

        /// <summary>이전 버전이 만든 항목. 새 메뉴에서는 지웁니다(하위 메뉴 '범례'·'도움말' 포함).</summary>
        private static readonly string[] Legacy = { "범례", "Ahjooeng 도움말", "파일 선택창 복구 (FILEDIA)" };

        /// <summary>
        /// [Ahjooeng] 메뉴에 명령 하나를 넣고(같은 이름이 있으면 같은 자리에 다시 만듦) 맨 아래 공통 항목을 한 번만 둡니다.
        /// 다른 명령 DLL이 넣은 명령 줄은 그대로 둡니다. 성공하면 null, 실패하면 이유를 돌려줍니다(명령은 계속 쓸 수 있음).
        /// </summary>
        public static string Register(object acadApplication, MenuCommand command)
        {
            if (acadApplication == null) return "AutoCAD 메뉴에 접근할 수 없습니다.";
            if (command == null) throw new ArgumentNullException("command");
            try
            {
                dynamic app = acadApplication;
                dynamic menu = FindOrAdd(app.MenuGroups.Item(0).Menus);
                var footerLabels = new HashSet<string>(Footer.Select(f => f[0]));

                // 1) 이전 버전 항목·하위 메뉴·구분선·공통 항목을 걷어 내면 명령 줄만 남습니다.
                int position = -1;
                for (int i = (int)menu.Count - 1; i >= 0; i--)
                {
                    dynamic item = menu.Item(i);
                    int type = (int)item.Type; string label = Clean((string)item.Label);
                    bool remove = type == ItemTypeSeparator || type == ItemTypeSubMenu || Legacy.Contains(label) || footerLabels.Contains(label);
                    if (!remove && label == command.Label) { position = i; remove = true; }
                    if (remove) { item.Delete(); if (position > i) position--; }
                }

                // 2) 명령 줄: 같은 이름이 있던 자리, 없으면 명령 목록 끝
                if (position < 0 || position > (int)menu.Count) position = (int)menu.Count;
                dynamic added = menu.AddMenuItem(position, command.Label, Macro(command.Command));
                if (!string.IsNullOrEmpty(command.Description)) added.HelpString = command.Description;

                // 3) 맨 아래 공통 항목
                menu.AddSeparator((int)menu.Count);
                foreach (var f in Footer)
                {
                    dynamic footer = menu.AddMenuItem((int)menu.Count, f[0], Macro(f[1]));
                    footer.HelpString = f[2];
                }

                if (!(bool)menu.OnMenuBar) menu.InsertInMenuBar((int)app.MenuBar.Count);
                return null;
            }
            catch (Exception ex) { return "Ahjooeng 메뉴를 만들지 못했습니다: " + ex.Message; }
        }

        /// <summary>검사·진단용: 메뉴 모양을 "범례 생성 (LGD)=LGD | --- | 도움말=AHJOOENGHELP | 프로그램 정보=AHJOOENGINFO"처럼 돌려줍니다.</summary>
        public static string Describe(object acadApplication)
        {
            dynamic app = acadApplication; dynamic menu = Find(app.MenuGroups.Item(0).Menus);
            if (menu == null) return "(메뉴 없음)";
            var parts = new List<string>();
            for (int i = 0; i < (int)menu.Count; i++)
            {
                dynamic item = menu.Item(i); int type = (int)item.Type;
                if (type == ItemTypeSeparator) { parts.Add("---"); continue; }
                string label = Clean((string)item.Label);
                if (type == ItemTypeSubMenu)
                {
                    dynamic sub = item.SubMenu; var children = new List<string>();
                    for (int k = 0; k < (int)sub.Count; k++) children.Add(Clean((string)sub.Item(k).Label) + "=" + CommandOf((string)sub.Item(k).Macro));
                    parts.Add(label + " ▸ [" + string.Join(", ", children) + "]");
                }
                else parts.Add(label + "=" + CommandOf((string)item.Macro));
            }
            return string.Join(" | ", parts);
        }

        private static dynamic Find(dynamic menus)
        {
            for (int i = 0; i < (int)menus.Count; i++)
            {
                dynamic candidate = menus.Item(i);
                if (string.Equals(Clean((string)candidate.Name), MenuName, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        private static dynamic FindOrAdd(dynamic menus) { dynamic menu = Find(menus); return menu ?? menus.Add(MenuName); }

        // 메뉴 매크로: 실행 중인 명령을 두 번 취소(^C^C)한 뒤 명령 입력.
        private static string Macro(string command) { return "\u0003\u0003_" + command + " "; }
        private static string CommandOf(string macro) { return (macro ?? "").Replace("\u0003", "").Trim().TrimStart('_'); }
        private static string Clean(string label) { return (label ?? "").Replace("&", "").Trim(); }
    }
}
