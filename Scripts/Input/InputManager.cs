using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Tracking Space")]
    [SerializeField] private Transform trackingSpace;
    [SerializeField] private bool applyAsWorldPose = true;
    [SerializeField] private Vector3 trackingPositionScale = Vector3.one;
    [SerializeField] private Vector3 trackingPositionOffset;

    [Header("Left Controller")]
    [SerializeField] private InputActionReference leftTrigger;
    [SerializeField] private InputActionReference leftGrip;
    [SerializeField] private InputActionReference leftPosition;
    [SerializeField] private InputActionReference leftRotation;

    [Header("Right Controller")]
    [SerializeField] private InputActionReference rightTrigger;
    [SerializeField] private InputActionReference rightGrip;
    [SerializeField] private InputActionReference rightPosition;
    [SerializeField] private InputActionReference rightRotation;

    [Header("Keyboard Mouse Simulation")]
    [SerializeField] private bool keyboardMouseSimulationEnabled = true;
    [SerializeField] private bool editorOnlyKeyboardMouseSimulation = true;
    [SerializeField] private bool useKeyboardMouseWhenVrPoseMissing = true;
    [SerializeField] private bool forceKeyboardMouseSimulation;
    [SerializeField] private Camera keyboardMouseCamera;
    [SerializeField] private bool useFixedForwardReferenceForKeyboardMouse = true;
    [SerializeField] private Vector3 keyboardMouseHiddenLeftHandOffset = new Vector3(-1.2f, -0.9f, 1.2f);
    [SerializeField] private Vector3 keyboardMouseRightHandOffset = new Vector3(5.5f, -0.85f, 5f);
    [SerializeField] private Vector3 keyboardMouseRayRotationOffset = new Vector3(90f, 0f, 0f);
    [SerializeField, Min(0f)] private float keyboardMouseMoveSpeed = 1.5f;
    [SerializeField, Min(1f)] private float keyboardMouseFastMoveMultiplier = 3f;
    [SerializeField, Min(0f)] private float keyboardMouseScrollMoveSpeed = 0.35f;

    private event System.Action leftTriggerPressedCallbacks;
    private event System.Action leftTriggerReleasedCallbacks;
    private event System.Action leftGripPressedCallbacks;
    private event System.Action leftGripReleasedCallbacks;
    private event System.Action rightTriggerPressedCallbacks;
    private event System.Action rightTriggerReleasedCallbacks;
    private event System.Action rightGripPressedCallbacks;
    private event System.Action rightGripReleasedCallbacks;

    public Vector3 LeftControllerPosition { get; private set; }
    public Quaternion LeftControllerRotation { get; private set; } = Quaternion.identity;
    public Vector3 RightControllerPosition { get; private set; }
    public Quaternion RightControllerRotation { get; private set; } = Quaternion.identity;
    public bool IsKeyboardMouseSimulationActive { get; private set; }

    public static void ResetStaticState()
    {
        if (Instance != null)
        {
            Instance.leftTriggerPressedCallbacks = null;
            Instance.leftTriggerReleasedCallbacks = null;
            Instance.leftGripPressedCallbacks = null;
            Instance.leftGripReleasedCallbacks = null;
            Instance.rightTriggerPressedCallbacks = null;
            Instance.rightTriggerReleasedCallbacks = null;
            Instance.rightGripPressedCallbacks = null;
            Instance.rightGripReleasedCallbacks = null;
        }

        Instance = null;
    }

    private bool keyboardMouseSimulationInitialized;
    private bool keyboardMouseTriggerHeld;
    private bool keyboardMouseGripHeld;
    private Vector3 keyboardMouseLeftMutableOffset;
    private Vector3 keyboardMouseRightMutableOffset;
    private Vector3 keyboardMouseReferencePosition;
    private Quaternion keyboardMouseReferenceRotation = Quaternion.identity;
    private Vector3 keyboardMouseLeftWorldPosition;
    private Vector3 keyboardMouseRightWorldPosition;
    private Quaternion keyboardMouseLeftWorldRotation = Quaternion.identity;
    private Quaternion keyboardMouseRightWorldRotation = Quaternion.identity;
    private int keyboardMousePoseFrame = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("InputManager: Multiple instances exist. The latest one will become Instance.");
        }

        Instance = this;
    }

    private void OnEnable()
    {
        Register(leftTrigger, OnLeftTriggerPressed, OnLeftTriggerReleased, "Left Trigger");
        Register(leftGrip, OnLeftGripPressed, OnLeftGripReleased, "Left Grip");
        Register(rightTrigger, OnRightTriggerPressed, OnRightTriggerReleased, "Right Trigger");
        Register(rightGrip, OnRightGripPressed, OnRightGripReleased, "Right Grip");

        EnableValueAction(leftPosition, "Left Position");
        EnableValueAction(leftRotation, "Left Rotation");
        EnableValueAction(rightPosition, "Right Position");
        EnableValueAction(rightRotation, "Right Rotation");
    }

    private void OnDisable()
    {
        ReleaseKeyboardMouseSimulationButtons();

        Unregister(leftTrigger, OnLeftTriggerPressed, OnLeftTriggerReleased);
        Unregister(leftGrip, OnLeftGripPressed, OnLeftGripReleased);
        Unregister(rightTrigger, OnRightTriggerPressed, OnRightTriggerReleased);
        Unregister(rightGrip, OnRightGripPressed, OnRightGripReleased);

        DisableValueAction(leftPosition);
        DisableValueAction(leftRotation);
        DisableValueAction(rightPosition);
        DisableValueAction(rightRotation);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        RefreshControllerPoses();
        RefreshKeyboardMouseSimulationButtons();
    }

    private void FixedUpdate()
    {
        RefreshControllerPoses();
    }

    public void RefreshControllerPoses()
    {
        if (ShouldUseKeyboardMouseSimulation())
        {
            IsKeyboardMouseSimulationActive = true;
            RefreshKeyboardMouseSimulationPose();
            LeftControllerPosition = keyboardMouseLeftWorldPosition;
            LeftControllerRotation = keyboardMouseLeftWorldRotation;
            RightControllerPosition = keyboardMouseRightWorldPosition;
            RightControllerRotation = keyboardMouseRightWorldRotation;
            return;
        }

        IsKeyboardMouseSimulationActive = false;

        ApplyControllerPose(
            leftPosition,
            leftRotation,
            out Vector3 leftFinalPosition,
            out Quaternion leftFinalRotation);

        ApplyControllerPose(
            rightPosition,
            rightRotation,
            out Vector3 rightFinalPosition,
            out Quaternion rightFinalRotation);

        LeftControllerPosition = leftFinalPosition;
        LeftControllerRotation = leftFinalRotation;
        RightControllerPosition = rightFinalPosition;
        RightControllerRotation = rightFinalRotation;
    }

    private bool ShouldUseKeyboardMouseSimulation()
    {
        if (!IsKeyboardMouseSimulationAllowed())
        {
            return false;
        }

        if (forceKeyboardMouseSimulation)
        {
            return true;
        }

        return useKeyboardMouseWhenVrPoseMissing && !HasAnyTrackedControllerPose();
    }

    private bool IsKeyboardMouseSimulationAllowed()
    {
        if (!keyboardMouseSimulationEnabled)
        {
            return false;
        }

        return !editorOnlyKeyboardMouseSimulation || Application.isEditor;
    }

    private bool HasAnyTrackedControllerPose()
    {
        return HasTrackedControllerPose(leftPosition, leftRotation)
            || HasTrackedControllerPose(rightPosition, rightRotation);
    }

    private static bool HasTrackedControllerPose(
        InputActionReference positionAction,
        InputActionReference rotationAction)
    {
        if (positionAction != null && positionAction.action != null)
        {
            Vector3 position = positionAction.action.ReadValue<Vector3>();
            if (position.sqrMagnitude > 0.0001f)
            {
                return true;
            }
        }

        if (rotationAction != null && rotationAction.action != null)
        {
            Quaternion rotation = rotationAction.action.ReadValue<Quaternion>();
            bool hasRotationValue =
                Mathf.Abs(rotation.x) > 0.0001f
                || Mathf.Abs(rotation.y) > 0.0001f
                || Mathf.Abs(rotation.z) > 0.0001f
                || Mathf.Abs(rotation.w) > 0.0001f;
            if (hasRotationValue && Quaternion.Angle(rotation, Quaternion.identity) > 0.1f)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshKeyboardMouseSimulationPose()
    {
        EnsureKeyboardMouseSimulationInitialized();

        if (keyboardMousePoseFrame == Time.frameCount)
        {
            return;
        }

        keyboardMousePoseFrame = Time.frameCount;
        UpdateKeyboardMouseSimulationOffsetInput();

        Camera camera = GetKeyboardMouseCamera();
        keyboardMouseLeftWorldPosition =
            keyboardMouseReferencePosition + keyboardMouseReferenceRotation * keyboardMouseLeftMutableOffset;
        keyboardMouseRightWorldPosition =
            keyboardMouseReferencePosition + keyboardMouseReferenceRotation * keyboardMouseRightMutableOffset;

        Ray aimRay = GetKeyboardMouseAimRay(camera);
        Vector3 up = keyboardMouseReferenceRotation * Vector3.up;

        keyboardMouseLeftWorldRotation = GetKeyboardMouseControllerRotation(aimRay.direction, up);
        keyboardMouseRightWorldRotation = keyboardMouseLeftWorldRotation;
    }

    private void RefreshKeyboardMouseSimulationButtons()
    {
        Keyboard keyboard = Keyboard.current;
        if (IsKeyboardMouseSimulationAllowed() && keyboard != null && keyboard.f3Key.wasPressedThisFrame)
        {
            forceKeyboardMouseSimulation = !forceKeyboardMouseSimulation;
            Debug.Log($"InputManager: Keyboard/mouse simulation force mode = {forceKeyboardMouseSimulation}");
        }

        if (!ShouldUseKeyboardMouseSimulation())
        {
            ReleaseKeyboardMouseSimulationButtons();
            return;
        }

        EnsureKeyboardMouseSimulationInitialized();
        Mouse mouse = Mouse.current;

        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame)
            {
                CaptureKeyboardMouseReferencePose();
                ResetKeyboardMouseSimulationOffsets();
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                FindFirstObjectByType<Player>()?.SetAllLayerInteractionMode();
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                FindFirstObjectByType<Player>()?.SetUiOnlyInteractionMode();
            }
        }

        if (mouse == null)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            SetKeyboardMouseTriggerHeld(true);
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            SetKeyboardMouseTriggerHeld(false);
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            SetKeyboardMouseGripHeld(true);
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            SetKeyboardMouseGripHeld(false);
        }
    }

    private void EnsureKeyboardMouseSimulationInitialized()
    {
        if (keyboardMouseSimulationInitialized)
        {
            return;
        }

        keyboardMouseSimulationInitialized = true;
        CaptureKeyboardMouseReferencePose();
        ResetKeyboardMouseSimulationOffsets();
    }

    private void ResetKeyboardMouseSimulationOffsets()
    {
        keyboardMouseLeftMutableOffset = keyboardMouseHiddenLeftHandOffset;
        keyboardMouseRightMutableOffset = keyboardMouseRightHandOffset;
        keyboardMousePoseFrame = -1;
    }

    private void CaptureKeyboardMouseReferencePose()
    {
        Transform source = GetKeyboardMouseReferenceSource();
        keyboardMouseReferencePosition = source != null ? source.position : transform.position;

        Quaternion sourceRotation = source != null ? source.rotation : transform.rotation;
        if (!useFixedForwardReferenceForKeyboardMouse)
        {
            keyboardMouseReferenceRotation = sourceRotation;
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(sourceRotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        keyboardMouseReferenceRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private Transform GetKeyboardMouseReferenceSource()
    {
        if (keyboardMouseCamera != null)
        {
            return keyboardMouseCamera.transform;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform;
        }

        return trackingSpace != null ? trackingSpace : transform;
    }

    private void UpdateKeyboardMouseSimulationOffsetInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        Vector3 movement = Vector3.zero;
        if (keyboard.aKey.isPressed)
        {
            movement.x -= 1f;
        }

        if (keyboard.dKey.isPressed)
        {
            movement.x += 1f;
        }

        if (keyboard.qKey.isPressed)
        {
            movement.y -= 1f;
        }

        if (keyboard.eKey.isPressed)
        {
            movement.y += 1f;
        }

        if (keyboard.sKey.isPressed)
        {
            movement.z -= 1f;
        }

        if (keyboard.wKey.isPressed)
        {
            movement.z += 1f;
        }

        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        float moveSpeed = keyboardMouseMoveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            moveSpeed *= keyboardMouseFastMoveMultiplier;
        }

        Vector3 offsetDelta = movement * (moveSpeed * Time.deltaTime);
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            offsetDelta.z += mouse.scroll.ReadValue().y * keyboardMouseScrollMoveSpeed * 0.01f;
        }

        if (offsetDelta == Vector3.zero)
        {
            return;
        }

        keyboardMouseRightMutableOffset += offsetDelta;
    }

    private Camera GetKeyboardMouseCamera()
    {
        if (keyboardMouseCamera != null)
        {
            return keyboardMouseCamera;
        }

        return Camera.main;
    }

    private Ray GetKeyboardMouseAimRay(Camera camera)
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 screenSize = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            Vector2 viewport = mouse.position.ReadValue();
            float x = Mathf.Clamp01(viewport.x / screenSize.x) * 2f - 1f;
            float y = Mathf.Clamp01(viewport.y / screenSize.y) * 2f - 1f;

            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            float aspect = camera != null ? camera.aspect : screenSize.x / screenSize.y;
            float vertical = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 localDirection = new Vector3(x * aspect * vertical, y * vertical, 1f).normalized;
            return new Ray(keyboardMouseReferencePosition, keyboardMouseReferenceRotation * localDirection);
        }

        return new Ray(keyboardMouseReferencePosition, keyboardMouseReferenceRotation * Vector3.forward);
    }

    private Quaternion GetKeyboardMouseControllerRotation(Vector3 direction, Vector3 up)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        Vector3 safeUp = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        if (Vector3.Cross(direction, safeUp).sqrMagnitude < 0.0001f)
        {
            safeUp = Vector3.Cross(Vector3.right, direction).normalized;
            if (safeUp.sqrMagnitude < 0.0001f)
            {
                safeUp = Vector3.up;
            }
        }

        Quaternion rayRotation = Quaternion.LookRotation(direction, safeUp);
        return rayRotation * Quaternion.Inverse(Quaternion.Euler(keyboardMouseRayRotationOffset));
    }

    private void SetKeyboardMouseTriggerHeld(bool held)
    {
        if (keyboardMouseTriggerHeld == held)
        {
            return;
        }

        keyboardMouseTriggerHeld = held;
        if (held)
        {
            InvokeRightTriggerPressed();
        }
        else
        {
            InvokeRightTriggerReleased();
        }
    }

    private void SetKeyboardMouseGripHeld(bool held)
    {
        if (keyboardMouseGripHeld == held)
        {
            return;
        }

        keyboardMouseGripHeld = held;
        if (held)
        {
            InvokeRightGripPressed();
        }
        else
        {
            InvokeRightGripReleased();
        }
    }

    private void ReleaseKeyboardMouseSimulationButtons()
    {
        SetKeyboardMouseTriggerHeld(false);
        SetKeyboardMouseGripHeld(false);
    }

    private static void Register(
        InputActionReference actionReference,
        System.Action<InputAction.CallbackContext> pressed,
        System.Action<InputAction.CallbackContext> released,
        string label)
    {
        if (actionReference == null || actionReference.action == null)
        {
            Debug.LogWarning($"InputManager: {label} action is not assigned.");
            return;
        }

        InputAction action = actionReference.action;
        action.started += pressed;
        action.canceled += released;
        action.Enable();
    }

    private static void EnableValueAction(InputActionReference actionReference, string label)
    {
        if (actionReference == null || actionReference.action == null)
        {
            Debug.LogWarning($"InputManager: {label} action is not assigned.");
            return;
        }

        actionReference.action.Enable();
    }

    private static void Unregister(
        InputActionReference actionReference,
        System.Action<InputAction.CallbackContext> pressed,
        System.Action<InputAction.CallbackContext> released)
    {
        if (actionReference == null || actionReference.action == null)
        {
            return;
        }

        InputAction action = actionReference.action;
        action.started -= pressed;
        action.canceled -= released;
        action.Disable();
    }

    private static void DisableValueAction(InputActionReference actionReference)
    {
        if (actionReference == null || actionReference.action == null)
        {
            return;
        }

        actionReference.action.Disable();
    }

    private void ApplyControllerPose(
        InputActionReference positionAction,
        InputActionReference rotationAction,
        out Vector3 finalPosition,
        out Quaternion finalRotation)
    {
        finalPosition = Vector3.zero;
        finalRotation = Quaternion.identity;

        Vector3 trackingPosition = Vector3.zero;
        Quaternion trackingRotation = Quaternion.identity;

        if (positionAction != null && positionAction.action != null)
        {
            trackingPosition = positionAction.action.ReadValue<Vector3>();
        }

        if (rotationAction != null && rotationAction.action != null)
        {
            trackingRotation = rotationAction.action.ReadValue<Quaternion>();
        }

        trackingPosition = Vector3.Scale(trackingPosition, trackingPositionScale) + trackingPositionOffset;

        if (applyAsWorldPose && trackingSpace != null)
        {
            finalPosition = trackingSpace.TransformPoint(trackingPosition);
            finalRotation = trackingSpace.rotation * trackingRotation;
        }
        else if (applyAsWorldPose)
        {
            finalPosition = trackingPosition;
            finalRotation = trackingRotation;
        }
        else
        {
            finalPosition = trackingPosition;
            finalRotation = trackingRotation;
        }
    }

    public static void RegisterLeftTriggerPressed(System.Action callback) { if (HasInstance()) Instance.leftTriggerPressedCallbacks += callback; }
    public static void UnregisterLeftTriggerPressed(System.Action callback) { if (HasInstance()) Instance.leftTriggerPressedCallbacks -= callback; }
    public static void RegisterLeftTriggerReleased(System.Action callback) { if (HasInstance()) Instance.leftTriggerReleasedCallbacks += callback; }
    public static void UnregisterLeftTriggerReleased(System.Action callback) { if (HasInstance()) Instance.leftTriggerReleasedCallbacks -= callback; }
    public static void RegisterLeftGripPressed(System.Action callback) { if (HasInstance()) Instance.leftGripPressedCallbacks += callback; }
    public static void UnregisterLeftGripPressed(System.Action callback) { if (HasInstance()) Instance.leftGripPressedCallbacks -= callback; }
    public static void RegisterLeftGripReleased(System.Action callback) { if (HasInstance()) Instance.leftGripReleasedCallbacks += callback; }
    public static void UnregisterLeftGripReleased(System.Action callback) { if (HasInstance()) Instance.leftGripReleasedCallbacks -= callback; }
    public static void RegisterRightTriggerPressed(System.Action callback) { if (HasInstance()) Instance.rightTriggerPressedCallbacks += callback; }
    public static void UnregisterRightTriggerPressed(System.Action callback) { if (HasInstance()) Instance.rightTriggerPressedCallbacks -= callback; }
    public static void RegisterRightTriggerReleased(System.Action callback) { if (HasInstance()) Instance.rightTriggerReleasedCallbacks += callback; }
    public static void UnregisterRightTriggerReleased(System.Action callback) { if (HasInstance()) Instance.rightTriggerReleasedCallbacks -= callback; }
    public static void RegisterRightGripPressed(System.Action callback) { if (HasInstance()) Instance.rightGripPressedCallbacks += callback; }
    public static void UnregisterRightGripPressed(System.Action callback) { if (HasInstance()) Instance.rightGripPressedCallbacks -= callback; }
    public static void RegisterRightGripReleased(System.Action callback) { if (HasInstance()) Instance.rightGripReleasedCallbacks += callback; }
    public static void UnregisterRightGripReleased(System.Action callback) { if (HasInstance()) Instance.rightGripReleasedCallbacks -= callback; }

    private static bool HasInstance()
    {
        if (Instance != null)
        {
            return true;
        }

        Debug.LogWarning("InputManager: Instance is not ready.");
        return false;
    }

    private void OnLeftTriggerPressed(InputAction.CallbackContext context)
    {
        InvokeLeftTriggerPressed();
    }

    private void InvokeLeftTriggerPressed()
    {
        Debug.Log("Left Trigger Pressed");
        leftTriggerPressedCallbacks?.Invoke();
    }

    private void OnLeftTriggerReleased(InputAction.CallbackContext context)
    {
        InvokeLeftTriggerReleased();
    }

    private void InvokeLeftTriggerReleased()
    {
        Debug.Log("Left Trigger Released");
        leftTriggerReleasedCallbacks?.Invoke();
    }

    private void OnLeftGripPressed(InputAction.CallbackContext context)
    {
        InvokeLeftGripPressed();
    }

    private void InvokeLeftGripPressed()
    {
        Debug.Log("Left Grip Pressed");
        leftGripPressedCallbacks?.Invoke();
    }

    private void OnLeftGripReleased(InputAction.CallbackContext context)
    {
        InvokeLeftGripReleased();
    }

    private void InvokeLeftGripReleased()
    {
        Debug.Log("Left Grip Released");
        leftGripReleasedCallbacks?.Invoke();
    }

    private void OnRightTriggerPressed(InputAction.CallbackContext context)
    {
        InvokeRightTriggerPressed();
    }

    private void InvokeRightTriggerPressed()
    {
        Debug.Log("Right Trigger Pressed");
        rightTriggerPressedCallbacks?.Invoke();
    }

    private void OnRightTriggerReleased(InputAction.CallbackContext context)
    {
        InvokeRightTriggerReleased();
    }

    private void InvokeRightTriggerReleased()
    {
        Debug.Log("Right Trigger Released");
        rightTriggerReleasedCallbacks?.Invoke();
    }

    private void OnRightGripPressed(InputAction.CallbackContext context)
    {
        InvokeRightGripPressed();
    }

    private void InvokeRightGripPressed()
    {
        Debug.Log("Right Grip Pressed");
        rightGripPressedCallbacks?.Invoke();
    }

    private void OnRightGripReleased(InputAction.CallbackContext context)
    {
        InvokeRightGripReleased();
    }

    private void InvokeRightGripReleased()
    {
        Debug.Log("Right Grip Released");
        rightGripReleasedCallbacks?.Invoke();
    }
}
