using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

public class Target : MonoBehaviour
{
    public enum TargetState
    {
        Default,
        Detected,
        Selected
    }

    [SerializeField] private TargetState state = TargetState.Default;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color selectedColor = Color.green;
    [SerializeField, Min(0f)] private float colorTweenDuration = 0.2f;
    [SerializeField] private Ease colorTweenEase = Ease.OutQuad;

    [Header("Disappear")]
    [SerializeField, Min(0f)] private float disappearScaleDuration = 0.35f;
    [SerializeField] private Ease disappearScaleEase = Ease.InBack;

    [Header("Appear")]
    [SerializeField, Min(0f)] private float appearScaleDuration = 0.5f;
    [SerializeField] private Ease appearScaleEase = Ease.OutBack;

    private const float assistBubbleMinRadius = 0.25f;
    private const float assistBubblePadding = 0.15f;

    private float currentAssistBubbleRadius = 0.5f;

    [Header("Follow Physics")]
    [SerializeField] private bool preventOverlapWhileHeld = true;
    [SerializeField, Min(0f)] private float heldCollisionSkinWidth = 0.01f;
    [SerializeField, Min(0f)] private float heldMaxDepenetrationVelocity = 1f;
    [SerializeField, Min(0)] private int heldDepenetrationIterations = 2;
    [SerializeField] private LayerMask releaseDepenetrationMask = ~0;
    [SerializeField, Min(0)] private int releaseDepenetrationIterations = 4;
    [SerializeField, Min(0f)] private float releaseSkinWidth = 0.001f;

    [Header("Grounding")]
    [SerializeField] private LayerMask groundLayerMask = ~0;
    [SerializeField, Min(0.001f)] private float groundCheckDistance = 0.05f;
    [SerializeField, Range(0.1f, 1f)] private float groundCheckHorizontalScale = 0.9f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly List<Target> Targets = new List<Target>();

    private Material[] materials;
    private Color[] defaultColors;
    private int[] colorPropertyIds;
    private Sequence colorSequence;
    private readonly HashSet<object> detectors = new HashSet<object>();
    private readonly HashSet<object> selectors = new HashSet<object>();
    private readonly Collider[] overlapBuffer = new Collider[32];

    private Rigidbody targetRigidbody;
    private Collider[] targetColliders;
    private Vector3 originalLocalScale = Vector3.one;
    private Coroutine appearCoroutine;
    private Coroutine disappearCoroutine;
    private Tween appearTween;
    private Tween disappearTween;
    private object followSelector;
    private Vector3 followLocalPositionOffset;
    private Quaternion followLocalRotationOffset = Quaternion.identity;
    private Vector3 followPosition;
    private Quaternion followRotation = Quaternion.identity;
    private bool hasFollowPose;
    private bool physicsHeld;
    private bool originalUseGravity;
    private float originalMaxDepenetrationVelocity;
    private bool isDisappearing;

    public TargetState State => state;
    public bool IsDetected => state == TargetState.Detected;
    public bool IsSelected => state == TargetState.Selected;
    public bool IsGrounded => CheckGrounded();
    public static IReadOnlyList<Target> Instances => Targets;
    public bool AssistBubbleEnabled => !isDisappearing;
    public Vector3 AssistBubbleCenter => GetColliderCenterPosition();
    public float AssistBubbleRadius => currentAssistBubbleRadius;

    public static void ResetStaticState()
    {
        Targets.Clear();
    }

    private void Awake()
    {
        originalLocalScale = transform.localScale;
        InitializeMaterials();
        ApplyColorImmediately(state);
    }

    private void OnEnable()
    {
        if (!Targets.Contains(this))
        {
            Targets.Add(this);
        }

        Vector3 spawnPosition = AssistBubbleCenter;
        PlayAppear();
        GlobalManager.PlaySpawnEffect(spawnPosition);
        StudyLogger.RecordTargetSpawn(this);
        RefreshAllAssistBubbles();
    }

    private void Start()
    {
        CachePhysicsComponents();
        RefreshAllAssistBubbles();
    }

    private void OnDisable()
    {
        StopAppearTween();
        StopDisappearTween();
        Targets.Remove(this);
        RefreshAllAssistBubbles();
    }

    private void OnDestroy()
    {
        if (colorSequence.isAlive)
        {
            colorSequence.Stop();
        }

        StopDisappearTween();
        StopAppearTween();
    }

