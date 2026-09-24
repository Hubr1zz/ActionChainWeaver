using System;
using CardGame.ActionQueue;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.ActionQueue.Tutorial
{
    public sealed class TutorialAttackCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform cardRect;
        [SerializeField] private TutorialPreviewScene scene;
        [SerializeField] private TutorialPreviewTargetView slimeTarget;
        [SerializeField] private TutorialPreviewTargetView bossTarget;

        private RectTransform parentRect;
        private Vector2 homePosition;
        private Vector2 pointerOffset;
        private bool dragging;

        private void Awake()
        {
            parentRect = (RectTransform)cardRect.parent;
            homePosition = cardRect.anchoredPosition;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out Vector2 pointer))
                return;

            dragging = true;
            pointerOffset = pointer - cardRect.anchoredPosition;
            cardRect.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out Vector2 pointer))
                return;

            cardRect.anchoredPosition = pointer - pointerOffset;
            scene.ShowHoverPreview(GetTargetAt(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;

            TutorialPreviewTargetView target = GetTargetAt(eventData);
            ResetToHome();
            scene.DropOnTarget(target);
        }

        public void ResetToHome()
        {
            dragging = false;
            cardRect.anchoredPosition = homePosition;
        }

        private TutorialPreviewTargetView GetTargetAt(PointerEventData eventData)
        {
            if (slimeTarget.Contains(eventData.position, eventData.pressEventCamera))
                return slimeTarget;
            if (bossTarget.Contains(eventData.position, eventData.pressEventCamera))
                return bossTarget;
            return null;
        }
    }

    public sealed class TutorialPreviewTargetView : MonoBehaviour
    {
        [SerializeField] private RectTransform dropArea;
        [SerializeField] private Image healthFill;
        [SerializeField] private Image pendingLoss;
        [SerializeField] private Text healthText;
        [SerializeField] private Text blockText;

        private Color previewColor;
        private bool previewActive;

        private void Awake()
        {
            previewColor = pendingLoss.color;
            pendingLoss.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!previewActive)
                return;

            Color color = previewColor;
            color.a = 0.35f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));
            pendingLoss.color = color;
        }

        public bool Contains(Vector2 screenPoint, Camera eventCamera)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(dropArea, screenPoint, eventCamera);
        }

        public void SetState(string name, int hp, int maxHp, int block)
        {
            float ratio = maxHp > 0 ? (float)hp / maxHp : 0f;
            healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
            healthText.text = $"{TutorialLocalization.DisplayEntityName(name)}  {hp}/{maxHp} {(TutorialLocalization.IsChinese ? "生命" : "HP")}";
            blockText.text = block > 0 ? new TutorialText($"BLOCK {block}", $"格挡 {block}").Current : new TutorialText("NO BLOCK", "无格挡").Current;
        }

        public void ShowPreview(int hp, int maxHp, int damage, int blockedDamage)
        {
            int hpAfter = Math.Max(0, hp - damage);
            if (hpAfter == hp)
            {
                pendingLoss.gameObject.SetActive(false);
                previewActive = false;
                if (blockedDamage > 0)
                    blockText.text += new TutorialText("  /  FULLY BLOCKED", "  /  完全格挡").Current;
                return;
            }

            pendingLoss.rectTransform.anchorMin = new Vector2((float)hpAfter / maxHp, 0f);
            pendingLoss.rectTransform.anchorMax = new Vector2((float)hp / maxHp, 1f);
            pendingLoss.gameObject.SetActive(true);
            previewActive = true;
        }

        public void ClearPreview()
        {
            previewActive = false;
            pendingLoss.gameObject.SetActive(false);
            pendingLoss.color = previewColor;
        }
    }

    internal sealed class TutorialBlockReactor : GameActionReactor<TutorialDamageAction>
    {
        private readonly Action<TutorialText> trace;

        public TutorialBlockReactor(Action<TutorialText> trace)
        {
            this.trace = trace;
        }

        public override ReactionTiming Timing => ReactionTiming.BeforeExecution;

        protected override void React(TutorialDamageAction action, ReactionContext context, ReactionResponse response)
        {
            var target = (TutorialFighter)action.Target;
            int blocked = Math.Min(action.Amount, target.Block);
            action.SetAmount(action.Amount - blocked);
            action.SetBlockConsumption(blocked);
            trace(new TutorialText($"[Boss target] Block plans to absorb {blocked}; remaining damage {action.Amount}", $"[首领目标] 格挡将吸收 {blocked} 点伤害；剩余伤害 {action.Amount}"));
        }
    }
}
