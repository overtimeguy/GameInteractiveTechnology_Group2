using UnityEngine;

public class ClickTargetMission : MainMission
{
    [SerializeField] private Target target;
    [SerializeField] private bool requireTargetEnabled = true;

    private bool clickLogged;

    protected override void OnMissionStarted()
    {
        clickLogged = false;
        StudyLogger.BeginMission("Mission_Click", this, target, GetTargetDistance(), GetTargetWidth());
    }

    protected override void OnMissionCompleted()
    {
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

        if (requireTargetEnabled && !target.isActiveAndEnabled)
        {
            return;
        }

        if (target.IsSelected)
        {
            if (!clickLogged)
            {
                clickLogged = true;
                StudyLogger.RecordClickTargetClick(target);
                StudyLogger.EndCurrentMission(true);
            }

            CompleteMission();
        }
    }

    private float GetTargetDistance()
    {
        if (target == null)
        {
            return 0f;
        }

        return Vector3.Distance(GetReferencePosition(), target.AssistBubbleCenter);
    }

    private float GetTargetWidth()
    {
        return target != null ? target.AssistBubbleRadius * 2f : 0f;
    }

    private Vector3 GetReferencePosition()
    {
        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform.position : transform.position;
    }
}
