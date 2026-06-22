using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeautifyEffect = Beautify.Universal.Beautify;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class GlobalManager : MonoBehaviour
{
    private const float MainContainerOrderZStep = 100f;

    public static GlobalManager Instance { get; private set; }

    [Header("Scene References")]
    [SerializeField] private Player player;
    [SerializeField] private Target[] targets;
    [SerializeField] private VRButton[] buttons;

    [Header("Main Containers")]
    [SerializeField] private MainContainer containerTuto;
    [SerializeField] private MainContainer container1;
    [SerializeField] private MainContainer container2;
    [SerializeField] private MainContainer container3;
    [SerializeField] private MainContainer containerFinal;

    [Header("UI References")]
    [SerializeField] private UI_Warning uiWarning;
    [SerializeField] private UI_StudentID uiStudentID;
    [SerializeField] private UI_Announcer uiAnnouncer;
    [SerializeField] private UI_TestType uiTestType;
    [SerializeField] private UI_Title uiTitle;
    [SerializeField] private UI_Fade uiFade;
    [SerializeField] private string moveTitleText;
    [SerializeField, TextArea] private string finalWarningText;

    [Header("Debug Visuals")]
    [SerializeField] private bool renderTargetRanges;
    [SerializeField, Min(0f)] private float targetRangeAlphaMultiplier = 1f;

    [Header("Runtime Performance")]
    [SerializeField, Min(1)] private int targetFrameRate = 60;
    [SerializeField] private bool applyStartupResolution = true;
    [SerializeField, Min(1)] private int startupResolutionWidth = 1920;
    [SerializeField, Min(1)] private int startupResolutionHeight = 1080;
    [SerializeField, Range(0.1f, 2f)] private float xrRenderScale = 0.65f;
    [SerializeField, Min(0)] private int xrRenderScaleRetryFrames = 60;
    [SerializeField] private bool configureShinySsrrForVrOnStart = true;

    [Header("Beautify Blur")]
    [SerializeField] private BeautifyEffect beautify;
    [SerializeField, Min(0f)] private float beautifyBlurTweenDuration = 0.25f;
    [SerializeField, Min(0f)] private float normalBlurIntensity = 0.5f;
    [SerializeField, Range(0f, 1f)] private float normalBlurRadius = 0.375f;
    [SerializeField, Range(0f, 1f)] private float normalBlurFalloff = 0.5f;
    [SerializeField, Min(0f)] private float globalBlurIntensity = 2f;
    [SerializeField, Range(0f, 1f)] private float globalBlurRadius;
    [SerializeField, Range(0f, 1f)] private float globalBlurFalloff;

    [Header("Effects")]
    [SerializeField] private ParticleSystem missionClearParticle;
    [SerializeField] private ParticleSystem zoneParticle;
    [SerializeField] private ParticleSystem zoneClearedParticle;
    [SerializeField] private List<ParticleSystem> spawnList = new List<ParticleSystem>();

    private Sequence sequence;
    private Tween beautifyBlurTween;
    private ParticleSystem runtimeMissionClearParticle;
    private ParticleSystem runtimeZoneParticle;
    private ParticleSystem runtimeZoneClearedParticle;
    private readonly List<ParticleSystem> runtimeSpawnList = new List<ParticleSystem>();
    private readonly List<MainContainer> orderedMainContainers = new List<MainContainer>();
    private readonly List<XRDisplaySubsystem> xrDisplaySubsystems = new List<XRDisplaySubsystem>();
    private readonly HashSet<UnityEngine.Object> globalBlurOwners = new HashSet<UnityEngine.Object>();
    private int spawnEffectIndex;
    private static bool missingInstanceWarningShown;
    private static bool searchedShinySsrrFeatureType;
    private static Type shinySsrrFeatureType;
    private static bool searchedShinySsrrEnabledField;
    private static FieldInfo shinySsrrEnabledField;
    private Coroutine startupUiCoroutine;
    private bool isMainSequenceCompleted;
    private bool missingBeautifyWarningShown;

    public UI_Warning WarningUI => uiWarning;
    public UI_StudentID StudentIDUI => uiStudentID;
    public UI_Announcer AnnouncerUI => uiAnnouncer;
    public UI_TestType TestTypeUI => uiTestType;
    public UI_Title TitleUI => uiTitle;
    public UI_Fade FadeUI => uiFade;
    public int StudentIDValue => uiStudentID != null ? uiStudentID.SavedInputValue : 0;
    public int TestTypeValue => uiTestType != null ? uiTestType.SavedInputValue : 0;
    public bool HasStudentIDValue => uiStudentID != null && uiStudentID.HasSavedInputValue;
    public bool HasTestTypeValue => uiTestType != null && uiTestType.HasSavedInputValue;
    public bool IsMainSequenceCompleted => isMainSequenceCompleted;
    public string StudyEventLogFilePath => StudyLogger.EventLogFilePath;
    public string StudyTrialSummaryFilePath => StudyLogger.TrialSummaryFilePath;

    private void Awake()
    {
        Instance = this;
        missingInstanceWarningShown = false;
        CacheSceneReferences();
    }

    private void Start()
    {
        PlayStartupFadeIn();
        StartStartupUiSequence();
        ApplyStartupResolution();
        ApplyTargetFrameRate();
        ApplyShinySsrrStartupPolicy();
        StartCoroutine(ApplyXrRenderScaleWhenReady());
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        StopSequence();
        StopBeautifyBlurTween();
        StopStartupUiSequence();
        DestroyRuntimeParticle(runtimeMissionClearParticle);
        DestroyRuntimeParticle(runtimeZoneParticle);
        DestroyRuntimeParticle(runtimeZoneClearedParticle);
        DestroyRuntimeParticles(runtimeSpawnList);
    }

    public static void PlayMissionClearParticle(Vector3 position)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.PlayMissionClearParticleInternal(position);
    }

    public static bool ShouldRenderTargetRanges()
    {
        GlobalManager manager = ResolveInstance();
        return manager != null && manager.renderTargetRanges;
    }

    public static float TargetRangeAlphaMultiplier()
    {
        GlobalManager manager = ResolveInstance();
        return manager != null ? Mathf.Max(0f, manager.targetRangeAlphaMultiplier) : 1f;
    }

    public static void SetTargetRangeRendering(bool render)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.renderTargetRanges = render;
    }

    public static void SetVrRenderScale(float scale)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.xrRenderScale = Mathf.Clamp(scale, 0.1f, 2f);
        manager.ApplyXrRenderScale();
    }

    public static void SetShinySsrrEnabled(bool enabled)
    {
        FieldInfo enabledField = GetShinySsrrEnabledField();
        if (enabledField == null)
        {
            return;
        }

        enabledField.SetValue(null, enabled);
    }

    public static void SetBeautifyGlobalBlur()
    {
        SetBeautifyBlurState(true);
    }

    public static void SetBeautifyNormalBlur()
    {
        SetBeautifyBlurState(false);
    }

    public static void SetBeautifyBlurState(bool useGlobalBlur)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.TweenBeautifyBlur(useGlobalBlur);
    }

    public static void SetBeautifyBlurRequest(UnityEngine.Object owner, bool active)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.SetBeautifyBlurRequestInternal(owner, active);
    }

    public static void PlayZoneParticle(Vector3 position)
    {
        PlayZoneParticle(position, Vector3.one);
    }

    public static void PlayZoneParticle(Vector3 position, Vector3 localScale)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.PlayZoneParticleInternal(position, localScale);
    }

    public static void StopZoneParticle()
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.StopZoneParticleInternal();
    }

    public static void PlayZoneClearedParticle(Vector3 position)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.PlayZoneClearedParticleInternal(position);
    }

    public static void PlaySpawnEffect(Vector3 position)
    {
        GlobalManager manager = ResolveInstance();
        if (manager == null)
        {
            return;
        }

        manager.PlaySpawnEffectInternal(position);
    }

    private static GlobalManager ResolveInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        Instance = FindFirstObjectByType<GlobalManager>();
        if (Instance == null && !missingInstanceWarningShown)
        {
            missingInstanceWarningShown = true;
            Debug.LogWarning("[GlobalManager] Scene has no GlobalManager instance.");
        }

        return Instance;
    }

    private void CacheSceneReferences()
    {
        if (player == null)
        {
            player = FindSceneComponent<Player>();
        }

        if (targets == null || targets.Length == 0)
        {
            targets = FindSceneComponents<Target>();
        }

        if (buttons == null || buttons.Length == 0)
        {
            buttons = FindSceneComponents<VRButton>();
        }

        CacheBeautifyReference();
    }

    private static T FindSceneComponent<T>() where T : Component
    {
        T[] components = FindSceneComponents<T>();
        return components.Length > 0 ? components[0] : null;
    }

    private static T[] FindSceneComponents<T>() where T : Component
    {
        T[] components = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<T> sceneComponents = new List<T>();
        for (int i = 0; i < components.Length; i++)
        {
            T component = components[i];
            if (IsSceneComponent(component))
            {
                sceneComponents.Add(component);
            }
        }

        return sceneComponents.ToArray();
    }

    private void StartStartupUiSequence()
    {
        StopStartupUiSequence();
        ResetMainContainersForStartup();
        isMainSequenceCompleted = false;
        startupUiCoroutine = StartCoroutine(StartupUiSequence());
    }

    private void PlayStartupFadeIn()
    {
        if (uiFade == null)
        {
            return;
        }

        EnsureUiHierarchyActive(uiFade.transform);
        uiFade.FadeInFromBlack();
    }

    private IEnumerator StartupUiSequence()
    {
        if (uiStudentID != null)
        {
            EnsureUiHierarchyActive(uiStudentID.transform);
            uiStudentID.Activate();
            yield return new WaitUntil(() => uiStudentID == null || !uiStudentID.gameObject.activeSelf);
            CreateStudyLogFilesFromStudentID();
        }

        if (uiTestType != null)
        {
            EnsureUiHierarchyActive(uiTestType.transform);
            uiTestType.Activate();
            yield return new WaitUntil(() => uiTestType == null || !uiTestType.gameObject.activeSelf);
            StudyLogger.SetTestType(uiTestType.CurrentInput);
            StudyLogger.RecordEvent("UI_TestType_Complete", uiTestType.CurrentInput);
            ApplyMainContainerOrderFromTestType();
        }

        yield return RunContainerMission(containerTuto);

        for (int i = 0; i < orderedMainContainers.Count; i++)
        {
            yield return MoveAllContainersWithTitle();
            yield return RunContainerMission(orderedMainContainers[i]);
        }

        isMainSequenceCompleted = true;
        yield return RunFinalRestartSequence();
        startupUiCoroutine = null;
    }

    private IEnumerator RunContainerMission(MainContainer container)
    {
        if (container == null)
        {
            yield break;
        }

        EnsureContainerReady(container);
        container.StartMission();
        yield return new WaitUntil(() =>
            container == null ||
            container.CurrentMissionState == MainContainer.MissionProgressState.Completed);
    }

    private void CreateStudyLogFilesFromStudentID()
    {
        if (uiStudentID == null)
        {
            return;
        }

        string studentId = uiStudentID.CurrentInput;
        if (string.IsNullOrWhiteSpace(studentId))
        {
            Debug.LogWarning("[GlobalManager] Student ID is empty. Study log files were not created.", this);
            return;
        }

        StudyLogger.CreateFiles(studentId);
        StudyLogger.RecordEvent("UI_StudentID_Complete", studentId);
    }

    private IEnumerator MoveAllContainersWithTitle()
    {
        ActivateMoveTitle();
        MoveAllContainers();
        yield return new WaitForSeconds(MainContainer.MoveDuration);
        DeactivateMoveTitle();
    }

    private IEnumerator RunFinalRestartSequence()
    {
        ActivateFinalTitle();

        if (uiWarning != null)
        {
            EnsureUiHierarchyActive(uiWarning.transform);
            uiWarning.Activate(finalWarningText);
            yield return new WaitUntil(() => uiWarning == null || !uiWarning.gameObject.activeSelf);
        }

        DeactivateMoveTitle();

        if (uiFade != null)
        {
            EnsureUiHierarchyActive(uiFade.transform);
            uiFade.FadeOutToBlack();
            yield return new WaitForSeconds(UI_Fade.FadeDuration);
        }

        StudyLogger.RecordEvent("Scene_Restart");
        ResetStaticRuntimeState();
        ReloadCurrentScene();
    }

    private void ActivateMoveTitle()
    {
        if (uiTitle == null)
        {
            return;
        }

        EnsureUiHierarchyActive(uiTitle.transform);
        uiTitle.Activate(moveTitleText);
    }

    private void DeactivateMoveTitle()
    {
        if (uiTitle == null)
        {
            return;
        }

        uiTitle.Deactivate();
    }

    private void ActivateFinalTitle()
    {
        if (uiTitle == null)
        {
            return;
        }

        EnsureUiHierarchyActive(uiTitle.transform);
        uiTitle.Activate();
    }

    private static void EnsureContainerReady(MainContainer container)
    {
        if (container == null)
        {
            return;
        }

        if (!container.gameObject.activeSelf)
        {
            container.gameObject.SetActive(true);
        }

        if (!container.enabled)
        {
            container.enabled = true;
        }
    }

    private void ResetMainContainersForStartup()
    {
        ResetMainContainerForStartup(containerTuto);
        ResetMainContainerForStartup(container1);
        ResetMainContainerForStartup(container2);
        ResetMainContainerForStartup(container3);
        ResetMainContainerForStartup(containerFinal);
        orderedMainContainers.Clear();
    }

    private static void ResetMainContainerForStartup(MainContainer container)
    {
        if (container == null)
        {
            return;
        }

        container.ResetRuntimeState();
    }

    private void MoveAllContainers()
    {
        MoveContainer(containerTuto);
        MoveContainer(container1);
        MoveContainer(container2);
        MoveContainer(container3);
        MoveContainer(containerFinal);
    }

    private static void MoveContainer(MainContainer container)
    {
        if (container == null)
        {
            return;
        }

        container.Move();
    }

    private void ApplyMainContainerOrderFromTestType()
    {
        orderedMainContainers.Clear();

        if (uiTestType == null)
        {
            return;
        }

        string order = uiTestType.CurrentInput;
        for (int i = 0; i < order.Length; i++)
        {
            MainContainer container = GetMainContainerByTestTypeKey(order[i]);
            if (container == null)
            {
                continue;
            }

            float z = MainContainerOrderZStep * (i + 1);
            container.transform.position = new Vector3(0f, 0f, z);
            orderedMainContainers.Add(container);
        }

        if (containerFinal != null)
        {
            containerFinal.transform.position = new Vector3(0f, 0f, MainContainerOrderZStep * 4f);
        }
    }

    private MainContainer GetMainContainerByTestTypeKey(char key)
    {
        switch (key)
        {
            case '1':
                return container1;
            case '2':
                return container2;
            case '3':
                return container3;
            default:
                return null;
        }
    }

    private static void EnsureUiHierarchyActive(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }

            current = current.parent;
        }
    }

    private void StopStartupUiSequence()
    {
        if (startupUiCoroutine == null)
        {
            return;
        }

        StopCoroutine(startupUiCoroutine);
        startupUiCoroutine = null;
    }

    private static bool IsSceneComponent(Component component)
    {
        return component != null && IsSceneGameObject(component.gameObject);
    }

    private static bool IsSceneGameObject(GameObject gameObject)
    {
        return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
    }

    private static void ResetStaticRuntimeState()
    {
        Target.ResetStaticState();
        InputManager.ResetStaticState();
        StudyLogger.ResetStaticState();
        Instance = null;
        missingInstanceWarningShown = false;
        searchedShinySsrrFeatureType = false;
        shinySsrrFeatureType = null;
        searchedShinySsrrEnabledField = false;
        shinySsrrEnabledField = null;
    }

    private static void ReloadCurrentScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(activeScene.path))
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadScene(activeScene.path);
            return;
        }
