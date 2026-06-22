using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using Sirenix.OdinInspector;
using UnityEngine;

[DefaultExecutionOrder(-1100)]
public class MainContainer : MonoBehaviour
{
    public const float MoveDuration = 2.5f;

    public enum MissionProgressState
    {
        BeforeStart,
        Running,
        Completed
    }

    [SerializeField] private List<MissionGroup> missionGroups = new List<MissionGroup>();
    [SerializeField, TextArea] private string warningText;
    [SerializeField] private bool precisionGrabLimit = true;
    [SerializeField] private bool useAutoCorrection = true;
    [SerializeField] private bool useFocusMode = true;
    [SerializeField, ReadOnly] private MissionProgressState missionState = MissionProgressState.BeforeStart;

    private Vector3 _startVec;
    private Sequence _seqMain;
    private Coroutine missionCoroutine;
    private bool hasStartVec;

    public MissionProgressState CurrentMissionState => missionState;
    public bool IsMissionRunning => missionState == MissionProgressState.Running;
    public string ConditionName => BuildConditionName();

    private void Awake()
    {
        CacheStartPosition();
        ResetRuntimeState(false);
    }

    private void Start()
    {
        CacheStartPosition();
    }

    [Button]
    public void Move()
    {
        StopMoveSequence();
        var beginVec = transform.position;
        var endVec = beginVec + Vector3.back * 100;
        
        _seqMain = Sequence.Create();
        _seqMain.Chain(Tween.Position(transform,beginVec, endVec,MoveDuration,Ease.InOutCubic));
    }

    [Button]
    public void StartMission()
    {
        if (IsMissionRunning)
        {
            return;
        }

        if (missionCoroutine != null)
        {
            StopCoroutine(missionCoroutine);
        }

        missionCoroutine = StartCoroutine(StartMissionRoutine());
    }

    public void ResetRuntimeState(bool resetPosition = true)
    {
        if (missionCoroutine != null)
        {
            StopCoroutine(missionCoroutine);
            missionCoroutine = null;
        }

        StopMoveSequence();
        missionState = MissionProgressState.BeforeStart;
        DisableMissionGroups();

        if (resetPosition)
        {
            CacheStartPosition();
            transform.position = _startVec;
        }
    }

    private void CacheStartPosition()
    {
        if (hasStartVec)
        {
            return;
        }

        _startVec = transform.position;
        hasStartVec = true;
    }

    private void StopMoveSequence()
    {
        if (_seqMain.isAlive)
        {
            _seqMain.Stop();
        }
    }

    private IEnumerator StartMissionRoutine()
    {
        missionState = MissionProgressState.Running;
        DisableMissionGroups();
        ApplyPlayerMissionSettings();
        StudyLogger.SetCondition(ConditionName);
        StudyLogger.RecordEvent("MainContainer_Start", name);

        UI_Warning warning = ResolveWarning();
        if (warning != null)
        {
            warning.Activate(warningText);
            yield return new WaitUntil(() => warning == null || !warning.gameObject.activeInHierarchy);
        }

        for (int i = 0; i < missionGroups.Count; i++)
        {
            MissionGroup missionGroup = missionGroups[i];
            if (missionGroup == null)
            {
                continue;
            }

            yield return new WaitForSeconds(1f);
            ActivateMissionGroup(missionGroup);
            yield return null;
            yield return new WaitUntil(() => missionGroup == null || !missionGroup.isActiveAndEnabled);
        }

        missionState = MissionProgressState.Completed;
        StudyLogger.RecordEvent("MainContainer_End", name);
        missionCoroutine = null;
    }

    private void DisableMissionGroups()
    {
        for (int i = 0; i < missionGroups.Count; i++)
        {
            MissionGroup missionGroup = missionGroups[i];
            if (missionGroup == null)
            {
                continue;
            }

            missionGroup.enabled = false;
            if (missionGroup.gameObject.activeSelf)
            {
                missionGroup.gameObject.SetActive(false);
            }
        }
    }

    private static void ActivateMissionGroup(MissionGroup missionGroup)
    {
        missionGroup.enabled = true;
        if (!missionGroup.gameObject.activeSelf)
        {
            missionGroup.gameObject.SetActive(true);
        }
    }

    private static UI_Warning ResolveWarning()
    {
        GlobalManager manager = GlobalManager.Instance != null ? GlobalManager.Instance : FindFirstObjectByType<GlobalManager>();
        if (manager != null && manager.WarningUI != null)
        {
            return manager.WarningUI;
        }

        UI_Warning[] warnings = Resources.FindObjectsOfTypeAll<UI_Warning>();
        for (int i = 0; i < warnings.Length; i++)
        {
            UI_Warning warning = warnings[i];
            if (warning != null && warning.gameObject.scene.IsValid() && warning.gameObject.scene.isLoaded)
            {
                return warning;
            }
        }

        return null;
    }

    private void ApplyPlayerMissionSettings()
    {
        Player player = FindFirstObjectByType<Player>();
        if (player == null)
        {
            return;
        }

        player.SetPrecisionGrabLimit(precisionGrabLimit);
        player.SetMode(true, useAutoCorrection, useFocusMode);
    }

    private string BuildConditionName()
    {
        return $"{(useAutoCorrection ? "AutoCorrectionOn" : "AutoCorrectionOff")}_"
            + $"{(useFocusMode ? "FocusOn" : "FocusOff")}_"
            + $"{(precisionGrabLimit ? "PrecisionLimitOn" : "PrecisionLimitOff")}";
    }
}
