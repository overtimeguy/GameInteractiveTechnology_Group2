using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

public class UI_Fade : MonoBehaviour
{
    public const float FadeDuration = 2f;

    private CanvasGroup canvasGroup;
    private Tween fadeTween;
    private Tween disableTween;

    protected void Awake()
    {
        CacheCanvasGroup();
    }

    [Button]
    public void Activate()
    {
        CanvasGroup group = CacheCanvasGroup();
        StopFade();
        StopDisable();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        group.interactable = true;
        group.blocksRaycasts = true;
        fadeTween = Tween.Custom(
            this,
            group.alpha,
            1f,
            FadeDuration,
            (target, alpha) => target.SetAlpha(alpha),
            Ease.InOutSine);
    }

    [Button]
    public void Deactivate()
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
            Ease.InOutSine);

        disableTween = Tween.Delay(
            this,
            FadeDuration,
            target => target.gameObject.SetActive(false));
    }

    [Button]
    public void FadeInFromBlack()
    {
        CanvasGroup group = CacheCanvasGroup();
        StopFade();
        StopDisable();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        fadeTween = Tween.Custom(
            this,
            1f,
            0f,
            FadeDuration,
            (target, alpha) => target.SetAlpha(alpha),
            Ease.InOutSine);

        disableTween = Tween.Delay(
            this,
            FadeDuration,
            target =>
            {
                CanvasGroup targetGroup = target.CacheCanvasGroup();
                targetGroup.interactable = false;
                targetGroup.blocksRaycasts = false;
                target.gameObject.SetActive(false);
            });
    }

    [Button]
    public void FadeOutToBlack()
    {
        Activate();
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
        if (fadeTween.isAlive)
        {
            fadeTween.Stop();
        }
    }

    private void StopDisable()
    {
        if (disableTween.isAlive)
        {
            disableTween.Stop();
        }
    }
}
