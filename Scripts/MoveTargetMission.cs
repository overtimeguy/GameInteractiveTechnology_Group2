using UnityEngine;

public class MoveTargetMission : MainMission
{
    [SerializeField] private Target target;
    [SerializeField] private Transform goal;
    [SerializeField, Min(0f)] private float completeRadius = 0.35f;
    [SerializeField] private bool ignoreHeight;

    private bool clearEffectsPlayed;
    private bool wasInGoal;
    private bool wasGrounded;
    private bool completionLogged;

    protected override void OnMissionStarted()
    {
        clearEffectsPlayed = false;
        wasInGoal = false;
        wasGrounded = false;
        completionLogged = false;
        WarnMissingReferences();
        StudyLogger.BeginMission("Mission_MoveTarget", this, target, GetTrialDistance(), GetTrialWidth());
        GlobalManager.PlayZoneParticle(GetGoalPosition(), GetGoalLocalScale());
    }

    protected override void OnMissionStopped()
    {
        GlobalManager.StopZoneParticle();
    }

    protected override void OnMissionCompleted()
    {
        PlayClearEffects(GetGoalPosition());
        StudyLogger.EndCurrentMission(true);
    }

    protected override Target GetPriorityDisappearTarget()
    {
        return target;
    }

    protected override void UpdateMission(float deltaTime)
    {
        if (target == null)
        {
            return;
        }

        bool isInGoal = IsTargetInGoal();
        bool isGrounded = target.IsGrounded;
        LogGoalStateTransitions(isInGoal);
        LogGroundedStateTransitions(isGrounded);

        if (target.IsSelected)
        {
            return;
        }

        if (!isGrounded)
        {
            return;
        }

        if (isInGoal)
        {
            LogMoveComplete();
            PlayClearEffects(GetGoalPosition());
            CompleteMission();
        }
    }

    private bool IsTargetInGoal()
    {
        if (target == null)
        {
            return false;
        }

        Vector3 targetPosition = target.AssistBubbleCenter;
        Vector3 goalPosition = GetGoalPosition();
        if (ignoreHeight)
        {
            targetPosition.y = goalPosition.y;
        }

        float radius = Mathf.Max(0f, completeRadius);
        Vector3 weightedOffset = targetPosition - goalPosition;
        weightedOffset.z *= 0.5f;
        return weightedOffset.sqrMagnitude <= radius * radius;
    }

    private Vector3 GetGoalPosition()
    {
        return goal != null ? goal.position : transform.position;
    }

    private Vector3 GetGoalLocalScale()
    {
        return goal != null ? goal.localScale : transform.localScale;
    }

    private float GetTrialDistance()
    {
        return target != null ? Vector3.Distance(target.AssistBubbleCenter, GetGoalPosition()) : 0f;
    }

    private float GetTrialWidth()
    {
        return Mathf.Max(0f, completeRadius) * 2f;
    }

    private void LogGoalStateTransitions(bool isInGoal)
    {
        if (isInGoal == wasInGoal)
        {
            return;
        }

        wasInGoal = isInGoal;
        if (isInGoal)
        {
            StudyLogger.RecordMoveTargetEnterGoal(target);
            return;
        }

        StudyLogger.RecordMoveTargetExitGoal(target);
    }

    private void LogGroundedStateTransitions(bool isGrounded)
    {
        if (isGrounded == wasGrounded)
        {
            return;
        }

        wasGrounded = isGrounded;
        if (isGrounded)
        {
            StudyLogger.RecordMoveTargetGrounded(target);
            return;
        }

        StudyLogger.RecordMoveTargetUngrounded(target);
    }

    private void LogMoveComplete()
    {
        if (completionLogged)
        {
            return;
        }

        completionLogged = true;
        StudyLogger.RecordMoveTargetComplete(target);
        StudyLogger.EndCurrentMission(true);
    }

    private void WarnMissingReferences()
    {
        if (target == null)
        {
            Debug.LogWarning("[MoveTargetMission] Target is not assigned.", this);
        }

        if (goal == null)
        {
            Debug.LogWarning("[MoveTargetMission] Goal is not assigned. Zone particle will use this mission transform.", this);
        }
    }

    private void PlayClearEffects(Vector3 position)
    {
        if (clearEffectsPlayed)
        {
            return;
        }

        clearEffectsPlayed = true;
        GlobalManager.StopZoneParticle();
        GlobalManager.PlayZoneClearedParticle(position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Vector3 size = Vector3.one * (Mathf.Max(0f, completeRadius) * 2f);
        size.z *= 2f;
        Gizmos.DrawWireCube(GetGoalPosition(), size);
    }
}
