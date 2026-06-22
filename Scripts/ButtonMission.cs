using PrimeTween;
using UnityEngine;

public class ButtonMission : MainMission
{
    private const float HiddenScaleMultiplier = 0.95f;

    [SerializeField] private VRButton completeButton;
    [SerializeField] private bool completeOnPressed = true;
    [SerializeField] private bool requireButtonActive = true;
    [SerializeField, Min(0f)] private float buttonFadeDuration = 0.25f;

    private VRButton subscribedButton;
    private CanvasGroup buttonCanvasGroup;
    private Tween buttonFadeTween;
    private Tween buttonDisableTween;
    private Tween buttonScaleTween;
    private Vector3 initialButtonScale;
    private bool hasInitialButtonScale;

    private void Awake()
    {
        CacheInitialButtonScale();
    }

    protected override void OnMissionStarted()
    {
        StudyLogger.RecordEvent("Mission_Button_Start", name);
        ShowButton();
        SubscribeButton();
        if (subscribedButton == null)
        {
            Debug.LogWarning("[ButtonMission] Complete button is not assigned.", this);
        }
    }

    protected override void OnMissionStopped()
    {
        UnsubscribeButton();
        HideButton();
    }

    protected override void OnMissionCompleted()
    {
        UnsubscribeButton();
        HideButton();
        StudyLogger.RecordEvent("Mission_Button_End", name);
    }

    private void SubscribeButton()
    {
        UnsubscribeButton();

        subscribedButton = ResolveButton();
        if (subscribedButton == null)
        {
            return;
        }

        if (completeOnPressed)
        {
            subscribedButton.Pressed.AddListener(OnButtonActivated);
        }
        else
        {
            subscribedButton.Clicked.AddListener(OnButtonActivated);
        }
    }

    private void UnsubscribeButton()
    {
        if (subscribedButton == null)
        {
            return;
        }

        subscribedButton.Pressed.RemoveListener(OnButtonActivated);
        subscribedButton.Clicked.RemoveListener(OnButtonActivated);
        subscribedButton = null;
    }

    private VRButton ResolveButton()
    {
        return completeButton;
    }

    private void ShowButton()
    {
        CanvasGroup group = CacheButtonCanvasGroup();
        if (group == null)
        {
            return;
        }

        StopButtonTweens();
        CacheInitialButtonScale();
        completeButton.StopScaleFeedback();

        completeButton.gameObject.SetActive(true);
        completeButton.transform.localScale = initialButtonScale * HiddenScaleMultiplier;
        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;

        if (buttonFadeDuration <= Mathf.Epsilon)
        {
            group.alpha = 1f;
            completeButton.transform.localScale = initialButtonScale;
            return;
        }

        buttonFadeTween = Tween.Custom(
            group,
            group.alpha,
            1f,
            buttonFadeDuration,
            (target, alpha) => target.alpha = alpha,
            Ease.OutQuad);

        buttonScaleTween = Tween.Scale(
            completeButton.transform,
            initialButtonScale,
            buttonFadeDuration,
            Ease.OutQuad);
    }

    private void HideButton()
    {
        CanvasGroup group = CacheButtonCanvasGroup();
        if (group == null)
        {
            return;
        }

        StopButtonTweens();
        CacheInitialButtonScale();
        completeButton.StopScaleFeedback();
        group.interactable = false;
        group.blocksRaycasts = false;

        if (!completeButton.gameObject.activeSelf || buttonFadeDuration <= Mathf.Epsilon)
        {
            group.alpha = 0f;
            completeButton.transform.localScale = initialButtonScale;
            completeButton.gameObject.SetActive(false);
            return;
        }

        Vector3 cachedScale = initialButtonScale;
        buttonFadeTween = Tween.Custom(
            group,
            group.alpha,
            0f,
            buttonFadeDuration,
            (target, alpha) => target.alpha = alpha,
            Ease.OutQuad);

        buttonScaleTween = Tween.Scale(
            completeButton.transform,
            cachedScale * HiddenScaleMultiplier,
            buttonFadeDuration,
            Ease.OutQuad);

        buttonDisableTween = Tween.Delay(
            group,
            buttonFadeDuration,
            target =>
            {
                target.transform.localScale = cachedScale;
                target.gameObject.SetActive(false);
            });
    }

    private CanvasGroup CacheButtonCanvasGroup()
    {
        if (completeButton == null)
        {
            return null;
        }

        if (buttonCanvasGroup == null && !completeButton.TryGetComponent(out buttonCanvasGroup))
        {
            buttonCanvasGroup = completeButton.gameObject.AddComponent<CanvasGroup>();
        }

        return buttonCanvasGroup;
    }

    private void CacheInitialButtonScale()
    {
        if (hasInitialButtonScale || completeButton == null)
        {
            return;
        }

        initialButtonScale = completeButton.transform.localScale;
        hasInitialButtonScale = true;
    }

    private void StopButtonTweens()
    {
        if (buttonFadeTween.isAlive)
        {
            buttonFadeTween.Stop();
        }

        if (buttonDisableTween.isAlive)
        {
            buttonDisableTween.Stop();
        }

        if (buttonScaleTween.isAlive)
        {
            buttonScaleTween.Stop();
        }
    }

    private void OnButtonActivated()
    {
        if (requireButtonActive && (subscribedButton == null || !subscribedButton.isActiveAndEnabled))
        {
            return;
        }

        string buttonName = subscribedButton != null ? subscribedButton.name : name;
        UnsubscribeButton();
        StudyLogger.RecordEvent("Mission_Button_Click", buttonName);
        HideButton();
        CompleteMission();
    }
}
