using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Shapes;

public class Player : ImmediateModeShapeDrawer
{
    private enum PrecisionAimHand
    {
        None,
        Left,
        Right
    }

    private enum RayVisualHand
    {
        Left,
        Right
    }

    private struct RayDirectionData
    {
        public Vector3 RawDirection;
        public Vector3 Direction;
        public Vector3 AssistPoint;
        public bool IsAssisted;
    }

    private sealed class RayVisualState
    {
        public float AssistBlend;
        public int LastBlendFrame = -1;
        public Vector3 AssistPoint;
        public Vector3 AssistDirection;
        public bool HasAssistPoint;
    }

    [Header("Controller Visuals")]
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    [Header("Ray")]
    [SerializeField] private Vector3 rayRotationOffset;
    [SerializeField, Min(0f)] private float rayLength = 10f;
    [SerializeField, Tooltip("X = minimum thickness, Y = maximum thickness.")]
    private Vector2 rayThicknessRange = new Vector2(0.004f, 0.01f);
    [SerializeField] private Color leftRayColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color rightRayColor = new Color(1f, 0.35f, 0.2f, 1f);
    [SerializeField] private Color precisionRayMinGainColor = Color.white;
    [SerializeField] private LineGeometry lineGeometry = LineGeometry.Volumetric3D;

    [Header("Hit Marker")]
    [SerializeField] private bool drawHitMarkers = true;
    [SerializeField, Tooltip("X = minimum radius, Y = maximum radius.")]
    private Vector2 hitMarkerRadiusRange = new Vector2(0.02f, 0.05f);
    [SerializeField] private Color hitMarkerColor = Color.yellow;

    [Header("Precision Link Visual")]
    [SerializeField] private bool drawPrecisionLink = true;
    [SerializeField] private Color precisionLinkColor = new Color(0.3f, 0.9f, 1f, 0.95f);
    [SerializeField] private Color precisionLinkMinGainColor = Color.red;
    [SerializeField, Min(0.001f)] private float precisionLinkThickness = 0.036f;
    [SerializeField, Min(0.001f)] private float precisionLinkDashSize = 0.18f;
    [SerializeField, Min(0f)] private float precisionLinkDashSpacing = 0.105f;
    [SerializeField] private float precisionLinkDashSpeed = 10f;
    [SerializeField, Range(-1f, 1f)] private float precisionLinkDashShapeModifier = 1f;

    [Header("Targeting")]
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private bool uiOnlyInteractionMode;

    [Header("Precision Grab Limit")]
    [FormerlySerializedAs("blockTargetGrabAtNearMinimumPrecision")]
    [SerializeField] private bool requireNearMinimumPrecisionToGrab = true;
    [FormerlySerializedAs("precisionLinkMinGainMultiplier")]
    [SerializeField, Min(1f)] private float nearMinimumPrecisionGainMultiplier = 1.1f;

    [Header("Mode Toggles")]
    [SerializeField] private bool basicModeEnabled = true;
    [SerializeField] private bool assistModeEnabled = true;
    [SerializeField] private bool precisionModeEnabled = true;

    private const float assistBubbleDistanceBias = 0.05f;
    private const float assistedRayStraightRatio = 0.5f;
    private const float assistedRayControlDistanceRatio = 0.35f;
    private const float assistedRayVisualBlendSpeed = 8f;
    private const float assistedRayTargetBlendSpeed = 12f;
    private const int assistedRayVisualPointCount = 16;
    private static readonly Color assistBubbleColor = new Color(0.15f, 0.7f, 1f, 0.08f);
    private static readonly Color assistBubbleDetectedColor = new Color(0.2f, 1f, 0.65f, 0.16f);
    private static readonly Color assistBubbleSelectedColor = new Color(0.2f, 1f, 0.25f, 0.22f);

