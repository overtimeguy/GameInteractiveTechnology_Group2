using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class MissionGroup : MonoBehaviour
{
    private const float MissionCompleteButtonHiddenScaleMultiplier = 0.95f;

    [SerializeField] private bool disableGameObjectOnComplete = true;
    [SerializeField] private Vector2 missionTransitionDelayRange = new Vector2(0.5f, 2f);
    [SerializeField, TextArea] private string announcerText;
    [SerializeField] private List<MainMission> missions = new List<MainMission>();
    [SerializeField] private bool useMissionCompleteButton;
    [SerializeField] private VRButton missionCompleteButton;
    [SerializeField] private bool completeButtonOnPressed = true;
    [SerializeField, Min(0f)] private float missionCompleteButtonFadeDuration = 0.25f;

    private int currentMissionIndex = -1;
    private Coroutine advanceCoroutine;
    private UI_Announcer activeAnnouncer;
    private bool announcerActive;
    private VRButton subscribedCompleteButton;
    private CanvasGroup missionCompleteButtonCanvasGroup;
    private Tween missionCompleteButtonFadeTween;
    private Tween missionCompleteButtonDisableTween;
    private Tween missionCompleteButtonScaleTween;
    private Vector3 missionCompleteButtonInitialScale;
    private bool hasMissionCompleteButtonInitialScale;

    public IReadOnlyList<MainMission> Missions => missions;
    public int CurrentMissionIndex => currentMissionIndex;
    public MainMission CurrentMission => IsValidMissionIndex(currentMissionIndex) ? missions[currentMissionIndex] : null;

    private void Awake()
    {
        CacheMissionCompleteButtonInitialScale();
        RegisterMissionCallbacks();
        DisableAllMissions();
        HideMissionCompleteButtonImmediate();
    }

    private void OnEnable()
    {
        currentMissionIndex = -1;
        StopAdvanceCoroutine();
        DisableAllMissions();
        ActivateAnnouncer();
        ShowMissionCompleteButton();
        ActivateFirstMission();
    }

    private void OnDisable()
    {
        StopAdvanceCoroutine();
        DeactivateAnnouncer();
        HideMissionCompleteButton();
    }

    private void OnDestroy()
    {
        UnregisterMissionCallbacks();
        UnsubscribeMissionCompleteButton();
        StopMissionCompleteButtonTweens();
    }

    private void RegisterMissionCallbacks()
    {
        for (int i = 0; i < missions.Count; i++)
        {
            MainMission mission = missions[i];
            if (mission != null)
            {
                mission.MissionCompleted.AddListener(OnMissionCompleted);
            }
        }
    }

    private void UnregisterMissionCallbacks()
    {
        for (int i = 0; i < missions.Count; i++)
        {
            MainMission mission = missions[i];
            if (mission != null)
            {
                mission.MissionCompleted.RemoveListener(OnMissionCompleted);
            }
        }
    }

    private void ActivateFirstMission()
    {
        int firstMissionIndex = FindNextMissionIndex(0);
        if (firstMissionIndex < 0)
        {
            DisableGroup();
            return;
        }

        ActivateMission(firstMissionIndex);
    }

    private void OnMissionCompleted()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        StopAdvanceCoroutine();
        advanceCoroutine = StartCoroutine(AdvanceAfterMissionDisabled());
    }

    private IEnumerator AdvanceAfterMissionDisabled()
    {
        yield return null;
        advanceCoroutine = null;

        if (!isActiveAndEnabled)
        {
            yield break;
        }

        if (AreAllMissionsCompletedAndDisabled())
        {
            DisableGroup();
            yield break;
        }

        int nextMissionIndex = FindNextMissionIndex(currentMissionIndex + 1);
        if (nextMissionIndex < 0)
        {
            DisableGroup();
            yield break;
        }

        float transitionDelay = GetMissionTransitionDelay();
        if (transitionDelay > 0f)
        {
            yield return new WaitForSeconds(transitionDelay);
        }

        if (!isActiveAndEnabled)
        {
            yield break;
        }

        ActivateMission(nextMissionIndex);
    }

    private void ActivateMission(int missionIndex)
    {
        if (!IsValidMissionIndex(missionIndex))
        {
            DisableGroup();
            return;
        }

        currentMissionIndex = missionIndex;

        for (int i = 0; i < missions.Count; i++)
        {
            MainMission mission = missions[i];
            if (mission == null)
            {
                continue;
            }

            if (i == currentMissionIndex)
            {
                SetMissionEnabled(mission);
            }
            else
            {
                SetMissionDisabled(mission);
            }
        }
    }

    private void DisableAllMissions()
    {
        for (int i = 0; i < missions.Count; i++)
        {
            SetMissionDisabled(missions[i]);
        }
    }

    private int FindNextMissionIndex(int startIndex)
    {
        for (int i = Mathf.Max(0, startIndex); i < missions.Count; i++)
        {
            if (missions[i] != null)
            {
                return i;
            }
        }

        return -1;
    }

    private float GetMissionTransitionDelay()
    {
        float minDelay = Mathf.Min(missionTransitionDelayRange.x, missionTransitionDelayRange.y);
        float maxDelay = Mathf.Max(missionTransitionDelayRange.x, missionTransitionDelayRange.y);
        return Random.Range(Mathf.Max(0f, minDelay), Mathf.Max(0f, maxDelay));
    }

    private bool AreAllMissionsCompletedAndDisabled()
    {
        bool hasMission = false;

        for (int i = 0; i < missions.Count; i++)
        {
            MainMission mission = missions[i];
            if (mission == null)
            {
                continue;
            }

            hasMission = true;
            if (!mission.IsCompleted || mission.isActiveAndEnabled)
            {
                return false;
            }
        }

        return hasMission;
    }

    private bool IsValidMissionIndex(int missionIndex)
    {
        return missionIndex >= 0 && missionIndex < missions.Count && missions[missionIndex] != null;
    }

    private void SetMissionEnabled(MainMission mission)
    {
        if (mission == null)
        {
            return;
        }

        mission.enabled = true;
        if (!mission.gameObject.activeSelf)
        {
            mission.PrepareTargetsForMissionEnable();
            mission.gameObject.SetActive(true);
        }
    }

    private void SetMissionDisabled(MainMission mission)
    {
        if (mission == null)
        {
            return;
        }

        if (mission.gameObject == gameObject)
        {
            mission.enabled = false;
            return;
        }

        if (mission.gameObject.activeSelf)
        {
            mission.gameObject.SetActive(false);
        }
    }

    private void DisableGroup()
    {
        currentMissionIndex = -1;
        StopAdvanceCoroutine();
        DeactivateAnnouncer();
        HideMissionCompleteButton();

        if (disableGameObjectOnComplete)
        {
            gameObject.SetActive(false);
        }
        else
        {
            enabled = false;
        }
    }

    private void StopAdvanceCoroutine()
    {
        if (advanceCoroutine == null)
        {
            return;
        }

        StopCoroutine(advanceCoroutine);
        advanceCoroutine = null;
    }

    private void ActivateAnnouncer()
    {
        if (string.IsNullOrEmpty(announcerText))
        {
            return;
        }

        UI_Announcer announcer = ResolveAnnouncer();
        if (announcer == null)
        {
            return;
        }

        activeAnnouncer = announcer;
        announcerActive = true;
        announcer.Activate(announcerText);
    }

    private void DeactivateAnnouncer()
    {
        if (!announcerActive)
        {
            return;
        }

        UI_Announcer announcer = activeAnnouncer != null ? activeAnnouncer : ResolveAnnouncer();
        if (announcer != null)
        {
            announcer.Deactivate();
        }

        activeAnnouncer = null;
        announcerActive = false;
    }

    private static UI_Announcer ResolveAnnouncer()
    {
        GlobalManager manager = GlobalManager.Instance != null ? GlobalManager.Instance : FindFirstObjectByType<GlobalManager>();
        if (manager != null && manager.AnnouncerUI != null)
        {
            return manager.AnnouncerUI;
        }

        UI_Announcer[] announcers = Resources.FindObjectsOfTypeAll<UI_Announcer>();
        for (int i = 0; i < announcers.Length; i++)
        {
            UI_Announcer announcer = announcers[i];
            if (announcer != null && announcer.gameObject.scene.IsValid() && announcer.gameObject.scene.isLoaded)
            {
                return announcer;
            }
        }

        return null;
    }

    private void ShowMissionCompleteButton()
    {
        if (!useMissionCompleteButton)
        {
            HideMissionCompleteButtonImmediate();
            return;
        }

        CanvasGroup group = CacheMissionCompleteButtonCanvasGroup();
        if (group == null)
        {
            Debug.LogWarning("[MissionGroup] Mission complete button is not assigned.", this);
            return;
        }

        StopMissionCompleteButtonTweens();
        CacheMissionCompleteButtonInitialScale();
        missionCompleteButton.StopScaleFeedback();
        SubscribeMissionCompleteButton();

        missionCompleteButton.gameObject.SetActive(true);
        missionCompleteButton.transform.localScale = missionCompleteButtonInitialScale * MissionCompleteButtonHiddenScaleMultiplier;
        group.alpha = 0f;
        group.interactable = true;
        group.blocksRaycasts = true;

        if (missionCompleteButtonFadeDuration <= Mathf.Epsilon)
        {
            group.alpha = 1f;
            missionCompleteButton.transform.localScale = missionCompleteButtonInitialScale;
            return;
        }

        missionCompleteButtonFadeTween = Tween.Custom(
            group,
            group.alpha,
            1f,
            missionCompleteButtonFadeDuration,
            (target, alpha) => target.alpha = alpha,
            Ease.OutQuad);

        missionCompleteButtonScaleTween = Tween.Scale(
            missionCompleteButton.transform,
            missionCompleteButtonInitialScale,
            missionCompleteButtonFadeDuration,
            Ease.OutQuad);
    }

    private void HideMissionCompleteButton()
    {
        UnsubscribeMissionCompleteButton();

        CanvasGroup group = CacheMissionCompleteButtonCanvasGroup();
        if (group == null)
        {
            return;
        }

        StopMissionCompleteButtonTweens();
        CacheMissionCompleteButtonInitialScale();
        missionCompleteButton.StopScaleFeedback();
        group.interactable = false;
        group.blocksRaycasts = false;

        if (!missionCompleteButton.gameObject.activeSelf || missionCompleteButtonFadeDuration <= Mathf.Epsilon)
        {
            group.alpha = 0f;
            missionCompleteButton.transform.localScale = missionCompleteButtonInitialScale;
            missionCompleteButton.gameObject.SetActive(false);
            return;
        }

        Vector3 cachedScale = missionCompleteButtonInitialScale;
        missionCompleteButtonFadeTween = Tween.Custom(
            group,
            group.alpha,
            0f,
            missionCompleteButtonFadeDuration,
            (target, alpha) => target.alpha = alpha,
            Ease.OutQuad);

        missionCompleteButtonScaleTween = Tween.Scale(
            missionCompleteButton.transform,
            cachedScale * MissionCompleteButtonHiddenScaleMultiplier,
            missionCompleteButtonFadeDuration,
            Ease.OutQuad);

        missionCompleteButtonDisableTween = Tween.Delay(
            group,
            missionCompleteButtonFadeDuration,
            target =>
            {
                target.transform.localScale = cachedScale;
                target.gameObject.SetActive(false);
            });
    }

    private void HideMissionCompleteButtonImmediate()
    {
        UnsubscribeMissionCompleteButton();
        StopMissionCompleteButtonTweens();

        CanvasGroup group = CacheMissionCompleteButtonCanvasGroup();
        if (group == null)
        {
            return;
        }

        CacheMissionCompleteButtonInitialScale();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        missionCompleteButton.transform.localScale = missionCompleteButtonInitialScale;
        missionCompleteButton.gameObject.SetActive(false);
    }

    private void SubscribeMissionCompleteButton()
    {
        UnsubscribeMissionCompleteButton();

        if (missionCompleteButton == null)
        {
            return;
        }

        subscribedCompleteButton = missionCompleteButton;
        if (completeButtonOnPressed)
        {
            subscribedCompleteButton.Pressed.AddListener(OnMissionCompleteButtonActivated);
        }
        else
        {
            subscribedCompleteButton.Clicked.AddListener(OnMissionCompleteButtonActivated);
        }
    }

    private void UnsubscribeMissionCompleteButton()
    {
        if (subscribedCompleteButton == null)
        {
            return;
        }

        subscribedCompleteButton.Pressed.RemoveListener(OnMissionCompleteButtonActivated);
        subscribedCompleteButton.Clicked.RemoveListener(OnMissionCompleteButtonActivated);
        subscribedCompleteButton = null;
    }

    private CanvasGroup CacheMissionCompleteButtonCanvasGroup()
    {
        if (missionCompleteButton == null)
        {
            return null;
        }

        if (missionCompleteButtonCanvasGroup == null && !missionCompleteButton.TryGetComponent(out missionCompleteButtonCanvasGroup))
        {
            missionCompleteButtonCanvasGroup = missionCompleteButton.gameObject.AddComponent<CanvasGroup>();
        }

        return missionCompleteButtonCanvasGroup;
    }

    private void CacheMissionCompleteButtonInitialScale()
    {
        if (hasMissionCompleteButtonInitialScale || missionCompleteButton == null)
        {
            return;
        }

        missionCompleteButtonInitialScale = missionCompleteButton.transform.localScale;
        hasMissionCompleteButtonInitialScale = true;
    }

    private void StopMissionCompleteButtonTweens()
    {
        if (missionCompleteButtonFadeTween.isAlive)
        {
            missionCompleteButtonFadeTween.Stop();
        }

        if (missionCompleteButtonDisableTween.isAlive)
        {
            missionCompleteButtonDisableTween.Stop();
        }

        if (missionCompleteButtonScaleTween.isAlive)
        {
            missionCompleteButtonScaleTween.Stop();
        }
    }

    private void OnMissionCompleteButtonActivated()
    {
        if (!useMissionCompleteButton)
        {
            return;
        }

        MainMission currentMission = CurrentMission;
        if (currentMission == null || currentMission.IsCompleted || currentMission.IsCompleting)
        {
            return;
        }

        StudyLogger.RecordEvent("Mission_SkipUsed", currentMission.name);
        StudyLogger.EndCurrentMission(false);
        currentMission.ForceComplete();
    }
}
