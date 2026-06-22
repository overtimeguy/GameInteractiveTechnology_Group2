using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class VRButton : MonoBehaviour
{
    [SerializeField] private bool interactable = true;
    [SerializeField] private UnityEvent hoverEntered = new UnityEvent();
    [SerializeField] private UnityEvent hoverExited = new UnityEvent();
    [SerializeField] private UnityEvent pressed = new UnityEvent();
    [SerializeField] private UnityEvent released = new UnityEvent();
    [SerializeField] private UnityEvent clicked = new UnityEvent();
    [SerializeField] private UnityEvent canceled = new UnityEvent();
    [SerializeField] private bool clickOnPress = true;
    [SerializeField] private bool playScaleFeedback = true;
    [SerializeField, Range(0.01f, 1f)] private float pressedScaleMultiplier = 0.95f;
    [SerializeField, Min(0f)] private float pressScaleDuration = 0.06f;
    [SerializeField, Min(0f)] private float releaseScaleDuration = 0.08f;
    [SerializeField] private Ease pressScaleEase = Ease.OutQuad;
    [SerializeField] private Ease releaseScaleEase = Ease.OutQuad;

    private readonly HashSet<object> hoverSources = new HashSet<object>();
    private readonly HashSet<object> pressSources = new HashSet<object>();
    private Vector3 initialScale;
    private Tween scaleFeedbackTween;

    public bool Interactable
    {
        get => interactable;
        set
        {
            if (interactable == value)
            {
                return;
            }

            interactable = value;
            if (!interactable)
            {
                CancelAll();
            }
        }
    }

    public bool IsHovered => hoverSources.Count > 0;
    public bool IsPressed => pressSources.Count > 0;
    public UnityEvent HoverEntered => hoverEntered;
    public UnityEvent HoverExited => hoverExited;
    public UnityEvent Pressed => pressed;
    public UnityEvent Released => released;
    public UnityEvent Clicked => clicked;
    public UnityEvent Canceled => canceled;
    public bool ClickOnPress => clickOnPress;

    public void StopScaleFeedback()
    {
        if (scaleFeedbackTween.isAlive)
        {
            scaleFeedbackTween.Stop();
        }
    }

    protected virtual void Awake()
    {
        initialScale = transform.localScale;
    }

    protected virtual void OnDestroy()
    {
        StopScaleFeedback();
    }

    public void SetHoveredBy(object source, bool hovered)
    {
        if (source == null)
        {
            return;
        }

        if (!interactable)
        {
            hovered = false;
        }

        bool wasHovered = IsHovered;
        bool changed = hovered
            ? hoverSources.Add(source)
            : hoverSources.Remove(source);

        if (!changed)
        {
            return;
        }

        if (!wasHovered && IsHovered)
        {
            OnHoverEntered();
            hoverEntered.Invoke();
        }
        else if (wasHovered && !IsHovered)
        {
            OnHoverExited();
            hoverExited.Invoke();
        }
    }

    public void PressBy(object source)
    {
        if (source == null || !interactable)
        {
            return;
        }

        bool wasPressed = IsPressed;
        if (!pressSources.Add(source) || wasPressed)
        {
            return;
        }

        PlayPressedFeedback();
        OnPressed();
        pressed.Invoke();

        if (clickOnPress)
        {
            OnClicked();
            clicked.Invoke();
        }
    }

    public void ReleaseBy(object source)
    {
        if (source == null)
        {
            return;
        }

        bool wasPressed = IsPressed;
        if (!pressSources.Remove(source))
        {
            return;
        }

        if (wasPressed && !IsPressed)
        {
            PlayReleasedFeedback();
            OnReleased();
            released.Invoke();
        }

        if (!clickOnPress && interactable && hoverSources.Contains(source))
        {
            OnClicked();
            clicked.Invoke();
        }
    }

    public void CancelBy(object source)
    {
        if (source == null)
        {
            return;
        }

        bool wasPressed = IsPressed;
        bool wasHovered = IsHovered;
        bool hadPress = pressSources.Remove(source);
        bool hadHover = hoverSources.Remove(source);

        if (hadPress && wasPressed && !IsPressed)
        {
            PlayReleasedFeedback();
            OnCanceled();
            canceled.Invoke();
        }

        if (hadHover && wasHovered && !IsHovered)
        {
            OnHoverExited();
            hoverExited.Invoke();
        }
    }

    public void CancelPressBy(object source)
    {
        if (source == null)
        {
            return;
        }

        bool wasPressed = IsPressed;
        if (!pressSources.Remove(source))
        {
            return;
        }

        if (wasPressed && !IsPressed)
        {
            PlayReleasedFeedback();
            OnCanceled();
            canceled.Invoke();
        }
    }

    public void CancelAll()
    {
        bool wasPressed = IsPressed;
        bool wasHovered = IsHovered;

        pressSources.Clear();
        hoverSources.Clear();

        if (wasPressed)
        {
            PlayReleasedFeedback();
            OnCanceled();
            canceled.Invoke();
        }

        if (wasHovered)
        {
            OnHoverExited();
            hoverExited.Invoke();
        }
    }

    protected virtual void OnHoverEntered() { }
    protected virtual void OnHoverExited() { }
    protected virtual void OnPressed() { }
    protected virtual void OnReleased() { }
    protected virtual void OnClicked() { }
    protected virtual void OnCanceled() { }

    private void PlayPressedFeedback()
    {
        if (clickOnPress)
        {
            PlayPressedPunchFeedback();
            return;
        }

        PlayPressedScaleFeedback();
    }

    private void PlayReleasedFeedback()
    {
        if (!clickOnPress)
        {
            PlayReleasedScaleFeedback();
        }
    }

    private void PlayPressedPunchFeedback()
    {
        if (!playScaleFeedback)
        {
            return;
        }

        if (scaleFeedbackTween.isAlive)
        {
            scaleFeedbackTween.Stop();
        }

        transform.localScale = initialScale;
        scaleFeedbackTween = Tween.Scale(
            transform,
            initialScale * pressedScaleMultiplier,
            pressScaleDuration,
            pressScaleEase,
            2,
            CycleMode.Yoyo);
    }

    private void PlayPressedScaleFeedback()
    {
        PlayScaleFeedback(initialScale * pressedScaleMultiplier, pressScaleDuration, pressScaleEase);
    }

    private void PlayReleasedScaleFeedback()
    {
        PlayScaleFeedback(initialScale, releaseScaleDuration, releaseScaleEase);
    }

    private void PlayScaleFeedback(Vector3 targetScale, float duration, Ease ease)
    {
        if (!playScaleFeedback)
        {
            return;
        }

        if (scaleFeedbackTween.isAlive)
        {
            scaleFeedbackTween.Stop();
        }

        if (duration <= Mathf.Epsilon)
        {
            transform.localScale = targetScale;
            return;
        }

        scaleFeedbackTween = Tween.Scale(transform, targetScale, duration, ease);
    }
}
