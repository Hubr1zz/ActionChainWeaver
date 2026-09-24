using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text stateText;
        [SerializeField] private Text historyText;
        [SerializeField] private ScrollRect historyScroll;
        [SerializeField] private Button[] buttons;
        [SerializeField] private Text[] buttonTexts;
        [SerializeField] private Button rulesButton;
        [SerializeField] private Text rulesButtonText;
        [SerializeField] private GameObject rulesPanel;
        [SerializeField] private Text rulesText;
        [SerializeField] private Button languageButton;
        [SerializeField] private Text languageButtonText;
        [SerializeField] private Font chineseFont;
        [SerializeField] private TutorialStaticLabel[] staticLabels;

        private readonly Queue<TutorialText> historyLines = new();
        private readonly Dictionary<Text, TutorialText> externalTexts = new();
        private readonly List<Text> canvasTexts = new();
        private readonly List<Font> originalFonts = new();
        private TutorialText[] buttonLabels;
        private TutorialText title;
        private TutorialText description;
        private TutorialText state;
        private TutorialText rules;

        public event Action LanguageChanged;

        public void Initialize(TutorialText title, TutorialText description)
        {
            this.title = title;
            this.description = description;
            buttonLabels = new TutorialText[buttons.Length];
            Canvas canvas = titleText.GetComponentInParent<Canvas>();
            Text[] texts = canvas.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                canvasTexts.Add(texts[i]);
                originalFonts.Add(texts[i].font);
            }

            rulesPanel.SetActive(false);
            rulesButton.onClick.RemoveAllListeners();
            rulesButton.onClick.AddListener(ToggleRules);
            languageButton.onClick.RemoveAllListeners();
            languageButton.onClick.AddListener(ToggleLanguage);
            ClearHistory();
            RefreshLanguage();
        }

        public void SetRules(TutorialText value)
        {
            rules = value;
            rulesText.text = value.Current;
        }

        public void SetButton(int index, TutorialText label, Action action)
        {
            buttons[index].gameObject.SetActive(true);
            buttonLabels[index] = label;
            buttonTexts[index].text = label.Current;
            buttons[index].onClick.RemoveAllListeners();
            buttons[index].onClick.AddListener(() => action());
        }

        public void HideUnusedButtons(int firstUnused)
        {
            for (int i = firstUnused; i < buttons.Length; i++)
                buttons[i].gameObject.SetActive(false);
        }

        public void SetState(TutorialText value)
        {
            state = value;
            stateText.text = value.Current;
        }

        public void SetExternalText(Text label, TutorialText value)
        {
            externalTexts[label] = value;
            label.text = value.Current;
        }

        public void Append(TutorialText line)
        {
            historyLines.Enqueue(line);
            while (historyLines.Count > TutorialConsts.MaxTraceLines)
                historyLines.Dequeue();

            RefreshHistory();
        }

        public void ClearHistory()
        {
            historyLines.Clear();
            historyText.text = TutorialLocalization.IsChinese ? "点击按钮开始。" : "Click a button to begin.";
            RefreshHistoryLayout();
        }

        private void ToggleLanguage()
        {
            TutorialLocalization.Toggle();
            RefreshLanguage();
            LanguageChanged?.Invoke();
        }

        private void RefreshLanguage()
        {
            for (int i = 0; i < canvasTexts.Count; i++)
                canvasTexts[i].font = TutorialLocalization.IsChinese ? chineseFont : originalFonts[i];

            for (int i = 0; i < staticLabels.Length; i++)
                staticLabels[i].Apply(TutorialLocalization.IsChinese);

            titleText.text = title.Current;
            descriptionText.text = description.Current;
            stateText.text = state.Current;
            rulesText.text = rules.Current;
            for (int i = 0; i < buttonLabels.Length; i++)
                buttonTexts[i].text = buttonLabels[i].Current;
            foreach (KeyValuePair<Text, TutorialText> entry in externalTexts)
                entry.Key.text = entry.Value.Current;

            languageButtonText.font = chineseFont;
            languageButtonText.text = TutorialLocalization.IsChinese ? "EN" : "中文";
            rulesButtonText.text = rulesPanel.activeSelf ? (TutorialLocalization.IsChinese ? "关闭规则" : "CLOSE RULES") : (TutorialLocalization.IsChinese ? "查看规则" : "VIEW RULES");
            RefreshHistory();
        }

        private void RefreshHistory()
        {
            if (historyLines.Count == 0)
            {
                historyText.text = TutorialLocalization.IsChinese ? "点击按钮开始。" : "Click a button to begin.";
                RefreshHistoryLayout();
                return;
            }

            var builder = new StringBuilder();
            foreach (TutorialText line in historyLines)
            {
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(line.Current);
            }

            historyText.text = builder.ToString();
            RefreshHistoryLayout();
        }

        private void RefreshHistoryLayout()
        {
            Canvas.ForceUpdateCanvases();
            historyText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(historyScroll.viewport.rect.height, historyText.preferredHeight));
            Canvas.ForceUpdateCanvases();
            historyScroll.verticalNormalizedPosition = 0;
        }

        private void ToggleRules()
        {
            bool show = !rulesPanel.activeSelf;
            rulesPanel.SetActive(show);
            rulesButtonText.text = show ? (TutorialLocalization.IsChinese ? "关闭规则" : "CLOSE RULES") : (TutorialLocalization.IsChinese ? "查看规则" : "VIEW RULES");
        }
    }
}