    [Header("Precision C-D Gain")]
    [SerializeField, Tooltip("Optional body/head reference used as the pull target. Falls back to Camera.main, then this transform.")]
    private Transform bodyReference;
    [SerializeField, Min(0.001f), Tooltip("Pull distance required to reach the minimum C-D gain.")]
    private float pullDistanceForMinGain = 0.35f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Rotation C-D gain when pull amount is zero.")]
    private float maxRotationCdGain = 1f;
    [FormerlySerializedAs("minCdGain")]
    [SerializeField, Range(0.05f, 1f), Tooltip("Rotation C-D gain when pull amount is full.")]
    private float minRotationCdGain = 0.25f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Position C-D gain when pull amount is zero.")]
    private float maxPositionCdGain = 1f;
    [SerializeField, Range(0.05f, 1f), Tooltip("Position C-D gain when pull amount is full.")]
    private float minPositionCdGain = 0.25f;
    [SerializeField, Tooltip("Apply C-D gain to the precision hand's visual, ray, and follow position as well as rotation.")]
    private bool applyCdGainToRayPosition = true;
    [SerializeField, Min(0f), Tooltip("Runtime display: projected distance pulled toward the body reference.")]
    private float currentPrecisionPullDistance;
    [SerializeField, Range(0f, 1f), Tooltip("Runtime display: normalized pull amount used for gain blending.")]
    private float currentPrecisionAmount;
    [SerializeField, Range(0f, 1f), Tooltip("Runtime display: active rotation C-D gain.")]
    private float currentRotationCdGain = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("Runtime display: active position C-D gain.")]
    private float currentPositionCdGain = 1f;
    [SerializeField, Min(0.001f), Tooltip("Runtime display: active ray thickness after precision pull scaling.")]
    private float currentRayThickness = 0.01f;
    [SerializeField, Min(0.001f), Tooltip("Runtime display: active hit marker radius after precision pull scaling.")]
    private float currentHitMarkerRadius = 0.05f;

    private bool registered;
    private Target leftTarget;
    private Target rightTarget;
    private Target leftSelectedTarget;
    private Target rightSelectedTarget;
    private VRButton leftButton;
    private VRButton rightButton;
    private VRButton leftPressedButton;
    private VRButton rightPressedButton;
    private PrecisionAimHand precisionAimHand = PrecisionAimHand.None;
    private readonly object leftRayDetector = new object();
    private readonly object rightRayDetector = new object();
    private readonly object leftTriggerSelector = new object();
    private readonly object rightTriggerSelector = new object();
    private readonly object leftButtonPointer = new object();
    private readonly object rightButtonPointer = new object();
    private Vector3 leftControllerPosition;
    private Quaternion leftControllerRotation = Quaternion.identity;
    private Vector3 rightControllerPosition;
    private Quaternion rightControllerRotation = Quaternion.identity;
    private bool hasControllerPose;
    private bool keyboardMouseRightHandOnlyMode;
    private Vector3 precisionStartPosition;
    private Quaternion precisionStartRotation = Quaternion.identity;
    private Vector3 precisionPullDirection = Vector3.back;
    private readonly RayVisualState leftRayVisualState = new RayVisualState();
    private readonly RayVisualState rightRayVisualState = new RayVisualState();

    private void OnValidate()
    {
        raycastMask = uiOnlyInteractionMode ? GetUiLayerMask() : ~0;
        UpdatePrecisionGainValues();
        UpdatePrecisionVisualScale();
    }

    public bool BasicModeEnabled => basicModeEnabled;
    public bool AssistModeEnabled => assistModeEnabled;
    public bool PrecisionModeEnabled => precisionModeEnabled;
    public bool UiOnlyInteractionMode => uiOnlyInteractionMode;
    public LayerMask CurrentInteractionMask => raycastMask;

    public void SetPrecisionGrabLimit(bool enabled)
    {
        requireNearMinimumPrecisionToGrab = enabled;
        UpdateStudyLoggerPlayerSnapshot();
    }

    public void SetMode(bool basicMode, bool assistMode, bool precisionMode)
    {
        basicModeEnabled = basicMode;
        assistModeEnabled = assistMode;
        precisionModeEnabled = precisionMode;
        UpdateStudyLoggerPlayerSnapshot();

        ApplyModeAvailability();
        if (RefreshControllerPoseCache())
        {
            UpdateDetectedTargets();
            UpdateHoveredButtons();
            return;
        }

        CancelAllButtonPresses();
        SetLeftTarget(null);
        SetRightTarget(null);
        SetLeftButton(null);
        SetRightButton(null);
    }

    public void SetAllLayerInteractionMode()
    {
        uiOnlyInteractionMode = false;
        SetInteractionMask(~0, false);
    }

    public void SetUiOnlyInteractionMode()
    {
        uiOnlyInteractionMode = true;
        SetInteractionMask(GetUiLayerMask(), true);
    }

    private void SetInteractionMask(LayerMask nextMask, bool releaseSelectedTargets)
    {
        raycastMask = nextMask;

        if (releaseSelectedTargets)
        {
            ReleaseAllSelectedTargets();
        }

        CancelAllButtonPresses();

        if (RefreshControllerPoseCache())
        {
            UpdateDetectedTargets();
            UpdateHoveredButtons();
            return;
        }

        SetLeftTarget(null);
        SetRightTarget(null);
        SetLeftButton(null);
        SetRightButton(null);
    }

    private static LayerMask GetUiLayerMask()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        return uiLayer >= 0 ? 1 << uiLayer : 1 << 5;
    }

    private bool IsLayerInInteractionMask(int layer)
    {
        return (raycastMask.value & (1 << layer)) != 0;
    }

    private void Start()
    {
        if (InputManager.Instance == null)
        {
            Debug.LogWarning("Player: InputManager is not ready.");
            return;
        }

        InputManager.RegisterLeftTriggerPressed(OnLeftTriggerPressed);
        InputManager.RegisterLeftTriggerReleased(OnLeftTriggerReleased);
        InputManager.RegisterLeftGripPressed(OnLeftGripPressed);
        InputManager.RegisterLeftGripReleased(OnLeftGripReleased);
        InputManager.RegisterRightTriggerPressed(OnRightTriggerPressed);
        InputManager.RegisterRightTriggerReleased(OnRightTriggerReleased);
        InputManager.RegisterRightGripPressed(OnRightGripPressed);
        InputManager.RegisterRightGripReleased(OnRightGripReleased);
        registered = true;
        UpdateStudyLoggerPlayerSnapshot();
    }

    public override void OnDisable()
    {
        base.OnDisable();

        if (!registered)
        {
            return;
        }

        if (InputManager.Instance != null)
        {
            InputManager.UnregisterLeftTriggerPressed(OnLeftTriggerPressed);
            InputManager.UnregisterLeftTriggerReleased(OnLeftTriggerReleased);
            InputManager.UnregisterLeftGripPressed(OnLeftGripPressed);
            InputManager.UnregisterLeftGripReleased(OnLeftGripReleased);
            InputManager.UnregisterRightTriggerPressed(OnRightTriggerPressed);
            InputManager.UnregisterRightTriggerReleased(OnRightTriggerReleased);
            InputManager.UnregisterRightGripPressed(OnRightGripPressed);
            InputManager.UnregisterRightGripReleased(OnRightGripReleased);
        }

        ReleaseAllSelectedTargets();
        CancelButtonPress(ref leftPressedButton, leftButtonPointer);
        CancelButtonPress(ref rightPressedButton, rightButtonPointer);
        SetLeftTarget(null);
        SetRightTarget(null);
        SetLeftButton(null);
        SetRightButton(null);
        registered = false;
    }

    private void FixedUpdate()
    {
        if (!RefreshControllerPoseCache())
        {
            CancelAllButtonPresses();
            SetLeftTarget(null);
            SetRightTarget(null);
            SetLeftButton(null);
            SetRightButton(null);
            return;
        }

        ApplyModeAvailability();
        UpdatePrecisionCdGain();
        Target.RefreshAllAssistBubbles();

        ApplyPose(
            leftHand,
            GetLeftRayPosition(),
            GetLeftRayRotation());

        ApplyPose(
            rightHand,
            GetRightRayPosition(),
            GetRightRayRotation());

        UpdateDetectedTargets();
        UpdateHoveredButtons();

        GetLeftTriggerFollowPose(out Vector3 leftTriggerFollowPosition, out Quaternion leftTriggerFollowRotation);
        GetRightTriggerFollowPose(out Vector3 rightTriggerFollowPosition, out Quaternion rightTriggerFollowRotation);

        UpdateSelectedTargetFollow(
            leftSelectedTarget,
            leftTriggerSelector,
            leftTriggerFollowPosition,
            leftTriggerFollowRotation);

        UpdateSelectedTargetFollow(
            rightSelectedTarget,
            rightTriggerSelector,
            rightTriggerFollowPosition,
            rightTriggerFollowRotation);
    }

    public override void DrawShapes(Camera cam)
    {
        if (!hasControllerPose && !RefreshControllerPoseCache())
        {
            return;
        }

        using (Draw.Command(cam))
        {
            Draw.ResetAllDrawStates();
            Draw.LineGeometry = lineGeometry;
            Draw.ThicknessSpace = ThicknessSpace.Meters;
            Draw.Thickness = currentRayThickness;
            Draw.LineEndCaps = LineEndCap.Round;

            if (ShouldUseLeftRay())
            {
                DrawHandRay(
                    GetLeftRayPosition(),
                    GetLeftRayRotation(),
                    leftRayColor,
                    RayVisualHand.Left);
            }

            if (ShouldUseRightRay())
            {
                DrawHandRay(
                    GetRightRayPosition(),
                    GetRightRayRotation(),
                    rightRayColor,
                    RayVisualHand.Right);
            }

            DrawPrecisionLink();
            DrawAssistBubbles();
        }
    }

    private bool RefreshControllerPoseCache()
    {
        InputManager inputManager = InputManager.Instance;
        if (inputManager == null)
        {
            hasControllerPose = false;
            keyboardMouseRightHandOnlyMode = false;
            return false;
        }

        inputManager.RefreshControllerPoses();
        keyboardMouseRightHandOnlyMode = inputManager.IsKeyboardMouseSimulationActive;
        leftControllerPosition = inputManager.LeftControllerPosition;
        leftControllerRotation = inputManager.LeftControllerRotation;
        rightControllerPosition = inputManager.RightControllerPosition;
        rightControllerRotation = inputManager.RightControllerRotation;
        hasControllerPose = true;

        if (keyboardMouseRightHandOnlyMode)
        {
            ClearLeftHandInteractionForRightOnlyMode();
        }

        return true;
    }

    private static void ApplyPose(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target == null)
        {
            return;
        }

        target.SetPositionAndRotation(position, rotation);
    }

    private void DrawHandRay(Vector3 position, Quaternion rotation, Color color, RayVisualHand hand)
    {
        RayDirectionData rayData = GetRayDirectionData(position, rotation);
        Vector3 direction = rayData.Direction;
        Vector3 straightEnd = position + rayData.RawDirection * rayLength;
        if (TryRaycast(position, rayData.RawDirection, out RaycastHit straightHit))
        {
            straightEnd = straightHit.point;
        }

        TryRaycast(position, direction, out RaycastHit hit);

        Draw.Color = GetHandRayColor(color, hand);
        RayVisualState visualState = GetRayVisualState(hand);
        float assistBlend = UpdateAssistVisualBlend(visualState, rayData);
        Vector3 markerPosition = hit.collider != null
            ? GetHitMarkerPosition(position, hit.point)
            : straightEnd;
        if (assistBlend > 0f && visualState.HasAssistPoint)
        {
            DrawBlendedRay(position, rayData.RawDirection, straightEnd, visualState, assistBlend);
            Vector3 blendedEnd = GetBlendedRayVisualPoint(
                position,
                rayData.RawDirection,
                straightEnd,
                visualState,
                assistBlend,
                1f);
            markerPosition = GetHitMarkerPosition(position, blendedEnd);
        }
        else
        {
            Draw.Line(position, straightEnd);
        }

        if (drawHitMarkers && (hit.collider != null || (assistBlend > 0f && visualState.HasAssistPoint)))
        {
            Draw.Sphere(markerPosition, currentHitMarkerRadius, hitMarkerColor);
        }
    }

    private Color GetHandRayColor(Color defaultColor, RayVisualHand hand)
    {
        if (!IsCurrentPrecisionNearMinimum(nearMinimumPrecisionGainMultiplier))
        {
            return defaultColor;
        }

        bool isPrecisionHand = hand == RayVisualHand.Left
            ? precisionAimHand == PrecisionAimHand.Left
            : precisionAimHand == PrecisionAimHand.Right;
        return isPrecisionHand ? precisionRayMinGainColor : defaultColor;
    }

    private Vector3 GetHitMarkerPosition(Vector3 rayOrigin, Vector3 fallbackPosition)
    {
        Vector3 markerVector = fallbackPosition - rayOrigin;
        if (markerVector.sqrMagnitude <= Mathf.Epsilon)
        {
            return fallbackPosition;
        }

        Ray ray = new Ray(rayOrigin, markerVector.normalized);
        float markerDistance = Mathf.Min(markerVector.magnitude + currentHitMarkerRadius, rayLength);
        if (!Physics.Raycast(ray, out RaycastHit markerHit, markerDistance, raycastMask, triggerInteraction))
        {
            return fallbackPosition;
        }

        return markerHit.point + markerHit.normal * currentHitMarkerRadius;
    }

    private void DrawBlendedRay(
        Vector3 position,
        Vector3 rawDirection,
        Vector3 straightEnd,
        RayVisualState visualState,
        float assistBlend)
    {
        Vector3 assistPoint = visualState.AssistPoint;
        Vector3 originToAssist = assistPoint - position;
        if (originToAssist.sqrMagnitude <= Mathf.Epsilon)
        {
            Draw.Line(position, straightEnd);
            return;
        }

        using (PolylinePath path = new PolylinePath())
        {
            for (int i = 0; i <= assistedRayVisualPointCount; i++)
            {
                float t = i / (float)assistedRayVisualPointCount;
                path.AddPoint(GetBlendedRayVisualPoint(
                    position,
                    rawDirection,
                    straightEnd,
                    visualState,
                    assistBlend,
                    t));
            }

            Draw.PolylineGeometry = PolylineGeometry.Billboard;
            Draw.PolylineJoins = PolylineJoins.Round;
            Draw.Polyline(path);
        }
    }

    private Vector3 GetBlendedRayVisualPoint(
        Vector3 position,
        Vector3 rawDirection,
        Vector3 straightEnd,
        RayVisualState visualState,
        float assistBlend,
        float t)
    {
        Vector3 straightPoint = Vector3.Lerp(position, straightEnd, t);
        Vector3 assistedPoint = GetAssistedRayVisualPoint(position, rawDirection, visualState, t);
        return Vector3.Lerp(straightPoint, assistedPoint, assistBlend);
    }

    private Vector3 GetAssistedRayVisualPoint(
        Vector3 position,
        Vector3 rawDirection,
        RayVisualState visualState,
        float t)
    {
        Vector3 assistPoint = visualState.AssistPoint;
        Vector3 originToAssist = assistPoint - position;
        if (originToAssist.sqrMagnitude <= Mathf.Epsilon)
        {
            return position;
        }

        Vector3 assistDirection = visualState.AssistDirection.sqrMagnitude > Mathf.Epsilon
            ? visualState.AssistDirection.normalized
            : originToAssist.normalized;

        float rawDistanceToAssist = Mathf.Max(0f, Vector3.Dot(originToAssist, rawDirection));
        Vector3 bendPoint = position + rawDirection * (rawDistanceToAssist * assistedRayStraightRatio);
        float curveLength = Vector3.Distance(bendPoint, assistPoint);
        if (curveLength <= Mathf.Epsilon)
        {
            return Vector3.Lerp(position, assistPoint, t);
        }

        if (t <= assistedRayStraightRatio)
        {
            float straightT = Mathf.Clamp01(t / Mathf.Max(assistedRayStraightRatio, Mathf.Epsilon));
            return Vector3.Lerp(position, bendPoint, straightT);
        }

        float curveT = Mathf.Clamp01((t - assistedRayStraightRatio) / Mathf.Max(1f - assistedRayStraightRatio, Mathf.Epsilon));
        Vector3 startControl = bendPoint + rawDirection * (curveLength * assistedRayControlDistanceRatio);
        Vector3 endControl = assistPoint - assistDirection * (curveLength * assistedRayControlDistanceRatio);
        return EvaluateCubicBezier(bendPoint, startControl, endControl, assistPoint, curveT);
    }

    private static Vector3 EvaluateCubicBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float inverseT = 1f - t;
        return inverseT * inverseT * inverseT * a
            + 3f * inverseT * inverseT * t * b
            + 3f * inverseT * t * t * c
            + t * t * t * d;
    }

    private RayVisualState GetRayVisualState(RayVisualHand hand)
    {
        return hand == RayVisualHand.Left ? leftRayVisualState : rightRayVisualState;
    }

    private float UpdateAssistVisualBlend(RayVisualState visualState, RayDirectionData rayData)
    {
        if (visualState.LastBlendFrame != Time.frameCount)
        {
            if (rayData.IsAssisted)
            {
                UpdateAssistVisualTarget(visualState, rayData);
            }

            float targetBlend = rayData.IsAssisted ? 1f : 0f;
            visualState.AssistBlend = Mathf.MoveTowards(
                visualState.AssistBlend,
                targetBlend,
                assistedRayVisualBlendSpeed * Time.deltaTime);
            visualState.LastBlendFrame = Time.frameCount;

            if (!rayData.IsAssisted && visualState.AssistBlend <= Mathf.Epsilon)
            {
                visualState.AssistBlend = 0f;
                visualState.HasAssistPoint = false;
            }
        }

        return visualState.AssistBlend;
    }

    private void UpdateAssistVisualTarget(RayVisualState visualState, RayDirectionData rayData)
    {
        if (!visualState.HasAssistPoint)
        {
            visualState.AssistPoint = rayData.AssistPoint;
            visualState.AssistDirection = rayData.Direction;
            visualState.HasAssistPoint = true;
            return;
        }

        float blend = 1f - Mathf.Exp(-assistedRayTargetBlendSpeed * Time.deltaTime);
        visualState.AssistPoint = Vector3.Lerp(visualState.AssistPoint, rayData.AssistPoint, blend);

        Vector3 currentDirection = visualState.AssistDirection.sqrMagnitude > Mathf.Epsilon
            ? visualState.AssistDirection.normalized
            : rayData.Direction;
        visualState.AssistDirection = Vector3.Slerp(currentDirection, rayData.Direction, blend).normalized;
    }

    private void DrawPrecisionLink()
    {
        if (!drawPrecisionLink
            || !TryGetPrecisionLinkPositions(
                out Vector3 inputHandPosition,
                out Vector3 focusHandPosition,
                out Vector3 targetPosition,
                out bool hasTargetLink))
        {
            return;
        }

        float dashOffset = Time.time * precisionLinkDashSpeed;
        DashStyle dashStyle = DashStyle.MeterDashes(
            DashType.Angled,
            precisionLinkDashSize,
            precisionLinkDashSpacing,
            DashSnapping.Off,
            dashOffset,
            precisionLinkDashShapeModifier);

        Draw.Color = IsCurrentPrecisionNearMinimum(nearMinimumPrecisionGainMultiplier)
            ? precisionLinkMinGainColor
            : precisionLinkColor;
        Draw.Thickness = precisionLinkThickness;
        Draw.LineEndCaps = LineEndCap.Round;

        using (Draw.DashedScope(dashStyle))
        {
            if ((inputHandPosition - focusHandPosition).sqrMagnitude > Mathf.Epsilon)
            {
                Draw.Line(inputHandPosition, focusHandPosition);
            }

            if (hasTargetLink)
            {
                Draw.Line(focusHandPosition, targetPosition);
            }
        }
    }

    private bool TryGetPrecisionLinkPositions(
        out Vector3 inputHandPosition,
        out Vector3 focusHandPosition,
        out Vector3 targetPosition,
        out bool hasTargetLink)
    {
        inputHandPosition = Vector3.zero;
        focusHandPosition = Vector3.zero;
        targetPosition = Vector3.zero;
        hasTargetLink = false;

        Target linkedTarget;
        Quaternion focusRayRotation;
        if (precisionAimHand == PrecisionAimHand.Left)
        {
            linkedTarget = leftSelectedTarget;
            focusHandPosition = GetLeftRayPosition();
            inputHandPosition = focusHandPosition;
            focusRayRotation = GetLeftRayRotation();
        }
        else if (precisionAimHand == PrecisionAimHand.Right)
        {
            linkedTarget = rightSelectedTarget;
            focusHandPosition = GetRightRayPosition();
            inputHandPosition = focusHandPosition;
            focusRayRotation = GetRightRayRotation();
        }
        else
        {
            return false;
        }

        if (linkedTarget == null)
        {
            targetPosition = focusHandPosition;
            return false;
        }

        targetPosition = GetRayEnd(focusHandPosition, focusRayRotation);
        hasTargetLink = true;
        return true;
    }

    private Vector3 GetRayEnd(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = GetAssistedRayDirection(position, rotation);
        return TryRaycast(position, direction, out RaycastHit hit)
            ? hit.point
            : position + direction * rayLength;
    }

    private bool ShouldUseLeftRay()
    {
        if (keyboardMouseRightHandOnlyMode)
        {
            return false;
        }

        if (precisionAimHand == PrecisionAimHand.Left)
        {
            return true;
        }

        if (precisionAimHand == PrecisionAimHand.Right)
        {
            return false;
        }

        return CanUseFreeAimRay();
    }

    private bool ShouldUseRightRay()
    {
        if (precisionAimHand == PrecisionAimHand.Right)
        {
            return true;
        }

        if (precisionAimHand == PrecisionAimHand.Left)
        {
            return false;
        }

        return CanUseFreeAimRay();
    }

    private bool CanUseFreeAimRay()
    {
        return basicModeEnabled || assistModeEnabled;
    }

    private void ClearLeftHandInteractionForRightOnlyMode()
    {
        SetLeftTarget(null);
        SetLeftButton(null);
        CancelButtonPress(ref leftPressedButton, leftButtonPointer);
        Target leftReleasedTarget = leftSelectedTarget;
        ReleaseSelectedTarget(ref leftSelectedTarget, leftTriggerSelector);
        if (leftReleasedTarget != null)
        {
            RecordPlayerTargetReleased("Left", leftReleasedTarget);
        }

        if (precisionAimHand == PrecisionAimHand.Left)
        {
            ExitPrecisionMode(PrecisionAimHand.Left);
        }
    }

    private Vector3 GetLeftRayPosition()
    {
        return GetRayPosition(PrecisionAimHand.Left, leftControllerPosition);
    }

    private Vector3 GetRightRayPosition()
    {
        return GetRayPosition(PrecisionAimHand.Right, rightControllerPosition);
    }

    private Vector3 GetRayPosition(PrecisionAimHand hand, Vector3 controllerPosition)
    {
        if (!applyCdGainToRayPosition || precisionAimHand != hand)
        {
            return controllerPosition;
        }

        return precisionStartPosition + (controllerPosition - precisionStartPosition) * currentPositionCdGain;
    }

    private Quaternion GetLeftRayRotation()
    {
        return GetRayRotation(PrecisionAimHand.Left, leftControllerRotation);
    }

    private Quaternion GetRightRayRotation()
    {
        return GetRayRotation(PrecisionAimHand.Right, rightControllerRotation);
    }

    private Quaternion GetRayRotation(PrecisionAimHand hand, Quaternion controllerRotation)
    {
        if (precisionAimHand != hand)
        {
            return controllerRotation;
        }

        Quaternion delta = controllerRotation * Quaternion.Inverse(precisionStartRotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle <= Mathf.Epsilon
            || axis.sqrMagnitude <= Mathf.Epsilon
            || float.IsNaN(axis.x)
            || float.IsNaN(axis.y)
            || float.IsNaN(axis.z))
        {
            return precisionStartRotation;
        }

        if (angle > 180f)
        {
            angle -= 360f;
        }

        Quaternion scaledDelta = Quaternion.AngleAxis(angle * currentRotationCdGain, axis);
        return scaledDelta * precisionStartRotation;
    }

    private Vector3 GetRayDirection(Quaternion rotation)
    {
        Quaternion finalRotation = rotation * Quaternion.Euler(rayRotationOffset);
        return finalRotation * Vector3.forward;
    }

    private Vector3 GetAssistedRayDirection(Vector3 position, Quaternion rotation)
    {
        return GetRayDirectionData(position, rotation).Direction;
    }

    private RayDirectionData GetRayDirectionData(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = GetRayDirection(rotation);
        RayDirectionData data = new RayDirectionData
        {
            RawDirection = direction,
            Direction = direction,
            AssistPoint = position + direction * rayLength,
            IsAssisted = false
        };

        if (!assistModeEnabled)
        {
            return data;
        }

        float maxAssistDistance = rayLength;
        if (TryRaycast(position, direction, out RaycastHit rawHit))
        {
            Target directTarget = rawHit.collider.GetComponentInParent<Target>();
            if (directTarget != null)
            {
                if (!directTarget.IsSelected
                    && TryGetDirectionToTargetCenter(position, direction, directTarget, out Vector3 directTargetDirection))
                {
                    data.Direction = directTargetDirection;
                    data.AssistPoint = directTarget.AssistBubbleCenter;
                    data.IsAssisted = true;
                }

                return data;
            }

            maxAssistDistance = rawHit.distance;
        }

        if (TryGetAssistBubbleCorrection(
            position,
            direction,
            maxAssistDistance,
            out Vector3 correctedDirection,
            out Vector3 assistPoint))
        {
            data.Direction = correctedDirection;
            data.AssistPoint = assistPoint;
            data.IsAssisted = true;
        }

        return data;
    }

    private Target RaycastTarget(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = GetAssistedRayDirection(position, rotation);
        if (TryRaycast(position, direction, out RaycastHit hit))
        {
            Target target = hit.collider.GetComponentInParent<Target>();
            return target != null && !target.IsSelected ? target : null;
        }

        return null;
    }

    private VRButton RaycastButton(Vector3 position, Quaternion rotation)
    {
        Vector3 direction = GetAssistedRayDirection(position, rotation);
        if (TryRaycast(position, direction, out RaycastHit hit))
        {
            VRButton button = hit.collider.GetComponentInParent<VRButton>();
            return button != null && button.Interactable ? button : null;
        }

        return null;
    }

    private bool TryRaycast(Vector3 position, Vector3 direction, out RaycastHit hit)
    {
        Ray ray = new Ray(position, direction);
        return Physics.Raycast(ray, out hit, rayLength, raycastMask, triggerInteraction);
    }

    private bool TryRaycast(Vector3 position, Vector3 direction)
    {
        Ray ray = new Ray(position, direction);
        return Physics.Raycast(ray, rayLength, raycastMask, triggerInteraction);
    }

    private bool TryGetAssistBubbleCorrection(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float maxAssistDistance,
        out Vector3 correctedDirection,
        out Vector3 assistPoint)
    {
        correctedDirection = rayDirection;
        assistPoint = rayOrigin + rayDirection * rayLength;
        IReadOnlyList<Target> targets = Target.Instances;
        Target bestTarget = null;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < targets.Count; i++)
        {
            Target target = targets[i];
            if (target == null
                || !target.isActiveAndEnabled
                || !target.AssistBubbleEnabled
                || !IsLayerInInteractionMask(target.gameObject.layer)
                || target.IsSelected)
            {
                continue;
            }

            float bubbleRadius = target.AssistBubbleRadius;
            if (bubbleRadius <= Mathf.Epsilon)
            {
                continue;
            }

            if (!TryGetRayBubbleContact(
                rayOrigin,
                rayDirection,
                target.AssistBubbleCenter,
                bubbleRadius,
                out float distanceToRay,
                out float distanceAlongRay))
            {
                continue;
            }

            if (distanceAlongRay > maxAssistDistance)
            {
                continue;
            }

            float normalizedRayDistance = distanceToRay / bubbleRadius;
            float normalizedRayDepth = distanceAlongRay / Mathf.Max(rayLength, Mathf.Epsilon);
            float score = normalizedRayDistance + normalizedRayDepth * assistBubbleDistanceBias;
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = target;
            }
        }

        if (bestTarget == null)
        {
            return false;
        }

        assistPoint = bestTarget.AssistBubbleCenter;
        return TryGetDirectionToTargetCenter(rayOrigin, rayDirection, bestTarget, out correctedDirection);
    }

    private bool TryGetDirectionToTargetCenter(
        Vector3 rayOrigin,
        Vector3 fallbackDirection,
        Target target,
        out Vector3 correctedDirection)
    {
        correctedDirection = fallbackDirection;
        Vector3 correctedVector = target.AssistBubbleCenter - rayOrigin;
        if (correctedVector.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        Vector3 targetDirection = correctedVector.normalized;
        if (Vector3.Dot(targetDirection, fallbackDirection) <= 0f)
        {
            return false;
        }

        correctedDirection = targetDirection;
        return (correctedDirection - fallbackDirection).sqrMagnitude > 0.000001f;
    }

    private bool TryGetRayBubbleContact(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 bubbleCenter,
        float bubbleRadius,
        out float distanceToRay,
        out float distanceAlongRay)
    {
        Vector3 originToCenter = bubbleCenter - rayOrigin;
        distanceAlongRay = Vector3.Dot(originToCenter, rayDirection);
        if (distanceAlongRay < 0f || distanceAlongRay > rayLength)
        {
            distanceToRay = float.PositiveInfinity;
            return false;
        }

        Vector3 closestPoint = rayOrigin + rayDirection * distanceAlongRay;
        distanceToRay = Vector3.Distance(bubbleCenter, closestPoint);
        return distanceToRay <= bubbleRadius;
    }

    private void DrawAssistBubbles()
    {
        if (!assistModeEnabled || !GlobalManager.ShouldRenderTargetRanges())
        {
            return;
        }

        IReadOnlyList<Target> targets = Target.Instances;
        Draw.BlendMode = ShapesBlendMode.Transparent;
        float alphaMultiplier = GlobalManager.TargetRangeAlphaMultiplier();
        for (int i = 0; i < targets.Count; i++)
        {
            Target target = targets[i];
            if (target == null
                || !target.isActiveAndEnabled
                || !target.AssistBubbleEnabled
                || !IsLayerInInteractionMask(target.gameObject.layer))
            {
                continue;
            }

            float bubbleRadius = target.AssistBubbleRadius;
            if (bubbleRadius <= Mathf.Epsilon)
            {
                continue;
            }

            Color color = target.IsSelected
                ? assistBubbleSelectedColor
                : target.IsDetected
                    ? assistBubbleDetectedColor
                    : assistBubbleColor;
            color.a = Mathf.Clamp01(color.a * alphaMultiplier);

            Draw.Sphere(target.AssistBubbleCenter, bubbleRadius, color);
        }
    }

    private void SetLeftTarget(Target target)
    {
        if (leftTarget == target)
        {
            return;
        }

        if (leftTarget != null)
        {
            leftTarget.SetDetectedBy(leftRayDetector, false);
        }

        leftTarget = target;

        if (leftTarget != null)
        {
            leftTarget.SetDetectedBy(leftRayDetector, true);
            RecordPlayerTargetDetected("Left", leftTarget);
        }
    }

    private void SetRightTarget(Target target)
    {
        if (rightTarget == target)
        {
            return;
        }

        if (rightTarget != null)
        {
            rightTarget.SetDetectedBy(rightRayDetector, false);
        }

        rightTarget = target;

        if (rightTarget != null)
        {
            rightTarget.SetDetectedBy(rightRayDetector, true);
            RecordPlayerTargetDetected("Right", rightTarget);
        }
    }

    private void SetLeftButton(VRButton button)
    {
        if (leftButton == button)
        {
            return;
        }

        if (leftButton != null)
        {
            leftButton.SetHoveredBy(leftButtonPointer, false);
        }

        leftButton = button;

        if (leftButton != null)
        {
            leftButton.SetHoveredBy(leftButtonPointer, true);
        }
    }

    private void SetRightButton(VRButton button)
    {
        if (rightButton == button)
        {
            return;
        }

        if (rightButton != null)
        {
            rightButton.SetHoveredBy(rightButtonPointer, false);
        }

        rightButton = button;

        if (rightButton != null)
        {
            rightButton.SetHoveredBy(rightButtonPointer, true);
        }
    }

    private void UpdateDetectedTargets()
    {
        SetLeftTarget(ShouldUseLeftRay()
            ? RaycastTarget(GetLeftRayPosition(), GetLeftRayRotation())
            : null);

        SetRightTarget(ShouldUseRightRay()
            ? RaycastTarget(GetRightRayPosition(), GetRightRayRotation())
            : null);
    }

    private void UpdateHoveredButtons()
    {
        SetLeftButton(ShouldUseLeftRay()
            ? RaycastButton(GetLeftRayPosition(), GetLeftRayRotation())
            : null);

        SetRightButton(ShouldUseRightRay()
            ? RaycastButton(GetRightRayPosition(), GetRightRayRotation())
            : null);
    }

    private Target GetLeftTriggerSelectionTarget()
    {
        return precisionAimHand switch
        {
            PrecisionAimHand.Right => null,
            _ => leftTarget
        };
    }

    private Target GetRightTriggerSelectionTarget()
    {
        return precisionAimHand switch
        {
            PrecisionAimHand.Left => null,
            _ => rightTarget
        };
    }

    private void GetLeftTriggerFollowPose(out Vector3 position, out Quaternion rotation)
    {
        position = GetLeftRayPosition();
        rotation = GetLeftRayRotation();
    }

    private void GetRightTriggerFollowPose(out Vector3 position, out Quaternion rotation)
    {
        position = GetRightRayPosition();
        rotation = GetRightRayRotation();
    }

    private static bool SelectDetectedTarget(
        Target target,
        object selector,
        ref Target selectedTarget,
        Vector3 controllerPosition,
        Quaternion controllerRotation)
    {
        if (target == null || !target.IsDetected)
        {
            return false;
        }

        if (selectedTarget != null && selectedTarget != target)
        {
            selectedTarget.EndFollowBy(selector);
        }

        selectedTarget = target;
        selectedTarget.BeginFollowBy(selector, controllerPosition, controllerRotation);
        return true;
    }

    private bool CanGrabTargetWithCurrentPrecision()
    {
        return !requireNearMinimumPrecisionToGrab
            || IsCurrentPrecisionNearMinimum(nearMinimumPrecisionGainMultiplier);
    }

    private static void ReleaseSelectedTarget(ref Target selectedTarget, object selector)
    {
        if (selectedTarget == null)
        {
            return;
        }

        selectedTarget.EndFollowBy(selector);
        selectedTarget = null;
    }

    private static void UpdateSelectedTargetFollow(
        Target selectedTarget,
        object selector,
        Vector3 controllerPosition,
        Quaternion controllerRotation)
    {
        if (selectedTarget == null)
        {
            return;
        }

        selectedTarget.MoveFollowPoseBy(selector, controllerPosition, controllerRotation);
    }

    private static bool PressButton(
        VRButton button,
        object pointer,
        ref VRButton pressedButton)
    {
        if (button == null || !button.Interactable)
        {
            return false;
        }

        if (pressedButton != null && pressedButton != button)
        {
            pressedButton.CancelPressBy(pointer);
        }

        pressedButton = button;
        pressedButton.PressBy(pointer);
        return true;
    }

    private static void ReleaseButtonPress(ref VRButton pressedButton, object pointer)
    {
        if (pressedButton == null)
        {
            return;
        }

        pressedButton.ReleaseBy(pointer);
        pressedButton = null;
    }

    private static void CancelButtonPress(ref VRButton pressedButton, object pointer)
    {
        if (pressedButton == null)
        {
            return;
        }

        pressedButton.CancelPressBy(pointer);
        pressedButton = null;
    }

    private void ReleaseAllSelectedTargets()
    {
        Target leftReleasedTarget = leftSelectedTarget;
        ReleaseSelectedTarget(ref leftSelectedTarget, leftTriggerSelector);
        if (leftReleasedTarget != null)
        {
            RecordPlayerTargetReleased("Left", leftReleasedTarget);
        }

        Target rightReleasedTarget = rightSelectedTarget;
        ReleaseSelectedTarget(ref rightSelectedTarget, rightTriggerSelector);
        if (rightReleasedTarget != null)
        {
            RecordPlayerTargetReleased("Right", rightReleasedTarget);
        }
    }

    private void CancelAllButtonPresses()
    {
        CancelButtonPress(ref leftPressedButton, leftButtonPointer);
        CancelButtonPress(ref rightPressedButton, rightButtonPointer);
    }

    private void ApplyModeAvailability()
    {
        if (!precisionModeEnabled && precisionAimHand != PrecisionAimHand.None)
        {
            ExitPrecisionMode(precisionAimHand);
        }

        if (!CanUseFreeAimRay() && precisionAimHand == PrecisionAimHand.None)
        {
            ReleaseAllSelectedTargets();
            CancelAllButtonPresses();
            SetLeftButton(null);
            SetRightButton(null);
        }
    }

    private void EnterPrecisionMode(PrecisionAimHand aimHand)
    {
        if (!precisionModeEnabled || precisionAimHand != PrecisionAimHand.None)
        {
            return;
        }

        ReleaseAllSelectedTargets();
        CancelAllButtonPresses();
        precisionAimHand = aimHand;
        CapturePrecisionStartPose(aimHand);
        UpdatePrecisionCdGain();
        RecordPlayerEvent(GetPrecisionHandName(aimHand) + "_PrecisionModeStarted");
        RefreshDetectedTargetsForPrecisionMode(aimHand);
    }

    private void ExitPrecisionMode(PrecisionAimHand aimHand)
    {
        if (precisionAimHand != aimHand)
        {
            return;
        }

        ReleaseAllSelectedTargets();
        CancelAllButtonPresses();
        string handName = GetPrecisionHandName(aimHand);
        precisionAimHand = PrecisionAimHand.None;
        ResetPrecisionCdGain();
        RecordPlayerEvent(handName + "_PrecisionModeEnded");
        UpdateDetectedTargets();
        UpdateHoveredButtons();
    }

    private void RefreshDetectedTargetsForPrecisionMode(PrecisionAimHand aimHand)
    {
        if (aimHand == PrecisionAimHand.Left)
        {
            SetRightTarget(null);
            CancelButtonPress(ref rightPressedButton, rightButtonPointer);
            SetRightButton(null);
        }
        else if (aimHand == PrecisionAimHand.Right)
        {
            SetLeftTarget(null);
            CancelButtonPress(ref leftPressedButton, leftButtonPointer);
            SetLeftButton(null);
        }

        UpdateDetectedTargets();
        UpdateHoveredButtons();
    }

    private void CapturePrecisionStartPose(PrecisionAimHand aimHand)
    {
        precisionStartPosition = aimHand == PrecisionAimHand.Left
            ? leftControllerPosition
            : rightControllerPosition;

        precisionStartRotation = aimHand == PrecisionAimHand.Left
            ? leftControllerRotation
            : rightControllerRotation;

        Vector3 toBody = GetBodyReferencePosition() - precisionStartPosition;
        precisionPullDirection = toBody.sqrMagnitude > Mathf.Epsilon
            ? toBody.normalized
            : -(precisionStartRotation * Vector3.forward);

        ResetPrecisionCdGain();
    }

    private void UpdatePrecisionCdGain()
    {
        if (precisionAimHand == PrecisionAimHand.None)
        {
            ResetPrecisionCdGain();
            return;
        }

        Vector3 currentPosition = precisionAimHand == PrecisionAimHand.Left
            ? leftControllerPosition
            : rightControllerPosition;

        currentPrecisionPullDistance = Mathf.Max(
            0f,
            Vector3.Dot(currentPosition - precisionStartPosition, precisionPullDirection));

        currentPrecisionAmount = Mathf.Clamp01(currentPrecisionPullDistance / pullDistanceForMinGain);
        UpdatePrecisionGainValues();
        UpdatePrecisionVisualScale();
    }

    private void ResetPrecisionCdGain()
    {
        currentPrecisionPullDistance = 0f;
        currentPrecisionAmount = 0f;
        UpdatePrecisionGainValues();
        UpdatePrecisionVisualScale();
    }

    private void UpdatePrecisionGainValues()
    {
        currentRotationCdGain = GetPrecisionGain(maxRotationCdGain, minRotationCdGain);
        currentPositionCdGain = GetPrecisionGain(maxPositionCdGain, minPositionCdGain);
    }

    private float GetPrecisionGain(float maxGain, float minGain)
    {
        float lowGain = Mathf.Min(minGain, maxGain);
        float highGain = Mathf.Max(minGain, maxGain);
        return Mathf.Lerp(highGain, lowGain, currentPrecisionAmount);
    }

    private bool IsCurrentPrecisionNearMinimum(float minGainMultiplier)
    {
        if (precisionAimHand == PrecisionAimHand.None)
        {
            return false;
        }

        return ArePrecisionGainsNearMinimum(
            currentRotationCdGain,
            currentPositionCdGain,
            minGainMultiplier);
    }

    private bool ArePrecisionGainsNearMinimum(float rotationGain, float positionGain, float minGainMultiplier)
    {
        float multiplier = Mathf.Max(1f, minGainMultiplier);
        float minRotationGain = Mathf.Min(minRotationCdGain, maxRotationCdGain);
        float minPositionGain = Mathf.Min(minPositionCdGain, maxPositionCdGain);
        return rotationGain <= minRotationGain * multiplier
            && positionGain <= minPositionGain * multiplier;
    }

    private Vector3 GetBodyReferencePosition()
    {
        if (bodyReference != null)
        {
            return bodyReference.position;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform.position : transform.position;
    }

    private void UpdatePrecisionVisualScale()
    {
        currentRayThickness = GetPrecisionRayThickness();
        currentHitMarkerRadius = GetPrecisionHitMarkerRadius();
    }

    private void UpdateStudyLoggerPlayerSnapshot()
    {
        StudyLogger.SetPlayerSnapshot(
            assistModeEnabled,
            precisionModeEnabled,
            precisionAimHand != PrecisionAimHand.None,
            GetPrecisionHandName(precisionAimHand),
            currentRotationCdGain,
            currentPositionCdGain,
            currentPrecisionAmount);
    }

    private void RecordPlayerEvent(string action, string value = "")
    {
        UpdateStudyLoggerPlayerSnapshot();
        StudyLogger.RecordEvent(action, value);
    }

    private void RecordPlayerTargetDetected(string hand, Target target)
    {
        UpdateStudyLoggerPlayerSnapshot();
        StudyLogger.RecordTargetDetected(hand, target);
    }

    private void RecordPlayerTargetSelected(string hand, Target target)
    {
        UpdateStudyLoggerPlayerSnapshot();
        StudyLogger.RecordTargetSelected(hand, target);
    }

    private void RecordPlayerTargetReleased(string hand, Target target)
    {
        UpdateStudyLoggerPlayerSnapshot();
        StudyLogger.RecordTargetReleased(hand, target);
    }

    private static string GetPrecisionHandName(PrecisionAimHand aimHand)
    {
        return aimHand switch
        {
            PrecisionAimHand.Left => "Left",
            PrecisionAimHand.Right => "Right",
            _ => string.Empty
        };
    }

    private float GetPrecisionRayThickness()
    {
        float minThickness = Mathf.Max(0.001f, Mathf.Min(rayThicknessRange.x, rayThicknessRange.y));
        float maxThickness = Mathf.Max(minThickness, Mathf.Max(rayThicknessRange.x, rayThicknessRange.y));
        return Mathf.Lerp(maxThickness, minThickness, currentPrecisionAmount);
    }

    private float GetPrecisionHitMarkerRadius()
    {
        float minRadius = Mathf.Max(0.001f, Mathf.Min(hitMarkerRadiusRange.x, hitMarkerRadiusRange.y));
        float maxRadius = Mathf.Max(minRadius, Mathf.Max(hitMarkerRadiusRange.x, hitMarkerRadiusRange.y));
        return Mathf.Lerp(maxRadius, minRadius, currentPrecisionAmount);
    }

    private void OnLeftTriggerPressed()
    {
        bool hasPose = RefreshControllerPoseCache();
        if (hasPose)
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Left_TriggerPressed");
        if (!hasPose)
        {
            return;
        }

        UpdateDetectedTargets();
        UpdateHoveredButtons();
        if (PressButton(leftButton, leftButtonPointer, ref leftPressedButton))
        {
            RecordPlayerEvent("Left_ButtonPressed", leftPressedButton != null ? leftPressedButton.name : string.Empty);
            return;
        }

        if (!CanGrabTargetWithCurrentPrecision())
        {
            Target blockedTarget = GetLeftTriggerSelectionTarget();
            if (blockedTarget != null)
            {
                RecordPlayerEvent("Left_TargetGrabBlockedByPrecision", blockedTarget.name);
            }

            return;
        }

        GetLeftTriggerFollowPose(out Vector3 followPosition, out Quaternion followRotation);
        if (SelectDetectedTarget(
            GetLeftTriggerSelectionTarget(),
            leftTriggerSelector,
            ref leftSelectedTarget,
            followPosition,
            followRotation))
        {
            RecordPlayerTargetSelected("Left", leftSelectedTarget);
        }
    }

    private void OnLeftTriggerReleased()
    {
        if (RefreshControllerPoseCache())
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Left_TriggerReleased");
        if (leftPressedButton != null)
        {
            RecordPlayerEvent("Left_ButtonReleased", leftPressedButton.name);
        }

        ReleaseButtonPress(ref leftPressedButton, leftButtonPointer);
        Target releasedTarget = leftSelectedTarget;
        ReleaseSelectedTarget(ref leftSelectedTarget, leftTriggerSelector);
        if (releasedTarget != null)
        {
            RecordPlayerTargetReleased("Left", releasedTarget);
        }
    }

    private void OnLeftGripPressed()
    {
        bool hasPose = RefreshControllerPoseCache();
        if (hasPose)
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Left_GripPressed");
        if (!hasPose)
        {
            return;
        }

        EnterPrecisionMode(PrecisionAimHand.Left);
    }

    private void OnLeftGripReleased()
    {
        if (RefreshControllerPoseCache())
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Left_GripReleased");
        ExitPrecisionMode(PrecisionAimHand.Left);
    }

    private void OnRightTriggerPressed()
    {
        bool hasPose = RefreshControllerPoseCache();
        if (hasPose)
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Right_TriggerPressed");
        if (!hasPose)
        {
            return;
        }

        UpdateDetectedTargets();
        UpdateHoveredButtons();
        if (PressButton(rightButton, rightButtonPointer, ref rightPressedButton))
        {
            RecordPlayerEvent("Right_ButtonPressed", rightPressedButton != null ? rightPressedButton.name : string.Empty);
            return;
        }

        if (!CanGrabTargetWithCurrentPrecision())
        {
            Target blockedTarget = GetRightTriggerSelectionTarget();
            if (blockedTarget != null)
            {
                RecordPlayerEvent("Right_TargetGrabBlockedByPrecision", blockedTarget.name);
            }

            return;
        }

        GetRightTriggerFollowPose(out Vector3 followPosition, out Quaternion followRotation);
        if (SelectDetectedTarget(
            GetRightTriggerSelectionTarget(),
            rightTriggerSelector,
            ref rightSelectedTarget,
            followPosition,
            followRotation))
        {
            RecordPlayerTargetSelected("Right", rightSelectedTarget);
        }
    }

    private void OnRightTriggerReleased()
    {
        if (RefreshControllerPoseCache())
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Right_TriggerReleased");
        if (rightPressedButton != null)
        {
            RecordPlayerEvent("Right_ButtonReleased", rightPressedButton.name);
        }

        ReleaseButtonPress(ref rightPressedButton, rightButtonPointer);
        Target releasedTarget = rightSelectedTarget;
        ReleaseSelectedTarget(ref rightSelectedTarget, rightTriggerSelector);
        if (releasedTarget != null)
        {
            RecordPlayerTargetReleased("Right", releasedTarget);
        }
    }

    private void OnRightGripPressed()
    {
        bool hasPose = RefreshControllerPoseCache();
        if (hasPose)
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Right_GripPressed");
        if (!hasPose)
        {
            return;
        }

        EnterPrecisionMode(PrecisionAimHand.Right);
    }

    private void OnRightGripReleased()
    {
        if (RefreshControllerPoseCache())
        {
            UpdatePrecisionCdGain();
        }

        RecordPlayerEvent("Right_GripReleased");
        ExitPrecisionMode(PrecisionAimHand.Right);
    }
}
