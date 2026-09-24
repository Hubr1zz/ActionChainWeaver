using System.Collections.Generic;
using GameFramework.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialPresentationQueueView : MonoBehaviour
    {
        [SerializeField] private Text actionSummaryText;
        [SerializeField] private Image[] actionSlotBackgrounds;
        [SerializeField] private Text[] actionSlotTexts;
        [SerializeField] private Text motionSummaryText;
        [SerializeField] private Image[] motionSlotBackgrounds;
        [SerializeField] private Text[] motionSlotTexts;
        [SerializeField] private Color activeColor = new(0.21f, 0.66f, 0.83f, 1f);
        [SerializeField] private Color waitingColor = new(0.19f, 0.27f, 0.38f, 1f);

        public void Render(int submittedActions, int finishedActions, IReadOnlyList<int> unfinishedActions, int submittedMotions, int finishedMotions, IReadOnlyList<PresentationHandle> unfinishedMotions)
        {
            int activeActions = unfinishedActions.Count > 0 ? 1 : 0;
            actionSummaryText.text = new TutorialText($"LOGIC  /  ACTION QUEUE\nIN {submittedActions}   RUN {activeActions}   WAIT {unfinishedActions.Count - activeActions}   DONE {finishedActions}", $"逻辑／行动队列\n总 {submittedActions}   运行 {activeActions}   等待 {unfinishedActions.Count - activeActions}   完成 {finishedActions}").Current;
            RenderActionSlots(unfinishedActions);

            int playingMotions = 0;
            for (int i = 0; i < unfinishedMotions.Count; i++)
            {
                if (unfinishedMotions[i].Status == PresentationStatus.Running)
                    playingMotions++;
            }

            motionSummaryText.text = new TutorialText($"VISUAL  /  PRESENTATION QUEUE\nIN {submittedMotions}   PLAY {playingMotions}   WAIT {unfinishedMotions.Count - playingMotions}   DONE {finishedMotions}", $"视觉／表现队列\n总 {submittedMotions}   播放 {playingMotions}   等待 {unfinishedMotions.Count - playingMotions}   完成 {finishedMotions}").Current;
            RenderMotionSlots(unfinishedMotions);
        }

        private void RenderActionSlots(IReadOnlyList<int> unfinished)
        {
            int visible = unfinished.Count <= actionSlotTexts.Length ? unfinished.Count : actionSlotTexts.Length - 1;
            for (int i = 0; i < actionSlotTexts.Length; i++)
            {
                if (i < visible)
                {
                    actionSlotBackgrounds[i].gameObject.SetActive(true);
                    actionSlotBackgrounds[i].color = i == 0 ? activeColor : waitingColor;
                    actionSlotTexts[i].text = new TutorialText($"#{unfinished[i]}\n{(i == 0 ? "RUNNING" : "WAITING")}", $"#{unfinished[i]}\n{(i == 0 ? "运行中" : "等待中")}").Current;
                    continue;
                }

                if (i == visible && unfinished.Count > actionSlotTexts.Length)
                {
                    actionSlotBackgrounds[i].gameObject.SetActive(true);
                    actionSlotBackgrounds[i].color = waitingColor;
                    actionSlotTexts[i].text = new TutorialText($"+{unfinished.Count - visible}\nMORE", $"+{unfinished.Count - visible}\n更多").Current;
                    continue;
                }

                actionSlotBackgrounds[i].gameObject.SetActive(false);
            }
        }

        private void RenderMotionSlots(IReadOnlyList<PresentationHandle> unfinished)
        {
            int visible = unfinished.Count <= motionSlotTexts.Length ? unfinished.Count : motionSlotTexts.Length - 1;
            for (int i = 0; i < motionSlotTexts.Length; i++)
            {
                if (i < visible)
                {
                    PresentationHandle handle = unfinished[i];
                    bool isPlaying = handle.Status == PresentationStatus.Running;
                    motionSlotBackgrounds[i].gameObject.SetActive(true);
                    motionSlotBackgrounds[i].color = isPlaying ? activeColor : waitingColor;
                    motionSlotTexts[i].text = new TutorialText($"HIT FX\n{(isPlaying ? "PLAYING" : "WAITING")}", $"撞击\n{(isPlaying ? "播放中" : "等待中")}").Current;
                    continue;
                }

                if (i == visible && unfinished.Count > motionSlotTexts.Length)
                {
                    motionSlotBackgrounds[i].gameObject.SetActive(true);
                    motionSlotBackgrounds[i].color = waitingColor;
                    motionSlotTexts[i].text = new TutorialText($"+{unfinished.Count - visible}\nMORE", $"+{unfinished.Count - visible}\n更多").Current;
                    continue;
                }

                motionSlotBackgrounds[i].gameObject.SetActive(false);
            }
        }
    }
}
