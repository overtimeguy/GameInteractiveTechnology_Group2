using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class StudyLogger
{
    private const string EventHeader = "ElapsedTime,Action,Value,AutoAimEnabled,FocusModeEnabled,FocusActive,FocusHand,RotationGain,PositionGain,PrecisionAmount";
    private const string TrialHeader = "StudentID,TestType,Condition,MissionType,TrialIndex,TargetID,Distance,Width,ID,MovementTime,ErrorCount,Success";

    private static string studentId = string.Empty;
    private static string eventLogFilePath = string.Empty;
    private static string trialSummaryFilePath = string.Empty;
    private static float startRealtime;
    private static bool initialized;
    private static string testType = string.Empty;
    private static string condition = string.Empty;
    private static string currentMissionType = string.Empty;
    private static MainMission currentMission;
    private static Target currentTarget;
    private static float currentMissionStartRealtime;
    private static float currentDistance;
    private static float currentWidth;
    private static float currentIndexOfDifficulty;
    private static int trialIndex;
    private static int currentErrorCount;
    private static bool currentMissionActive;
    private static string currentTargetSelectHand = string.Empty;
    private static bool currentMissionEnded;
    private static bool autoAimEnabled;
    private static bool focusModeEnabled;
    private static bool focusActive;
    private static string focusHand = "None";
    private static float rotationGain = 1f;
    private static float positionGain = 1f;
    private static float precisionAmount;

    public static bool IsInitialized => initialized;
    public static string StudentID => studentId;
    public static string EventLogFilePath => eventLogFilePath;
    public static string TrialSummaryFilePath => trialSummaryFilePath;
    public static string TestType => testType;
    public static string Condition => condition;
    public static string CurrentMissionType => currentMissionType;

    public static void CreateFiles(string rawStudentId)
    {
        if (string.IsNullOrWhiteSpace(rawStudentId))
        {
            Debug.LogWarning("[StudyLogger] Student ID is empty. Log files were not created.");
            return;
        }

        studentId = rawStudentId.Trim();
        startRealtime = Time.realtimeSinceStartup;

        string safeStudentId = SanitizeFileName(studentId);
        string rootFolder = GetRootFolderPath();
        eventLogFilePath = Path.Combine(rootFolder, safeStudentId + "_events.csv");
        trialSummaryFilePath = Path.Combine(rootFolder, safeStudentId + "_trials.csv");

        RecreateFile(eventLogFilePath, EventHeader);
        RecreateFile(trialSummaryFilePath, TrialHeader);
        initialized = true;
        trialIndex = 0;
        ClearCurrentMissionState();

        Debug.Log($"[StudyLogger] Event log created: {eventLogFilePath}");
        Debug.Log($"[StudyLogger] Trial summary created: {trialSummaryFilePath}");
    }

    public static void SetTestType(string nextTestType)
    {
        testType = nextTestType ?? string.Empty;
    }

    public static void SetCondition(string nextCondition)
    {
        condition = nextCondition ?? string.Empty;
    }

    public static void SetPlayerSnapshot(
        bool nextAutoAimEnabled,
        bool nextFocusModeEnabled,
        bool nextFocusActive,
        string nextFocusHand,
        float nextRotationGain,
        float nextPositionGain,
        float nextPrecisionAmount)
    {
        autoAimEnabled = nextAutoAimEnabled;
        focusModeEnabled = nextFocusModeEnabled;
        focusActive = nextFocusActive;
        focusHand = string.IsNullOrWhiteSpace(nextFocusHand) ? "None" : nextFocusHand.Trim();
        rotationGain = nextRotationGain;
        positionGain = nextPositionGain;
        precisionAmount = nextPrecisionAmount;
    }

    public static void BeginMission(
        string missionType,
        MainMission mission,
        Target primaryTarget,
        float distance,
        float width)
    {
        currentMissionType = string.IsNullOrWhiteSpace(missionType) ? "Mission" : missionType;
        currentMission = mission;
        currentTarget = primaryTarget;
        currentMissionStartRealtime = Time.realtimeSinceStartup;
        currentDistance = Mathf.Max(0f, distance);
        currentWidth = Mathf.Max(0f, width);
        currentIndexOfDifficulty = CalculateIndexOfDifficulty(currentDistance, currentWidth);
        currentErrorCount = 0;
        currentTargetSelectHand = string.Empty;
        currentMissionActive = true;
        currentMissionEnded = false;
        trialIndex++;

        RecordEvent(currentMissionType + "_Start", GetTargetEventValue(currentTarget));
    }

    public static void EndCurrentMission(bool success)
    {
        if (!currentMissionActive || currentMissionEnded)
        {
            return;
        }

        currentMissionEnded = true;
        float movementTime = Mathf.Max(0f, Time.realtimeSinceStartup - currentMissionStartRealtime);
        RecordTrial(
            testType,
            condition,
            currentMissionType,
            trialIndex,
            GetTargetId(currentTarget),
            currentDistance,
            currentWidth,
            currentIndexOfDifficulty,
            movementTime,
            currentErrorCount,
            success);

        RecordEvent(currentMissionType + "_End", success ? "Success" : "Fail");
        ClearCurrentMissionState();
    }

    public static void RecordTargetSpawn(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent(currentMissionType + "_TargetSpawn", GetTargetEventValue(target));
    }

    public static void RecordTargetDetected(string hand, Target target)
    {
        if (!currentMissionActive || target == null)
        {
            return;
        }

        RecordEvent(GetHandAction(hand, currentMissionType + "_TargetDetected"), GetTargetEventValue(target));
    }

    public static void RecordTargetSelected(string hand, Target target)
    {
        if (!currentMissionActive || target == null)
        {
            return;
        }

        string safeHand = NormalizeHand(hand);
        if (target == currentTarget)
        {
            currentTargetSelectHand = safeHand;
            RecordEvent(GetHandAction(safeHand, currentMissionType + "_TargetSelected"), GetTargetEventValue(target));
            return;
        }

        currentErrorCount++;
        string action = currentMissionType switch
        {
            "Mission_Click" => "Mission_Click_WrongTargetClick",
            "Mission_MoveTarget" => "Mission_MoveTarget_WrongTargetSelected",
            _ => currentMissionType + "_WrongTargetSelected"
        };
        RecordEvent(GetHandAction(safeHand, action), GetTargetEventValue(target));
    }

    public static void RecordTargetReleased(string hand, Target target)
    {
        if (!currentMissionActive || target == null)
        {
            return;
        }

        RecordEvent(GetHandAction(hand, currentMissionType + "_TargetReleased"), GetTargetEventValue(target));
    }

    public static void RecordClickTargetClick(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        string action = GetHandAction(currentTargetSelectHand, "Mission_Click_TargetClick");
        RecordEvent(action, GetTargetEventValue(target));
    }

    public static void RecordMoveTargetEnterGoal(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent("Mission_MoveTarget_EnterGoal", GetTargetEventValue(target));
    }

    public static void RecordMoveTargetExitGoal(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent("Mission_MoveTarget_ExitGoal", GetTargetEventValue(target));
    }

    public static void RecordMoveTargetGrounded(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent("Mission_MoveTarget_Grounded", GetTargetEventValue(target));
    }

    public static void RecordMoveTargetUngrounded(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent("Mission_MoveTarget_Ungrounded", GetTargetEventValue(target));
    }

    public static void RecordMoveTargetComplete(Target target)
    {
        if (!IsTargetInCurrentMission(target))
        {
            return;
        }

        RecordEvent("Mission_MoveTarget_Complete", GetTargetEventValue(target));
    }

    public static void RecordEvent(string action, string value = "")
    {
        if (!CanWrite(eventLogFilePath))
        {
            return;
        }

        string elapsedTime = ElapsedTime().ToString("F4", CultureInfo.InvariantCulture);
        AppendCsvLine(
            eventLogFilePath,
            elapsedTime,
            action,
            value,
            FormatBool(autoAimEnabled),
            FormatBool(focusModeEnabled),
            FormatBool(focusActive),
            focusHand,
            FormatFloat(rotationGain),
            FormatFloat(positionGain),
            FormatFloat(precisionAmount));
    }

    public static void RecordTrial(
        string testType,
        string condition,
        string missionType,
        int trialIndex,
        string targetId,
        float distance,
        float width,
        float indexOfDifficulty,
        float movementTime,
        int errorCount,
        bool success)
    {
        if (!CanWrite(trialSummaryFilePath))
        {
            return;
        }

        AppendCsvLine(
            trialSummaryFilePath,
            studentId,
            testType,
            condition,
            missionType,
            trialIndex.ToString(CultureInfo.InvariantCulture),
            targetId,
            distance.ToString("F4", CultureInfo.InvariantCulture),
            width.ToString("F4", CultureInfo.InvariantCulture),
            indexOfDifficulty.ToString("F4", CultureInfo.InvariantCulture),
            movementTime.ToString("F4", CultureInfo.InvariantCulture),
            errorCount.ToString(CultureInfo.InvariantCulture),
            success ? "TRUE" : "FALSE");
    }

    public static void ResetStaticState()
    {
        studentId = string.Empty;
        eventLogFilePath = string.Empty;
        trialSummaryFilePath = string.Empty;
        startRealtime = 0f;
        initialized = false;
        testType = string.Empty;
        condition = string.Empty;
        trialIndex = 0;
        ResetPlayerSnapshot();
        ClearCurrentMissionState();
    }

    private static float ElapsedTime()
    {
        return initialized ? Time.realtimeSinceStartup - startRealtime : 0f;
    }

    private static bool CanWrite(string path)
    {
        if (initialized && !string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return false;
    }

    private static void ClearCurrentMissionState()
    {
        currentMissionType = string.Empty;
        currentMission = null;
        currentTarget = null;
        currentMissionStartRealtime = 0f;
        currentDistance = 0f;
        currentWidth = 0f;
        currentIndexOfDifficulty = 0f;
        currentErrorCount = 0;
        currentMissionActive = false;
        currentTargetSelectHand = string.Empty;
        currentMissionEnded = false;
    }

    private static void ResetPlayerSnapshot()
    {
        autoAimEnabled = false;
        focusModeEnabled = false;
        focusActive = false;
        focusHand = "None";
        rotationGain = 1f;
        positionGain = 1f;
        precisionAmount = 0f;
    }

    private static bool IsTargetInCurrentMission(Target target)
    {
        if (!currentMissionActive || currentMission == null || target == null)
        {
            return false;
        }

        return target.transform == currentMission.transform || target.transform.IsChildOf(currentMission.transform);
    }

    private static string GetTargetId(Target target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        return GetTransformPath(target.transform) + "#" + target.GetInstanceID().ToString(CultureInfo.InvariantCulture);
    }

    private static string GetTargetEventValue(Target target)
    {
        string targetId = GetTargetId(target);
        if (string.IsNullOrEmpty(targetId))
        {
            return string.Empty;
        }

        return targetId + ";MainTarget=" + FormatBool(target == currentTarget);
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static string GetHandAction(string hand, string action)
    {
        string safeHand = NormalizeHand(hand);
        return string.IsNullOrEmpty(safeHand) ? action : safeHand + "_" + action;
    }

    private static string NormalizeHand(string hand)
    {
        if (string.IsNullOrWhiteSpace(hand))
        {
            return string.Empty;
        }

        return hand.Trim();
    }

    private static float CalculateIndexOfDifficulty(float distance, float width)
    {
        if (distance <= 0f || width <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Log(distance / width + 1f, 2f);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string FormatBool(bool value)
    {
        return value ? "TRUE" : "FALSE";
    }

    private static void RecreateFile(string path, string header)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.WriteAllText(path, header + Environment.NewLine, Encoding.UTF8);
    }

    private static void AppendCsvLine(string path, params string[] values)
    {
        string[] escapedValues = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            escapedValues[i] = EscapeCsvValue(values[i]);
        }

        File.AppendAllText(path, string.Join(",", escapedValues) + Environment.NewLine, Encoding.UTF8);
    }

    private static string EscapeCsvValue(string value)
    {
        string safeValue = value ?? string.Empty;
        bool needsQuotes = safeValue.Contains(",")
            || safeValue.Contains("\"")
            || safeValue.Contains("\n")
            || safeValue.Contains("\r");

        if (!needsQuotes)
        {
            return safeValue;
        }

        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
    }

    private static string GetRootFolderPath()
    {
        DirectoryInfo assetsParent = Directory.GetParent(Application.dataPath);
        return assetsParent != null ? assetsParent.FullName : Application.dataPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        string sanitized = fileName.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            sanitized = sanitized.Replace(invalidChars[i], '_');
        }

        return sanitized;
    }
}
