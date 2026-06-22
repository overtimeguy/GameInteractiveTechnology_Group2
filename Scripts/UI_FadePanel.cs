using PrimeTween;
using UnityEngine;

public abstract class UI_FadePanel : MonoBehaviour
{
    private const float FadeDuration = 0.15f;

    private CanvasGroup canvasGroup;
    private Tween fadeTween;
    private Tween disableTween;

    protected virtual void Awake()
    {
        CacheCanvasGroup();
    }

    protected void ActivatePanel()
    {
        CanvasGroup group = CacheCanvasGroup();
        StopFade();
        StopDisable();

        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;
        gameObject.SetActive(true);
        fadeTween = Tween.Custom(
            this,
            group.alpha,
            1f,
            FadeDuration,
            (target, alpha) => target.SetAlpha(alpha),
            Ease.OutQuad);
    }

    protected void DeactivatePanel()
    {
        DeactivatePanel(FadeDuration);
    }

    protected void DeactivatePanel(float disableDelay)
    {
        CanvasGroup group = CacheCanvasGroup();
        StopFade();
        StopDisable();

        group.interactable = false;
        group.blocksRaycasts = false;

        if (!gameObject.activeInHierarchy)
        {
            group.alpha = 0f;
            return;
        }

        fadeTween = Tween.Custom(
                this,
                group.alpha,
                0f,
                FadeDuration,
                (target, alpha) => target.SetAlpha(alpha),
                Ease.OutQuad);

        disableTween = Tween.Delay(
            this,
            Mathf.Max(0f, disableDelay),
            target => target.gameObject.SetActive(false));
    }

    private CanvasGroup CacheCanvasGroup()
    {
        if (canvasGroup == null && !TryGetComponent(out canvasGroup))
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        return canvasGroup;
    }

    private void SetAlpha(float alpha)
    {
        CacheCanvasGroup().alpha = alpha;
    }

    private void StopFade()
    {
        if (!fadeTween.isAlive)
        {
            return;
        }

        fadeTween.Stop();
    }

    private void StopDisable()
    {
        if (!disableTween.isAlive)
        {
            return;
        }

        disableTween.Stop();
    }
}