    private void OnValidate()
    {
        RefreshAssistBubbleRadius(Targets);
        RefreshAllAssistBubbles();
    }

    public static void RefreshAllAssistBubbles()
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            Target target = Targets[i];
            if (target != null && target.isActiveAndEnabled)
            {
                target.RefreshAssistBubbleRadius(Targets);
            }
        }
    }

    public void SetDetected(bool detected)
    {
        SetDetectedBy(this, detected);
    }

    public void SetDetectedBy(object detector, bool detected)
    {
        if (detector == null)
        {
            return;
        }

        if (detected)
        {
            detectors.Add(detector);
        }
        else
        {
            detectors.Remove(detector);
        }

        RefreshDetectionState();
    }

    public void ClearDetections()
    {
        if (detectors.Count == 0)
        {
            return;
        }

        detectors.Clear();
        RefreshDetectionState();
    }

    public void SetSelected(bool selected)
    {
        SetSelectedBy(this, selected);
    }

    public void SetSelectedBy(object selector, bool selected)
    {
        if (selector == null)
        {
            return;
        }

        if (selected)
        {
            selectors.Add(selector);
        }
        else
        {
            selectors.Remove(selector);
        }

        RefreshSelectionState();
    }

    public void BeginFollowBy(object selector, Vector3 controllerPosition, Quaternion controllerRotation)
    {
        if (selector == null)
        {
            return;
        }

        CachePhysicsComponents();
        SetSelectedBy(selector, true);
        followSelector = selector;
        followLocalPositionOffset = Quaternion.Inverse(controllerRotation) * (GetCurrentPosition() - controllerPosition);
        followLocalRotationOffset = Quaternion.Inverse(controllerRotation) * GetCurrentRotation();
        StartPhysicsHold();
        UpdateFollowPoseBy(selector, controllerPosition, controllerRotation);
    }

    public void UpdateFollowPoseBy(object selector, Vector3 controllerPosition, Quaternion controllerRotation)
    {
        if (followSelector != selector)
        {
            return;
        }

        followPosition = controllerPosition + controllerRotation * followLocalPositionOffset;
        followRotation = controllerRotation * followLocalRotationOffset;
        hasFollowPose = true;
    }

    public void MoveFollowPoseBy(object selector, Vector3 controllerPosition, Quaternion controllerRotation)
    {
        UpdateFollowPoseBy(selector, controllerPosition, controllerRotation);
        ApplyFollowPoseBy(selector);
    }

    public void EndFollowBy(object selector)
    {
        SetSelectedBy(selector, false);

        if (followSelector != selector)
        {
            return;
        }

        followSelector = null;
        hasFollowPose = false;
        EndPhysicsHold();
    }

    public float Disappear(float delay)
    {
        float safeDelay = Mathf.Max(0f, delay);

        StopAppearTween();
        StopPhysicalInteractionsImmediately();
        StopDisappearTween();

        if (!gameObject.activeInHierarchy)
        {
            transform.localScale = Vector3.zero;
            return 0f;
        }

        disappearCoroutine = StartCoroutine(DisappearRoutine(safeDelay));
        return safeDelay + disappearScaleDuration;
    }

    public void SetState(TargetState nextState)
    {
        if (state == TargetState.Selected && nextState == TargetState.Detected && selectors.Count > 0)
        {
            return;
        }

        if (state == nextState)
        {
            return;
        }

        state = nextState;
        PlayColorSequence(state);
    }

    private void RefreshSelectionState()
    {
        if (selectors.Count > 0)
        {
            SetState(TargetState.Selected);
            return;
        }

        SetState(detectors.Count > 0 ? TargetState.Detected : TargetState.Default);
    }

    private void RefreshDetectionState()
    {
        if (state == TargetState.Selected)
        {
            return;
        }

        SetState(detectors.Count > 0 ? TargetState.Detected : TargetState.Default);
    }

    private void CachePhysicsComponents()
    {
        if (targetRigidbody == null)
        {
            targetRigidbody = GetComponent<Rigidbody>();
        }

        if (targetColliders == null || targetColliders.Length == 0)
        {
            targetColliders = GetComponentsInChildren<Collider>(true);
        }
    }

    private void PlayAppear()
    {
        StopAppearTween();
        StopDisappearTween();

        isDisappearing = false;
        transform.localScale = Vector3.zero;

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        appearCoroutine = StartCoroutine(AppearRoutine());
        RefreshAllAssistBubbles();
    }

    private IEnumerator AppearRoutine()
    {
        if (appearScaleDuration <= 0f)
        {
            transform.localScale = originalLocalScale;
            appearCoroutine = null;
            RefreshAllAssistBubbles();
            yield break;
        }

        appearTween = Tween.Scale(transform, originalLocalScale, appearScaleDuration, appearScaleEase);
        while (appearTween.isAlive)
        {
            yield return null;
        }

        transform.localScale = originalLocalScale;
        appearCoroutine = null;
        RefreshAllAssistBubbles();
    }

    private IEnumerator DisappearRoutine(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (disappearScaleDuration <= 0f)
        {
            transform.localScale = Vector3.zero;
            disappearCoroutine = null;
            yield break;
        }

        disappearTween = Tween.Scale(transform, Vector3.zero, disappearScaleDuration, disappearScaleEase);
        while (disappearTween.isAlive)
        {
            yield return null;
        }

        disappearCoroutine = null;
    }

    private void StopPhysicalInteractionsImmediately()
    {
        isDisappearing = true;
        ClearInteractions();
        StopPhysicalInteractionsForDisappear();
        RefreshAllAssistBubbles();
    }

    private void ClearInteractions()
    {
        detectors.Clear();
        selectors.Clear();
        state = TargetState.Default;
        followSelector = null;
        hasFollowPose = false;
        physicsHeld = false;

        if (colorSequence.isAlive)
        {
            colorSequence.Stop();
        }

        ApplyColorImmediately(state);
    }

    private void StopPhysicalInteractionsForDisappear()
    {
        CachePhysicsComponents();

        if (targetRigidbody != null)
        {
            targetRigidbody.linearVelocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            targetRigidbody.useGravity = false;
            targetRigidbody.isKinematic = true;
            targetRigidbody.detectCollisions = false;
        }

        if (targetColliders != null)
        {
            for (int i = 0; i < targetColliders.Length; i++)
            {
                if (targetColliders[i] != null)
                {
                    targetColliders[i].enabled = false;
                }
            }
        }
    }

    private void StopAppearTween()
    {
        if (appearCoroutine != null)
        {
            StopCoroutine(appearCoroutine);
            appearCoroutine = null;
        }

        if (appearTween.isAlive)
        {
            appearTween.Stop();
        }
    }

    private void StopDisappearTween()
    {
        if (disappearCoroutine != null)
        {
            StopCoroutine(disappearCoroutine);
            disappearCoroutine = null;
        }

        if (disappearTween.isAlive)
        {
            disappearTween.Stop();
        }
    }

    private bool CheckGrounded()
    {
        if (isDisappearing)
        {
            return false;
        }

        CachePhysicsComponents();
        if (targetColliders == null || targetColliders.Length == 0)
        {
            return false;
        }

        float checkDistance = Mathf.Max(0.001f, groundCheckDistance);
        float horizontalScale = Mathf.Clamp(groundCheckHorizontalScale, 0.1f, 1f);

        for (int i = 0; i < targetColliders.Length; i++)
        {
            Collider ownCollider = targetColliders[i];
            if (ownCollider == null || !ownCollider.enabled)
            {
                continue;
            }

            Bounds bounds = ownCollider.bounds;
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.001f, bounds.extents.x * horizontalScale),
                checkDistance * 0.5f,
                Mathf.Max(0.001f, bounds.extents.z * horizontalScale));
            Vector3 center = new Vector3(bounds.center.x, bounds.min.y - halfExtents.y, bounds.center.z);

            int overlapCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                overlapBuffer,
                Quaternion.identity,
                groundLayerMask,
                QueryTriggerInteraction.Ignore);

            for (int j = 0; j < overlapCount; j++)
            {
                Collider otherCollider = overlapBuffer[j];
                if (otherCollider == null || otherCollider == ownCollider || otherCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private void RefreshAssistBubbleRadius(IReadOnlyList<Target> targets)
    {
        if (!AssistBubbleEnabled)
        {
            currentAssistBubbleRadius = 0f;
            return;
        }

        float maxRadius = GetMaxAssistBubbleRadius();
        float adjustedRadius = maxRadius;
        Vector3 center = AssistBubbleCenter;

        for (int i = 0; i < targets.Count; i++)
        {
            Target other = targets[i];
            if (other == null || other == this || !other.AssistBubbleEnabled || !other.isActiveAndEnabled)
            {
                continue;
            }

            float otherMaxRadius = other.GetMaxAssistBubbleRadius();
            float radiusSum = maxRadius + otherMaxRadius;
            if (radiusSum <= Mathf.Epsilon)
            {
                continue;
            }

            float centerDistance = Vector3.Distance(center, other.AssistBubbleCenter);
            if (centerDistance >= radiusSum)
            {
                continue;
            }

            float nonOverlapRadius = maxRadius * Mathf.Clamp01(centerDistance / radiusSum);
            adjustedRadius = Mathf.Min(adjustedRadius, nonOverlapRadius);
        }

        currentAssistBubbleRadius = Mathf.Max(0f, adjustedRadius);
    }

    private float GetMaxAssistBubbleRadius()
    {
        return Mathf.Max(GetTargetScaleRadius() + assistBubblePadding, assistBubbleMinRadius);
    }

    private float GetTargetScaleRadius()
    {
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private void ApplyFollowPoseBy(object selector)
    {
        if (followSelector != selector || !hasFollowPose)
        {
            return;
        }

        if (targetRigidbody != null)
        {
            if (preventOverlapWhileHeld)
            {
                ResolvePenetrations(heldDepenetrationIterations, heldCollisionSkinWidth);
            }

            Vector3 nextPosition = preventOverlapWhileHeld
                ? GetCollisionConstrainedPosition(followPosition)
                : followPosition;

            targetRigidbody.MovePosition(nextPosition);
            targetRigidbody.MoveRotation(followRotation);
        }
        else
        {
            transform.SetPositionAndRotation(followPosition, followRotation);
        }
    }

    private Vector3 GetCurrentPosition()
    {
        return targetRigidbody != null ? targetRigidbody.position : transform.position;
    }

    private Vector3 GetColliderCenterPosition()
    {
        CachePhysicsComponents();
        if (targetColliders == null || targetColliders.Length == 0)
        {
            return GetCurrentPosition();
        }

        Bounds combinedBounds = default;
        bool hasBounds = false;
        for (int i = 0; i < targetColliders.Length; i++)
        {
            Collider targetCollider = targetColliders[i];
            if (targetCollider == null || !targetCollider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = targetCollider.bounds;
                hasBounds = true;
                continue;
            }

            combinedBounds.Encapsulate(targetCollider.bounds);
        }

        return hasBounds ? combinedBounds.center : GetCurrentPosition();
    }

    private Quaternion GetCurrentRotation()
    {
        return targetRigidbody != null ? targetRigidbody.rotation : transform.rotation;
    }

    private void StartPhysicsHold()
    {
        if (targetRigidbody == null || physicsHeld)
        {
            return;
        }

        originalUseGravity = targetRigidbody.useGravity;
        originalMaxDepenetrationVelocity = targetRigidbody.maxDepenetrationVelocity;
        targetRigidbody.useGravity = false;
        targetRigidbody.maxDepenetrationVelocity = heldMaxDepenetrationVelocity;
        targetRigidbody.linearVelocity = Vector3.zero;
        targetRigidbody.angularVelocity = Vector3.zero;
        physicsHeld = true;
    }

    private void EndPhysicsHold()
    {
        if (targetRigidbody == null || !physicsHeld)
        {
            return;
        }

        ResolvePenetrations(releaseDepenetrationIterations, releaseSkinWidth);
        targetRigidbody.linearVelocity = Vector3.zero;
        targetRigidbody.angularVelocity = Vector3.zero;
        targetRigidbody.useGravity = originalUseGravity;
        targetRigidbody.maxDepenetrationVelocity = originalMaxDepenetrationVelocity;
        physicsHeld = false;
    }

    private Vector3 GetCollisionConstrainedPosition(Vector3 targetPosition)
    {
        Vector3 currentPosition = targetRigidbody.position;
        Vector3 movement = targetPosition - currentPosition;
        float distance = movement.magnitude;

        if (distance <= Mathf.Epsilon)
        {
            return targetPosition;
        }

        Vector3 direction = movement / distance;
        if (!targetRigidbody.SweepTest(direction, out RaycastHit hit, distance + heldCollisionSkinWidth, QueryTriggerInteraction.Ignore))
        {
            return targetPosition;
        }

        float safeDistance = Mathf.Max(0f, hit.distance - heldCollisionSkinWidth);
        Vector3 safePosition = currentPosition + direction * Mathf.Min(safeDistance, distance);
        Vector3 remainingMovement = targetPosition - safePosition;
        float blockedAmount = Vector3.Dot(remainingMovement, hit.normal);

        if (blockedAmount < 0f)
        {
            remainingMovement -= hit.normal * blockedAmount;
        }

        Vector3 slideTargetPosition = safePosition + remainingMovement;
        Vector3 slideMovement = slideTargetPosition - currentPosition;
        float slideDistance = slideMovement.magnitude;

        if (slideDistance <= Mathf.Epsilon)
        {
            return safePosition;
        }

        Vector3 slideDirection = slideMovement / slideDistance;
        if (!targetRigidbody.SweepTest(slideDirection, out RaycastHit slideHit, slideDistance + heldCollisionSkinWidth, QueryTriggerInteraction.Ignore))
        {
            return slideTargetPosition;
        }

        float slideSafeDistance = Mathf.Max(0f, slideHit.distance - heldCollisionSkinWidth);
        return currentPosition + slideDirection * Mathf.Min(slideSafeDistance, slideDistance);
    }

    private void ResolvePenetrations(int iterationCount, float skinWidth)
    {
        if (iterationCount <= 0 || targetColliders == null || targetColliders.Length == 0)
        {
            return;
        }

        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            bool moved = false;
            for (int i = 0; i < targetColliders.Length; i++)
            {
                Collider ownCollider = targetColliders[i];
                if (ownCollider == null || !ownCollider.enabled)
                {
                    continue;
                }

                Bounds bounds = ownCollider.bounds;
                int overlapCount = Physics.OverlapBoxNonAlloc(
                    bounds.center,
                    bounds.extents + Vector3.one * skinWidth,
                    overlapBuffer,
                    Quaternion.identity,
                    releaseDepenetrationMask,
                    QueryTriggerInteraction.Ignore);

                for (int j = 0; j < overlapCount; j++)
                {
                    Collider otherCollider = overlapBuffer[j];
                    if (otherCollider == null || otherCollider == ownCollider || otherCollider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                        ownCollider,
                        ownCollider.transform.position,
                        ownCollider.transform.rotation,
                        otherCollider,
                        otherCollider.transform.position,
                        otherCollider.transform.rotation,
                        out Vector3 direction,
                        out float distance))
                    {
                        continue;
                    }

                    MoveBy(direction * (distance + skinWidth));
                    moved = true;
                    Physics.SyncTransforms();
                }
            }

            if (!moved)
            {
                break;
            }
        }
    }

    private void MoveBy(Vector3 offset)
    {
        if (targetRigidbody != null)
        {
            targetRigidbody.position += offset;
        }
        else
        {
            transform.position += offset;
        }
    }

    private void InitializeMaterials()
    {
        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>();
        }

        materials = new Material[renderers.Length];
        defaultColors = new Color[renderers.Length];
        colorPropertyIds = new int[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            materials[i] = renderers[i].material;
            int colorPropertyId = materials[i].HasProperty(BaseColorId) ? BaseColorId : ColorId;
            colorPropertyIds[i] = colorPropertyId;

            defaultColors[i] = materials[i].HasProperty(colorPropertyId)
                ? materials[i].GetColor(colorPropertyId)
                : Color.white;
        }
    }

    private void PlayColorSequence(TargetState targetState)
    {
        if (materials == null || materials.Length == 0)
        {
            InitializeMaterials();
        }

        if (colorSequence.isAlive)
        {
            colorSequence.Stop();
        }

        colorSequence = Sequence.Create();
        for (int i = 0; i < materials.Length; i++)
        {
            int colorPropertyId = colorPropertyIds[i];
            if (materials[i] == null || !materials[i].HasProperty(colorPropertyId))
            {
                continue;
            }

            Color color = GetColorForState(targetState, i);
            colorSequence.Group(Tween.MaterialColor(materials[i], colorPropertyId, color, colorTweenDuration, colorTweenEase));
        }
    }

    private void ApplyColorImmediately(TargetState targetState)
    {
        if (materials == null)
        {
            return;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            int colorPropertyId = colorPropertyIds[i];
            if (materials[i] != null && materials[i].HasProperty(colorPropertyId))
            {
                Color color = GetColorForState(targetState, i);
                materials[i].SetColor(colorPropertyId, color);
            }
        }
    }

    private Color GetColorForState(TargetState targetState, int materialIndex)
    {
        return targetState switch
        {
            TargetState.Selected => selectedColor,
            _ => defaultColors[materialIndex]
        };
    }
}
