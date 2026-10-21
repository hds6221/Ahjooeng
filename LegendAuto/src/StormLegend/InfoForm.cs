using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using CadUi;

namespace StormLegend
{
    /// <summary>프로그램 정보(AHJOOENGINFO): 버전·설치 위치·템플릿 상태·명령 목록.</summary>
    public sealed class InfoForm : CadDialog
    {
        public InfoForm(string version, string installFolder, string templatePath, string templateStatus, string settingsPath, string helpPath)
            : base("프로그램 정보", "Ahjooeng · AutoCAD 2020 자동화 명령 모음")
        {
            AddSection("범례 생성 (LegendAuto " + version + ")");
            AddInfo("설치 위치", installFolder);
            AddInfo("템플릿", templatePath);
            AddInfo("템플릿 상태", templateStatus);
            AddInfo("설정 파일", settingsPath);
            AddSection("명령");
            AddInfo("LEGEND / LGD", "범례 만들기 창 열기");
            AddInfo("LEGENDALL", "한 공종 전체 넣기(명령줄)");
            AddInfo("LEGENDORIGINAL", "원본 표 그대로 복사(명령줄)");
            AddInfo("LEGENDTEMPLATE", "범례 템플릿 DWG 열기");
            AddInfo("LEGENDSETTINGS", "범례 설정");
            AddInfo("LEGENDHELP", "사용 설명서");
            AddInfo("AHJOOENGHELP", "Ahjooeng 도움말(명령 목록)");
            AddInfo("AHJOOENGMENU", "메뉴 막대 [Ahjooeng]을 다시 만들기");
            AddButton("CloseInfo", "닫기", true, (s, e) => Close());
            AddButton("OpenFolder", "설치 폴더 열기", false, (s, e) => Open(installFolder), true, CadGlyph.Open);
            if (File.Exists(helpPath)) AddButton("OpenHelp", "도움말", false, (s, e) => Open(helpPath), true, CadGlyph.Help);
            CancelButton = (IButtonControl)Controls.Find("CloseInfo", true)[0];
            FinishLayout();
        }

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException) { MessageBox.Show(ex.Message, "프로그램 정보"); }
        }
    }
}