#endif
        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
            return;
        }

        SceneManager.LoadScene(activeScene.name);
    }

    private void ApplyTargetFrameRate()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(1, targetFrameRate);
    }

    private void ApplyStartupResolution()
    {
        if (!applyStartupResolution)
        {
            return;
        }

        int width = Mathf.Max(1, startupResolutionWidth);
        int height = Mathf.Max(1, startupResolutionHeight);
        Screen.SetResolution(width, height, Screen.fullScreenMode);
    }

    private void ApplyShinySsrrStartupPolicy()
    {
        if (!configureShinySsrrForVrOnStart)
        {
            return;
        }

        SetShinySsrrEnabled(true);
        ConfigureLoadedShinySsrrFeaturesForVr();
    }

    private static FieldInfo GetShinySsrrEnabledField()
    {
        if (searchedShinySsrrEnabledField)
        {
            return shinySsrrEnabledField;
        }

        searchedShinySsrrEnabledField = true;
        Type featureType = GetShinySsrrFeatureType();
        shinySsrrEnabledField = featureType?.GetField("isEnabled", BindingFlags.Public | BindingFlags.Static);
        return shinySsrrEnabledField;
    }

    private static Type GetShinySsrrFeatureType()
    {
        if (searchedShinySsrrFeatureType)
        {
            return shinySsrrFeatureType;
        }

        searchedShinySsrrFeatureType = true;
        Type featureType = Type.GetType("ShinySSRR.ShinySSRR, ShinySSRR");
        if (featureType == null)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                featureType = assemblies[i].GetType("ShinySSRR.ShinySSRR");
                if (featureType != null)
                {
                    break;
                }
            }
        }

        shinySsrrFeatureType = featureType;
        return shinySsrrFeatureType;
    }

    private static void ConfigureLoadedShinySsrrFeaturesForVr()
    {
        Type featureType = GetShinySsrrFeatureType();
        if (featureType == null)
        {
            return;
        }

        UnityEngine.Object[] features = Resources.FindObjectsOfTypeAll(featureType);
        for (int i = 0; i < features.Length; i++)
        {
            UnityEngine.Object feature = features[i];
            SetBoolField(featureType, feature, "useDeferred", false);
            SetBoolField(featureType, feature, "enableScreenSpaceNormalsPass", false);
            SetBoolField(featureType, feature, "screenSpaceNormalsOpaques", false);
            SetBoolField(featureType, feature, "includeTransparentsInScreenSpaceNormals", false);
            SetBoolField(featureType, feature, "enableTransparencyDepthPrepass", false);
            SetBoolField(featureType, feature, "customSmoothnessMetallicPass", false);
        }
    }

    private static void SetBoolField(Type type, object target, string fieldName, bool value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(bool))
        {
            field.SetValue(target, value);
        }
    }

    private IEnumerator ApplyXrRenderScaleWhenReady()
    {
        int retryFrames = Mathf.Max(0, xrRenderScaleRetryFrames);
        for (int i = 0; i <= retryFrames; i++)
        {
            if (ApplyXrRenderScale())
            {
                yield break;
            }

            yield return null;
        }
    }

    private bool ApplyXrRenderScale()
    {
        SubsystemManager.GetSubsystems(xrDisplaySubsystems);

        bool applied = false;
        float scale = Mathf.Clamp(xrRenderScale, 0.1f, 2f);
        for (int i = 0; i < xrDisplaySubsystems.Count; i++)
        {
            XRDisplaySubsystem display = xrDisplaySubsystems[i];
            if (display == null || !display.running)
            {
                continue;
            }

            display.scaleOfAllRenderTargets = scale;
            applied = true;
        }

        return applied;
    }

    private void CacheBeautifyReference()
    {
        if (beautify != null)
        {
            return;
        }

        Volume[] volumes = Resources.FindObjectsOfTypeAll<Volume>();
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (!IsSceneComponent(volume))
            {
                continue;
            }

            VolumeProfile profile = volume.profile != null ? volume.profile : volume.sharedProfile;
            if (profile != null && profile.TryGet(out BeautifyEffect foundBeautify))
            {
                beautify = foundBeautify;
                return;
            }
        }
    }

    private void SetBeautifyBlurRequestInternal(UnityEngine.Object owner, bool active)
    {
        if (owner == null)
        {
            TweenBeautifyBlur(active);
            return;
        }

        bool changed = active ? globalBlurOwners.Add(owner) : globalBlurOwners.Remove(owner);
        if (!changed)
        {
            return;
        }

        TweenBeautifyBlur(globalBlurOwners.Count > 0);
    }

    private void TweenBeautifyBlur(bool useGlobalBlur)
    {
        if (!TryGetBeautify(out BeautifyEffect targetBeautify))
        {
            return;
        }

        StopBeautifyBlurTween();

        float startIntensity = targetBeautify.blurIntensity.value;
        float targetIntensity = useGlobalBlur ? globalBlurIntensity : normalBlurIntensity;
        float targetRadius = useGlobalBlur ? globalBlurRadius : normalBlurRadius;
        float targetFalloff = useGlobalBlur ? globalBlurFalloff : normalBlurFalloff;

        SetBeautifyBlurOverrideStates(targetBeautify);
        ApplyBeautifyBlurShapeValues(targetRadius, targetFalloff);

        if (beautifyBlurTweenDuration <= 0f)
        {
            ApplyBeautifyBlurIntensity(targetIntensity);
            return;
        }

        beautifyBlurTween = Tween.Custom(
            this,
            0f,
            1f,
            beautifyBlurTweenDuration,
            (target, progress) => target.ApplyBeautifyBlurIntensity(Mathf.Lerp(startIntensity, targetIntensity, progress)),
            Ease.InOutSine);
    }

    private bool TryGetBeautify(out BeautifyEffect targetBeautify)
    {
        CacheBeautifyReference();
        targetBeautify = beautify;
        if (targetBeautify != null)
        {
            return true;
        }

        if (!missingBeautifyWarningShown)
        {
            missingBeautifyWarningShown = true;
            Debug.LogWarning("[GlobalManager] Scene has no Beautify volume component.", this);
        }

        return false;
    }

    private static void SetBeautifyBlurOverrideStates(BeautifyEffect targetBeautify)
    {
        targetBeautify.blurIntensity.overrideState = true;
        targetBeautify.blurStyle.overrideState = true;
        targetBeautify.blurRadialBlurRadius.overrideState = true;
        targetBeautify.blurRadialBlurFalloff.overrideState = true;
    }

    private void ApplyBeautifyBlurShapeValues(float radius, float falloff)
    {
        if (beautify == null)
        {
            return;
        }

        beautify.blurStyle.value = BeautifyEffect.CreativeBlurStyle.RadialBlur;
        beautify.blurRadialBlurRadius.value = radius;
        beautify.blurRadialBlurFalloff.value = falloff;
    }

    private void ApplyBeautifyBlurIntensity(float intensity)
    {
        if (beautify == null)
        {
            return;
        }

        beautify.blurIntensity.value = intensity;
    }

    private void StopBeautifyBlurTween()
    {
        if (beautifyBlurTween.isAlive)
        {
            beautifyBlurTween.Stop();
        }
    }

    private void StopSequence()
    {
        if (sequence.isAlive)
        {
            sequence.Stop();
        }
    }

    private void PlayMissionClearParticleInternal(Vector3 position)
    {
        PlayParticleAt(missionClearParticle, ref runtimeMissionClearParticle, position, nameof(missionClearParticle));
    }

    private void PlayZoneParticleInternal(Vector3 position, Vector3 localScale)
    {
        PlayParticleAt(zoneParticle, ref runtimeZoneParticle, position, localScale, nameof(zoneParticle));
    }

    private void StopZoneParticleInternal()
    {
        StopParticle(zoneParticle, runtimeZoneParticle);
    }

    private void PlayZoneClearedParticleInternal(Vector3 position)
    {
        PlayParticleAt(zoneClearedParticle, ref runtimeZoneClearedParticle, position, nameof(zoneClearedParticle));
    }

    private void PlaySpawnEffectInternal(Vector3 position)
    {
        if (spawnList == null || spawnList.Count == 0)
        {
            return;
        }

        int spawnCount = spawnList.Count;
        for (int attempt = 0; attempt < spawnCount; attempt++)
        {
            int index = spawnEffectIndex % spawnCount;
            spawnEffectIndex = (spawnEffectIndex + 1) % spawnCount;

            ParticleSystem particle = GetSpawnParticle(index);
            if (particle == null)
            {
                continue;
            }

            PlayParticleAt(particle, position);
            return;
        }
    }

    private void PlayParticleAt(ParticleSystem sourceParticle, ref ParticleSystem runtimeParticle, Vector3 position, string fieldName)
    {
        PlayParticleAt(sourceParticle, ref runtimeParticle, position, null, fieldName);
    }

    private void PlayParticleAt(ParticleSystem sourceParticle, ref ParticleSystem runtimeParticle, Vector3 position, Vector3? localScale, string fieldName)
    {
        ParticleSystem particle = GetPlayableParticle(sourceParticle, ref runtimeParticle, fieldName);
        if (particle == null)
        {
            return;
        }

        if (!particle.gameObject.activeSelf)
        {
            particle.gameObject.SetActive(true);
        }

        particle.transform.position = position;
        if (localScale.HasValue)
        {
            particle.transform.localScale = localScale.Value;
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.Play(true);
    }

    private void PlayParticleAt(ParticleSystem particle, Vector3 position)
    {
        if (particle == null)
        {
            return;
        }

        if (!particle.gameObject.activeSelf)
        {
            particle.gameObject.SetActive(true);
        }

        particle.transform.position = position;
        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.Play(true);
    }

    private void StopParticle(ParticleSystem sourceParticle, ParticleSystem runtimeParticle)
    {
        ParticleSystem particle = runtimeParticle != null ? runtimeParticle : sourceParticle;
        if (particle == null)
        {
            return;
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private ParticleSystem GetPlayableParticle(ParticleSystem sourceParticle, ref ParticleSystem runtimeParticle, string fieldName)
    {
        if (sourceParticle == null)
        {
            Debug.LogWarning($"[GlobalManager] {fieldName} is not assigned.", this);
            return null;
        }

        if (sourceParticle.gameObject.scene.IsValid())
        {
            return sourceParticle;
        }

        if (runtimeParticle == null)
        {
            runtimeParticle = Instantiate(sourceParticle);
            runtimeParticle.name = $"{sourceParticle.name} Runtime";
        }

        return runtimeParticle;
    }

    private ParticleSystem GetSpawnParticle(int index)
    {
        if (index < 0 || spawnList == null || index >= spawnList.Count)
        {
            return null;
        }

        ParticleSystem sourceParticle = spawnList[index];
        if (sourceParticle == null)
        {
            return null;
        }

        if (sourceParticle.gameObject.scene.IsValid())
        {
            return sourceParticle;
        }

        EnsureRuntimeSpawnListSize(index + 1);
        if (runtimeSpawnList[index] == null)
        {
            runtimeSpawnList[index] = Instantiate(sourceParticle);
            runtimeSpawnList[index].name = $"{sourceParticle.name} Spawn Runtime {index}";
        }

        return runtimeSpawnList[index];
    }

    private void EnsureRuntimeSpawnListSize(int size)
    {
        while (runtimeSpawnList.Count < size)
        {
            runtimeSpawnList.Add(null);
        }
    }

    private static void DestroyRuntimeParticle(ParticleSystem particle)
    {
        if (particle != null)
        {
            Destroy(particle.gameObject);
        }
    }

    private static void DestroyRuntimeParticles(List<ParticleSystem> particles)
    {
        if (particles == null)
        {
            return;
        }

        for (int i = 0; i < particles.Count; i++)
        {
            DestroyRuntimeParticle(particles[i]);
        }

        particles.Clear();
    }
}
