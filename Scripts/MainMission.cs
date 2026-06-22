using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class MainMission : MonoBehaviour
{
    [SerializeField] private bool disableGameObjectOnComplete = true;
    [SerializeField] private bool disappearChildTargetsOnComplete = true;
    [SerializeField, Min(0f)] private float targetDisappearDelayStep = 0.1f;
    [SerializeField] private bool enableChildTargetsSequentiallyOnStart = true;
    [SerializeField, Min(0f)] private float targetAppearDelayStep = 0.1f;
    [SerializeField] private UnityEvent missionStarted = new UnityEvent();
    [SerializeField] private UnityEvent missionCompleted = new UnityEvent();

    private bool isRunning;
    private bool isCompleted;
    private bool isCompleting;
    private Coroutine completionCoroutine;
    private Coroutine targetEnableCoroutine;

    public bool IsRunning => isRunning;
    public bool IsCompleted => isCompleted;
    public bool IsCompleting => isCompleting;
    public UnityEvent MissionStarted => missionStarted;
    public UnityEvent MissionCompleted => missionCompleted;

    protected virtual void OnEnable()
    {
        Target[] childTargets = GetChildTargets();
        PrepareChildTargetsForSequentialEnable(childTargets);
        BeginMission();
        PlayChildTargetsEnableSequence(childTargets);
    }

    protected virtual void OnDisable()
    {
        isRunning = false;
        isCompleting = false;
        if (completionCoroutine != null)
        {
            StopCoroutine(completionCoroutine);
            completionCoroutine = null;
        }

        StopTargetEnableCoroutine();
        OnMissionStopped();
    }

    protected virtual void Update()
    {
        if (!isRunning || isCompleted || isCompleting)
        {
            return;
        }

        UpdateMission(Time.deltaTime);
    }

    public void BeginMission()
    {
        isCompleted = false;
        isCompleting = false;
        isRunning = true;

        OnMissionStarted();
        missionStarted.Invoke();
    }

    public void ForceComplete()
    {
        CompleteMission();
    }

    protected virtual void OnMissionStarted()
    {
    }

    protected virtual void OnMissionStopped()
    {
    }

    protected virtual void UpdateMission(float deltaTime)
    {
    }

    protected virtual void OnMissionCompleted()
    {
    }

    protected virtual Target GetPriorityDisappearTarget()
    {
        return null;
    }

    protected void CompleteMission()
    {
        if (isCompleted || isCompleting)
        {
            return;
        }

        StopTargetEnableCoroutine();
        isRunning = false;
        isCompleting = true;

        Target[] targets = GetComponentsInChildren<Target>(true);
        Target firstTarget = GetFirstDisappearTarget(targets);
        PlayMissionClearParticle(firstTarget);

        float completionDelay = PlayCompletionTargetDisappearEffects(targets, firstTarget);
        if (completionDelay > 0f)
        {
            completionCoroutine = StartCoroutine(CompleteAfterDelay(completionDelay));
            return;
        }

        FinishMission();
    }

    private IEnumerator CompleteAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        completionCoroutine = null;
        FinishMission();
    }

    private void FinishMission()
    {
        if (isCompleted)
        {
            return;
        }

        isCompleted = true;
        isCompleting = false;
        OnMissionCompleted();
        missionCompleted.Invoke();

        if (disableGameObjectOnComplete)
        {
            gameObject.SetActive(false);
        }
        else
        {
            enabled = false;
        }
    }

    private void PlayMissionClearParticle(Target target)
    {
        if (target == null)
        {
            return;
        }

        GlobalManager.PlayMissionClearParticle(target.AssistBubbleCenter);
    }

    public void PrepareTargetsForMissionEnable()
    {
        PrepareChildTargetsForSequentialEnable(GetChildTargets());
    }

    private Target[] GetChildTargets()
    {
        return GetComponentsInChildren<Target>(true);
    }

    private void PrepareChildTargetsForSequentialEnable(Target[] targets)
    {
        if (!enableChildTargetsSequentiallyOnStart || targets == null || targets.Length == 0)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (!IsChildTarget(target))
            {
                continue;
            }

            target.gameObject.SetActive(false);
        }
    }

    private void PlayChildTargetsEnableSequence(Target[] targets)
    {
        StopTargetEnableCoroutine();
        if (!enableChildTargetsSequentiallyOnStart || targets == null || targets.Length == 0)
        {
            return;
        }

        targetEnableCoroutine = StartCoroutine(EnableChildTargetsSequentially(targets));
    }

    private IEnumerator EnableChildTargetsSequentially(Target[] targets)
    {
        float delayStep = Mathf.Max(0f, targetAppearDelayStep);

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (!IsChildTarget(target))
            {
                continue;
            }

            target.gameObject.SetActive(true);

            if (delayStep > 0f)
            {
                yield return new WaitForSeconds(delayStep);
            }
        }

        targetEnableCoroutine = null;
    }

    private void StopTargetEnableCoroutine()
    {
        if (targetEnableCoroutine == null)
        {
            return;
        }

        StopCoroutine(targetEnableCoroutine);
        targetEnableCoroutine = null;
    }

    private float PlayCompletionTargetDisappearEffects(Target[] targets, Target firstTarget)
    {
        if (!disappearChildTargetsOnComplete)
        {
            return 0f;
        }

        if (targets == null || targets.Length == 0)
        {
            return 0f;
        }

        float delayStep = Mathf.Max(0f, targetDisappearDelayStep);
        float maxDuration = 0f;
        int disappearOrder = 0;

        if (firstTarget != null)
        {
            maxDuration = Mathf.Max(maxDuration, firstTarget.Disappear(0f));
            disappearOrder++;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target == null || target == firstTarget)
            {
                continue;
            }

            float delay = disappearOrder * delayStep;
            maxDuration = Mathf.Max(maxDuration, target.Disappear(delay));
            disappearOrder++;
        }

        return maxDuration;
    }

    private Target GetFirstDisappearTarget(Target[] targets)
    {
        if (targets == null || targets.Length == 0)
        {
            return null;
        }

        Target priorityTarget = GetPriorityDisappearTarget();
        if (ContainsTarget(targets, priorityTarget))
        {
            return priorityTarget;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            Target target = targets[i];
            if (target != null && target.IsSelected)
            {
                return target;
            }
        }

        return null;
    }

    private static bool ContainsTarget(Target[] targets, Target target)
    {
        if (target == null)
        {
            return false;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == target)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsChildTarget(Target target)
    {
        return target != null && target.transform != transform && target.transform.IsChildOf(transform);
    }
}
