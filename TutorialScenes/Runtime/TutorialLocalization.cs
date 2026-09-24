using System;
using CardGame.ActionQueue;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public readonly struct TutorialText
    {
        public TutorialText(string english, string chinese)
        {
            English = english ?? string.Empty;
            Chinese = chinese ?? string.Empty;
        }

        public string English { get; }
        public string Chinese { get; }
        public string Current => TutorialLocalization.IsChinese ? Chinese : English;
    }

    [Serializable]
    public struct TutorialStaticLabel
    {
        [SerializeField] private Text label;
        [SerializeField] private string english;
        [SerializeField] private string chinese;

        public void Apply(bool chineseLanguage)
        {
            label.text = chineseLanguage ? chinese : english;
        }
    }

    internal static class TutorialLocalization
    {
        private const string LanguagePreferenceKey = "ActionChainWeaver.Tutorial.LanguageChinese";

        public static bool IsChinese { get; private set; } = PlayerPrefs.GetInt(LanguagePreferenceKey, 0) == 1;

        public static void Toggle()
        {
            IsChinese = !IsChinese;
            PlayerPrefs.SetInt(LanguagePreferenceKey, IsChinese ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static string DisplayEntityName(string name)
        {
            return IsChinese ? ChineseEntityName(name) : name;
        }

        public static string ChineseEntityName(string name)
        {
            return name switch
            {
                "Hero" => "玩家",
                "Boss" => "首领",
                "Slime" => "史莱姆",
                _ => name
            };
        }

        public static string ChineseActionName(string debugName)
        {
            if (debugName.StartsWith("Attack ", StringComparison.Ordinal))
                return $"攻击{ChineseEntityName(debugName.Substring(7))}";
            if (debugName.StartsWith("Damage ", StringComparison.Ordinal))
                return $"伤害{ChineseEntityName(debugName.Substring(7))}";
            return debugName switch
            {
                "Strength check" => "力量判定",
                "Heal after attack" => "攻击后治疗",
                _ => debugName
            };
        }

        public static string ChineseStatus(ActionStatus status)
        {
            return status switch
            {
                ActionStatus.Succeeded => "成功",
                ActionStatus.Failed => "失败",
                ActionStatus.Prevented => "被阻止",
                ActionStatus.Cancelled => "已取消",
                _ => status.ToString()
            };
        }

        public static string ChineseStatus(ActionStatus? status)
        {
            return status.HasValue ? ChineseStatus(status.Value) : "无结果";
        }

        public static string ChineseInputBlockers(RootInputBlockers blockers)
        {
            if (blockers == (RootInputBlockers.LogicBusy | RootInputBlockers.PresentationBusy))
                return "逻辑和表现都忙";
            if (blockers == RootInputBlockers.LogicBusy)
                return "逻辑忙";
            if (blockers == RootInputBlockers.PresentationBusy)
                return "表现忙";
            return "无";
        }
    }
}
