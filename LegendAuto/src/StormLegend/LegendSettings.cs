using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace StormLegend
{
    /// <summary>
    /// 범례 생성 설정(⚙ 설정 창). %LOCALAPPDATA%\StormLegend\settings.json에 사람마다 따로 저장됩니다.
    /// 파일이 없거나 깨져 있으면 기본값으로 시작합니다.
    /// </summary>
    [DataContract]
    public sealed class LegendSettings
    {
        public const double MinGap = 0, MaxGap = 5000, DefaultGap = LegendComposer.CategoryGap;

        /// <summary>true: 공종 전체를 블록 하나로 묶어 넣기 / false(기본): 블록 없이 개별 선·문자로 그리기.</summary>
        [DataMember(Name = "outputAsBlock")] public bool OutputAsBlock { get; set; }
        /// <summary>공종 표 사이 간격(1:100 기준 도면 단위). 축척을 바꾸면 함께 커집니다.</summary>
        [DataMember(Name = "categoryGap")] public double CategoryGap { get; set; } = DefaultGap;
        /// <summary>true: 다음에 열 때 마지막으로 쓴 축척으로 시작 / false(기본): 항상 1:100으로 시작.</summary>
        [DataMember(Name = "rememberScale")] public bool RememberScale { get; set; }
        [DataMember(Name = "lastScale")] public int LastScale { get; set; } = 100;
        /// <summary>true(기본): 생성한 범례를 선택된 상태로 둬서 바로 MOVE할 수 있게 합니다.</summary>
        [DataMember(Name = "selectAfterCreate")] public bool SelectAfterCreate { get; set; } = true;
        /// <summary>도면에 같은 이름·다른 내용의 레이어·문자 스타일·선종류·블록이 있을 때. 기본: 도면 정의 사용(+안내).</summary>
        [DataMember(Name = "conflicts")] public DefinitionConflict Conflicts { get; set; }

        public static string DefaultPath { get { return Path.Combine(PresetStore.Folder, "settings.json"); } }

        public int StartScale { get { return RememberScale && LastScale >= LegendComposer.MinScale && LastScale <= LegendComposer.MaxScale ? LastScale : 100; } }

        [OnDeserializing] private void Defaults(StreamingContext context) { CategoryGap = DefaultGap; LastScale = 100; SelectAfterCreate = true; }

        public LegendSettings Clone() { return (LegendSettings)MemberwiseClone(); }

        /// <summary>범위를 벗어난 값을 안전한 값으로 고칩니다.</summary>
        public LegendSettings Normalize()
        {
            if (double.IsNaN(CategoryGap) || double.IsInfinity(CategoryGap)) CategoryGap = DefaultGap;
            CategoryGap = Math.Max(MinGap, Math.Min(MaxGap, Math.Round(CategoryGap)));
            if (LastScale < LegendComposer.MinScale || LastScale > LegendComposer.MaxScale) LastScale = 100;
            if (!Enum.IsDefined(typeof(DefinitionConflict), Conflicts)) Conflicts = DefinitionConflict.UseDrawing;
            return this;
        }

        public static LegendSettings Load(string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                if (!File.Exists(path)) return new LegendSettings();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return ((LegendSettings)new DataContractJsonSerializer(typeof(LegendSettings)).ReadObject(stream) ?? new LegendSettings()).Normalize();
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            { return new LegendSettings(); }
        }

        /// <summary>임시 파일에 쓴 뒤 바꿔 끼워, 저장 중에 꺼져도 설정 파일이 깨지지 않습니다.</summary>
        public void Save(string path = null)
        {
            path = path ?? DefaultPath; Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(LegendSettings)).WriteObject(stream, this);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
