using PrimeTween;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

public class UI_Title : UI_FadePanel
{
    private const float ScaleDuration = 0.5f;
    private const float ActiveScaleMultiplier = 1f;
    private const float InactiveScaleMultiplier = 1.15f;

    [SerializeField] private TMP_Text tmpText;

    private Tween scaleTween;
    private Vector3 originalLocalScale;
    private string defaultMessage = string.Empty;
    private bool hasOriginalLocalScale;
    private bool hasDefaultMessage;

    public string CurrentMessage { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        CacheOriginalLocalScale();
        CacheText();
    }

    [Button]
    public void Activate()
    {
        Activate(defaultMessage);
    }

    [Button]
    public void Activate(string message)
    {
        CurrentMessage = message;
        ApplyText(message);
        CacheOriginalLocalScale();
        StopScaleTween();
        transform.localScale = GetScaledOriginalScale(InactiveScaleMultiplier);
        ActivatePanel();
        scaleTween = Tween.Scale(transform, GetScaledOriginalScale(ActiveScaleMultiplier), ScaleDuration, Ease.OutCubic);
    }

    [Button]
    public void Deactivate()
    {
        CacheOriginalLocalScale();
        StopScaleTween();
        scaleTween = Tween.Scale(transform, GetScaledOriginalScale(InactiveScaleMultiplier), ScaleDuration, Ease.OutCubic);
        DeactivatePanel(ScaleDuration);
    }

    private void OnDisable()
    {
        StopScaleTween();
    }

    private void ApplyText(string message)
    {
        CacheText();

        if (tmpText != null)
        {
            tmpText.text = message;
        }
    }

    private void CacheText()
    {
        if (tmpText == null)
        {
            tmpText = GetComponentInChildren<TMP_Text>(true);
        }

        if (!hasDefaultMessage && tmpText != null)
        {
            defaultMessage = tmpText.text ?? string.Empty;
            hasDefaultMessage = true;
        }
    }

    private void CacheOriginalLocalScale()
    {
        if (hasOriginalLocalScale)
        {
            return;
        }

        originalLocalScale = transform.localScale;
        hasOriginalLocalScale = true;
    }

    private Vector3 GetScaledOriginalScale(float multiplier)
    {
        return originalLocalScale * multiplier;
    }

    private void StopScaleTween()
    {
        if (!scaleTween.isAlive)
        {
            return;
        }

        scaleTween.Stop();
    }
}
