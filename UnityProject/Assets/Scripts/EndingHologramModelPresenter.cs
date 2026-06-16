using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowPrototype
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EndingHologramModelPresenter : MonoBehaviour
    {
        private static readonly Vector2Int DefaultRenderTextureSize = new Vector2Int(1080, 1080);
        private const int LandmarksPerHand = 21;
        private const int MaxHands = 2;
        private const int IndexTipLandmark = 8;
        private const float SideCameraAngleDegrees = 60.0f;

        [SerializeField] private string modelResourcePath = "Models/star character_2_1";
        [SerializeField] private string pokeModelResourcePath = "Models/star character_2";
        [SerializeField] private bool showOnStart;
        [SerializeField] private bool previewInEditMode = true;
        [SerializeField] private int targetDisplayIndex = DisplayRoutingSettings.HologramUnityDisplayIndex;
        [SerializeField] private int sortingOrder = 5100;
        [SerializeField] private bool autoApplyPanelLayout;
        [SerializeField] private bool autoApplyCameraLayout;
        [SerializeField] private bool autoApplyModelTransform;
        [SerializeField] private bool autoApplyLightSettings;
        [SerializeField] private bool createCameraLights = true;
        [SerializeField] private bool autoApplyCameraLightSettings;
        [SerializeField] private Vector3 modelPosition = Vector3.zero;
        [SerializeField] private Vector3 modelEulerAngles = Vector3.zero;
        [SerializeField] private Vector3 modelScale = new Vector3(0.8f, 0.8f, 0.8f);
        [SerializeField, Min(0.1f)] private float cameraDistance = 3.0f;
        [SerializeField] private float cameraHeight = 0.5f;
        [SerializeField] private float cameraPitchDegrees = -9.462f;
        [SerializeField, Min(1.0f)] private float cameraFieldOfView = 40.0f;
        [SerializeField] private Color clearColor = Color.black;
        [SerializeField] private Vector2Int renderTextureSize = DefaultRenderTextureSize;
        [SerializeField] private Vector3 lightEulerAngles = new Vector3(45.0f, -30.0f, 0.0f);
        [SerializeField, Min(0.0f)] private float lightIntensity = 2.0f;
        [SerializeField] private LightType cameraLightType = LightType.Spot;
        [SerializeField] private Color cameraLightColor = Color.white;
        [SerializeField, Min(0.0f)] private float cameraLightIntensity = 4.0f;
        [SerializeField, Min(0.0f)] private float cameraLightRange = 10.0f;
        [SerializeField, Range(1.0f, 179.0f)] private float cameraLightSpotAngle = 45.0f;
        [SerializeField] private bool enablePokeInteraction = true;
        [SerializeField] private MediaPipeUdpReceiver mediaPipeReceiver;
        [SerializeField] private MediaPipeTrackingProcessLauncher mediaPipeLauncher;
        [SerializeField] private bool startMediaPipeReceiverOnShow = true;
        [SerializeField] private bool startMediaPipeLauncherOnShow = true;
        [SerializeField, Range(0, LandmarksPerHand - 1)] private int pokeFingertipLandmarkIndex = IndexTipLandmark;
        [SerializeField] private bool pokeTowardCameraUsesNegativeZ = true;
        [SerializeField, Min(0.0001f)] private float pokeDepthThreshold = 0.14f;
        [SerializeField, Min(0.0001f)] private float pokeMinimumSpeed = 0.65f;
        [SerializeField, Min(0.01f)] private float pokeWindowSeconds = 0.32f;
        [SerializeField, Min(0.0f)] private float pokeCooldownSeconds = 0.75f;
        [SerializeField, Min(0.0f)] private float pokeInitialIgnoreSeconds = 0.35f;
        [SerializeField, Min(0.0f)] private float pokeRearmDepthThreshold = 0.09f;
        [SerializeField, Min(0.0f)] private float pokeMinimumFrameDepthDelta = 0.006f;
        [SerializeField, Min(1)] private int pokeRequiredForwardFrames = 3;
        [SerializeField, Min(0.0f)] private float pokeModelHoldSeconds = 0.5f;
        [SerializeField] private AudioSource pokeAudioSource;
        [SerializeField] private AudioClip[] pokeAudioClips = new AudioClip[0];
        [SerializeField, Range(0.0f, 1.0f)] private float pokeAudioVolume = 1.0f;

        private GameObject rigRoot;
        private Transform modelRoot;
        private GameObject idleModelInstance;
        private GameObject pokeModelInstance;
        private GameObject activeModelInstance;
        private Canvas canvas;
        private RawImage frontPanel;
        private RawImage leftPanel;
        private RawImage rightPanel;
        private Camera frontCamera;
        private Camera leftCamera;
        private Camera rightCamera;
        private Light keyLight;
        private Light frontCameraLight;
        private Light leftCameraLight;
        private Light rightCameraLight;
        private RenderTexture frontTexture;
        private RenderTexture leftTexture;
        private RenderTexture rightTexture;
        private Vector2Int lastLayoutSize;
        private Coroutine pokeRoutine;
        private Coroutine pokeAudioRoutine;
        private bool isVisible;
        private bool hasPokeStart;
        private float pokeStartDepth;
        private float pokeStartTime;
        private float nextPokeAllowedTime;
        private bool hasLastPokeDepth;
        private float lastPokeDepth;
        private float firstPokeSeenTime = -1.0f;
        private bool isPokeArmed = true;
        private float lastTriggeredPokeDepth;
        private int forwardPokeFrameCount;
        private int lastPokeClipIndex = -1;

        private void Awake()
        {
            EnsureOverlay();
            EnsureRig();
            ResolvePokeReferences(false);

            if (Application.isPlaying)
            {
                SetVisible(false);
            }
            else
            {
                RefreshEditorPreview();
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                RefreshEditorPreview();
            }
            else
            {
                ResolvePokeReferences(true);
            }
        }

        private void Start()
        {
            ResolvePokeReferences(true);
            if (showOnStart)
            {
                Show();
            }
        }

        private void OnValidate()
        {
            pokeFingertipLandmarkIndex = Mathf.Clamp(pokeFingertipLandmarkIndex, 0, LandmarksPerHand - 1);
            pokeRequiredForwardFrames = Mathf.Max(1, pokeRequiredForwardFrames);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                RefreshEditorPreview();
                return;
            }

            ApplyTargetDisplay();
            ApplyLayoutIfNeeded(false);
            AssignCameraTargets();

            if (autoApplyCameraLayout)
            {
                ApplyCameraSettings();
            }

            if (autoApplyModelTransform)
            {
                ApplyModelTransform();
            }

            if (autoApplyLightSettings)
            {
                ApplyLightSettings();
            }

            if (autoApplyCameraLightSettings)
            {
                ApplyCameraLightSettings();
            }

            UpdatePokeInteraction();
        }

        public void Show()
        {
            ShowPanels(true, true, true);
        }

        public void ShowSidePanelsOnly()
        {
            ShowPanels(false, true, true);
        }

        private void ShowPanels(bool showFrontPanel, bool showLeftPanel, bool showRightPanel)
        {
            EnsureOverlay();
            EnsureRig();
            EnsureRenderTextures();
            AssignPanelTextures();

            if (!EnsureModel())
            {
                SetVisible(false);
                return;
            }

            ClearRenderTextures();
            SetPanelVisibility(showFrontPanel, showLeftPanel, showRightPanel);
            SetVisible(true);
            StartMediaPipeReceiverIfNeeded();
            Debug.Log("EndingHologramModelPresenter: showing ending hologram model.");
        }

        public void Hide()
        {
            StopPokeRoutine();
            StopPokeAudioRoutine();
            SetActiveModel(false);
            ResetPokeTracking();
            SetVisible(false);
        }

        private void RefreshEditorPreview()
        {
            EnsureOverlay();
            EnsureRig();

            if (!previewInEditMode)
            {
                SetVisible(false);
                return;
            }

            EnsureRenderTextures();
            AssignPanelTextures();
            EnsureModel();
            ApplyTargetDisplay();
            AssignCameraTargets();
            if (autoApplyPanelLayout)
            {
                ApplyLayoutIfNeeded(false);
            }

            if (autoApplyCameraLayout)
            {
                ApplyCameraSettings();
            }

            if (autoApplyModelTransform)
            {
                ApplyModelTransform();
            }

            if (autoApplyLightSettings)
            {
                ApplyLightSettings();
            }

            if (autoApplyCameraLightSettings)
            {
                ApplyCameraLightSettings();
            }

            SetVisible(true);
        }

        private bool EnsureModel()
        {
            if (!EnsureModelInstances())
            {
                return false;
            }

            SetActiveModel(false);
            return true;
        }

        private bool EnsureModelInstances()
        {
            if (modelRoot == null)
            {
                return false;
            }

            if (idleModelInstance == null)
            {
                Transform existingIdle = modelRoot.Find("EndingStarCharacter_Idle");
                if (existingIdle != null)
                {
                    idleModelInstance = existingIdle.gameObject;
                }
            }

            if (pokeModelInstance == null)
            {
                Transform existingPoke = modelRoot.Find("EndingStarCharacter_Poke");
                if (existingPoke == null)
                {
                    existingPoke = modelRoot.Find("EndingStarCharacter");
                }

                if (existingPoke != null)
                {
                    pokeModelInstance = existingPoke.gameObject;
                    pokeModelInstance.name = "EndingStarCharacter_Poke";
                }
            }

            if (idleModelInstance == null)
            {
                idleModelInstance = InstantiateModel(modelResourcePath, "EndingStarCharacter_Idle");
            }

            if (pokeModelInstance == null)
            {
                pokeModelInstance = InstantiateModel(pokeModelResourcePath, "EndingStarCharacter_Poke");
            }

            if (activeModelInstance == null)
            {
                activeModelInstance = idleModelInstance != null ? idleModelInstance : pokeModelInstance;
            }

            SetGameObjectActive(idleModelInstance, activeModelInstance == idleModelInstance);
            SetGameObjectActive(pokeModelInstance, activeModelInstance == pokeModelInstance);
            return idleModelInstance != null || pokeModelInstance != null;
        }

        private GameObject InstantiateModel(string resourcePath, string instanceName)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                Debug.LogWarning($"EndingHologramModelPresenter: model resource path is empty for {instanceName}.");
                return null;
            }

            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"EndingHologramModelPresenter: model prefab was not found in Resources: {resourcePath}");
                return null;
            }

            GameObject instance = Instantiate(prefab, modelRoot);
            instance.name = instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private void SetActiveModel(bool usePokeModel)
        {
            GameObject nextModel = usePokeModel && pokeModelInstance != null
                ? pokeModelInstance
                : idleModelInstance;

            if (nextModel == null)
            {
                nextModel = pokeModelInstance;
            }

            activeModelInstance = nextModel;
            SetGameObjectActive(idleModelInstance, activeModelInstance == idleModelInstance);
            SetGameObjectActive(pokeModelInstance, activeModelInstance == pokeModelInstance);
        }

        private static void SetGameObjectActive(GameObject target, bool isActive)
        {
            if (target != null && target.activeSelf != isActive)
            {
                target.SetActive(isActive);
            }
        }

        private void EnsureRig()
        {
            bool createdCamera = false;
            bool createdLight = false;
            bool createdCameraLight = false;

            if (rigRoot == null)
            {
                Transform existingRig = transform.Find("EndingHologramModelRig");
                rigRoot = existingRig != null ? existingRig.gameObject : new GameObject("EndingHologramModelRig");
                rigRoot.transform.SetParent(transform, false);
            }

            if (modelRoot == null)
            {
                Transform existingModelRoot = rigRoot.transform.Find("ModelRoot");
                if (existingModelRoot != null)
                {
                    modelRoot = existingModelRoot;
                }
                else
                {
                    GameObject modelRootObject = new GameObject("ModelRoot");
                    modelRootObject.transform.SetParent(rigRoot.transform, false);
                    modelRoot = modelRootObject.transform;
                }
            }

            EnsureRenderTextures();

            if (frontCamera == null)
            {
                frontCamera = FindRigComponent<Camera>("EndingModel_Cam_Front");
                if (frontCamera == null)
                {
                    frontCamera = CreateViewCamera("EndingModel_Cam_Front", frontTexture);
                    createdCamera = true;
                }
            }

            if (leftCamera == null)
            {
                leftCamera = FindRigComponent<Camera>("EndingModel_Cam_Left");
                if (leftCamera == null)
                {
                    leftCamera = CreateViewCamera("EndingModel_Cam_Left", leftTexture);
                    createdCamera = true;
                }
            }

            if (rightCamera == null)
            {
                rightCamera = FindRigComponent<Camera>("EndingModel_Cam_Right");
                if (rightCamera == null)
                {
                    rightCamera = CreateViewCamera("EndingModel_Cam_Right", rightTexture);
                    createdCamera = true;
                }
            }

            if (createCameraLights)
            {
                frontCameraLight = EnsureCameraLight(frontCamera, frontCameraLight, "EndingModel_FrontLight", ref createdCameraLight);
                leftCameraLight = EnsureCameraLight(leftCamera, leftCameraLight, "EndingModel_LeftLight", ref createdCameraLight);
                rightCameraLight = EnsureCameraLight(rightCamera, rightCameraLight, "EndingModel_RightLight", ref createdCameraLight);
            }

            if (keyLight == null)
            {
                keyLight = FindRigComponent<Light>("EndingModel_KeyLight");
            }

            if (keyLight == null)
            {
                GameObject lightObject = new GameObject("EndingModel_KeyLight");
                lightObject.transform.SetParent(rigRoot.transform, false);
                keyLight = lightObject.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                createdLight = true;
            }

            AssignCameraTargets();
            if (createdCamera || autoApplyCameraLayout)
            {
                ApplyCameraSettings();
            }

            if (createdLight || autoApplyLightSettings)
            {
                ApplyLightSettings();
            }

            if (createdCameraLight || autoApplyCameraLightSettings)
            {
                ApplyCameraLightSettings();
            }
        }

        private Camera CreateViewCamera(string cameraName, RenderTexture targetTexture)
        {
            GameObject cameraObject = new GameObject(cameraName);
            cameraObject.transform.SetParent(rigRoot.transform, false);

            Camera viewCamera = cameraObject.AddComponent<Camera>();
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = clearColor;
            viewCamera.nearClipPlane = 0.01f;
            viewCamera.farClipPlane = 100.0f;
            viewCamera.allowHDR = true;
            viewCamera.allowMSAA = true;
            viewCamera.targetTexture = targetTexture;
            return viewCamera;
        }

        private Light EnsureCameraLight(Camera viewCamera, Light currentLight, string lightName, ref bool createdAny)
        {
            if (viewCamera == null)
            {
                return currentLight;
            }

            if (currentLight == null)
            {
                Transform existingLight = viewCamera.transform.Find(lightName);
                if (existingLight != null)
                {
                    currentLight = existingLight.GetComponent<Light>();
                }
            }

            if (currentLight != null)
            {
                return currentLight;
            }

            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(viewCamera.transform, false);
            lightObject.transform.localPosition = Vector3.zero;
            lightObject.transform.localRotation = Quaternion.identity;

            currentLight = lightObject.AddComponent<Light>();
            createdAny = true;
            return currentLight;
        }

        private T FindRigComponent<T>(string objectName) where T : Component
        {
            if (rigRoot == null)
            {
                return null;
            }

            Transform child = rigRoot.transform.Find(objectName);
            return child != null ? child.GetComponent<T>() : null;
        }

        private void AssignCameraTargets()
        {
            AssignCameraTarget(frontCamera, frontTexture);
            AssignCameraTarget(leftCamera, leftTexture);
            AssignCameraTarget(rightCamera, rightTexture);
        }

        private void AssignCameraTarget(Camera viewCamera, RenderTexture targetTexture)
        {
            if (viewCamera == null)
            {
                return;
            }

            viewCamera.targetTexture = targetTexture;
        }

        private void ApplyCameraSettings()
        {
            float distance = Mathf.Max(0.1f, cameraDistance);
            float sideRadians = SideCameraAngleDegrees * Mathf.Deg2Rad;
            float sideX = Mathf.Sin(sideRadians) * distance;
            float backZ = -Mathf.Cos(sideRadians) * distance;

            ApplyCamera(frontCamera, new Vector3(0.0f, cameraHeight, distance), new Vector3(cameraPitchDegrees, 180.0f, 0.0f), frontTexture);
            ApplyCamera(leftCamera, new Vector3(sideX, cameraHeight, backZ), new Vector3(cameraPitchDegrees, -SideCameraAngleDegrees, 0.0f), leftTexture);
            ApplyCamera(rightCamera, new Vector3(-sideX, cameraHeight, backZ), new Vector3(cameraPitchDegrees, SideCameraAngleDegrees, 0.0f), rightTexture);
        }

        private void ApplyCamera(Camera viewCamera, Vector3 localPosition, Vector3 localEulerAngles, RenderTexture targetTexture)
        {
            if (viewCamera == null)
            {
                return;
            }

            viewCamera.transform.localPosition = localPosition;
            viewCamera.transform.localRotation = Quaternion.Euler(localEulerAngles);
            viewCamera.fieldOfView = cameraFieldOfView;
            viewCamera.backgroundColor = clearColor;
            viewCamera.targetTexture = targetTexture;
        }

        private void ApplyModelTransform()
        {
            if (modelRoot == null)
            {
                return;
            }

            modelRoot.localPosition = modelPosition;
            modelRoot.localRotation = Quaternion.Euler(modelEulerAngles);
            modelRoot.localScale = modelScale;
        }

        private void ApplyLightSettings()
        {
            if (keyLight == null)
            {
                return;
            }

            keyLight.transform.localRotation = Quaternion.Euler(lightEulerAngles);
            keyLight.intensity = lightIntensity;
        }

        private void ApplyCameraLightSettings()
        {
            ApplyCameraLightSettings(frontCameraLight);
            ApplyCameraLightSettings(leftCameraLight);
            ApplyCameraLightSettings(rightCameraLight);
        }

        private void ApplyCameraLightSettings(Light cameraLight)
        {
            if (cameraLight == null)
            {
                return;
            }

            cameraLight.type = cameraLightType;
            cameraLight.color = cameraLightColor;
            cameraLight.intensity = cameraLightIntensity;
            cameraLight.range = cameraLightRange;
            cameraLight.spotAngle = cameraLightSpotAngle;
            cameraLight.transform.localPosition = Vector3.zero;
            cameraLight.transform.localRotation = Quaternion.identity;
        }

        private void UpdatePokeInteraction()
        {
            if (!enablePokeInteraction || !isVisible)
            {
                ResetPokeTracking();
                return;
            }

            ResolvePokeReferences(true);
            StartMediaPipeReceiverIfNeeded();

            if (mediaPipeReceiver == null ||
                !TryGetPokeDepth(out float currentDepth))
            {
                ResetPokeTracking();
                return;
            }

            float now = Time.unscaledTime;
            if (firstPokeSeenTime < 0.0f)
            {
                firstPokeSeenTime = now;
            }

            if (!hasLastPokeDepth)
            {
                hasLastPokeDepth = true;
                lastPokeDepth = currentDepth;
                SetPokeStart(currentDepth, now);
                return;
            }

            float frameDepthDelta = GetTowardCameraDepthDelta(lastPokeDepth, currentDepth);
            if (frameDepthDelta >= pokeMinimumFrameDepthDelta)
            {
                forwardPokeFrameCount++;
            }
            else if (frameDepthDelta <= -pokeMinimumFrameDepthDelta * 0.5f)
            {
                forwardPokeFrameCount = 0;
            }

            lastPokeDepth = currentDepth;

            if (now - firstPokeSeenTime < pokeInitialIgnoreSeconds)
            {
                SetPokeStart(currentDepth, now);
                return;
            }

            if (!isPokeArmed)
            {
                float retractDelta = GetAwayFromCameraDepthDelta(lastTriggeredPokeDepth, currentDepth);
                if (retractDelta >= pokeRearmDepthThreshold)
                {
                    isPokeArmed = true;
                    forwardPokeFrameCount = 0;
                    SetPokeStart(currentDepth, now);
                }

                return;
            }

            if (!hasPokeStart)
            {
                SetPokeStart(currentDepth, now);
                return;
            }

            float elapsed = Mathf.Max(0.001f, now - pokeStartTime);
            float depthDelta = GetTowardCameraDepthDelta(pokeStartDepth, currentDepth);
            float speed = depthDelta / elapsed;

            if (now >= nextPokeAllowedTime &&
                elapsed <= pokeWindowSeconds &&
                forwardPokeFrameCount >= pokeRequiredForwardFrames &&
                depthDelta >= pokeDepthThreshold &&
                speed >= pokeMinimumSpeed)
            {
                TriggerPoke(now, currentDepth);
                SetPokeStart(currentDepth, now);
                return;
            }

            if (elapsed > pokeWindowSeconds || depthDelta < -pokeDepthThreshold * 0.5f)
            {
                SetPokeStart(currentDepth, now);
            }
        }

        private void ResolvePokeReferences(bool allowRuntimeComponentCreation)
        {
            if (mediaPipeReceiver == null)
            {
                mediaPipeReceiver = GetComponent<MediaPipeUdpReceiver>();
                if (mediaPipeReceiver == null)
                {
                    mediaPipeReceiver = GetComponentInParent<MediaPipeUdpReceiver>();
                }

                if (mediaPipeReceiver == null && Application.isPlaying)
                {
                    mediaPipeReceiver = FindObjectOfType<MediaPipeUdpReceiver>();
                }

                if (mediaPipeReceiver == null && allowRuntimeComponentCreation && Application.isPlaying)
                {
                    mediaPipeReceiver = gameObject.AddComponent<MediaPipeUdpReceiver>();
                }
            }

            if (mediaPipeLauncher == null)
            {
                mediaPipeLauncher = GetComponent<MediaPipeTrackingProcessLauncher>();
                if (mediaPipeLauncher == null)
                {
                    mediaPipeLauncher = GetComponentInParent<MediaPipeTrackingProcessLauncher>();
                }

                if (mediaPipeLauncher == null && Application.isPlaying)
                {
                    mediaPipeLauncher = FindObjectOfType<MediaPipeTrackingProcessLauncher>();
                }

                if (mediaPipeLauncher == null && allowRuntimeComponentCreation && Application.isPlaying)
                {
                    mediaPipeLauncher = gameObject.AddComponent<MediaPipeTrackingProcessLauncher>();
                }
            }

            if (pokeAudioSource == null)
            {
                pokeAudioSource = GetComponent<AudioSource>();
                if (pokeAudioSource == null && allowRuntimeComponentCreation && Application.isPlaying)
                {
                    pokeAudioSource = gameObject.AddComponent<AudioSource>();
                    pokeAudioSource.playOnAwake = false;
                    pokeAudioSource.spatialBlend = 0.0f;
                    pokeAudioSource.ignoreListenerPause = true;
                }
            }
        }

        private void StartMediaPipeReceiverIfNeeded()
        {
            if (!Application.isPlaying ||
                !enablePokeInteraction ||
                !startMediaPipeReceiverOnShow ||
                mediaPipeReceiver == null)
            {
                return;
            }

            if (startMediaPipeLauncherOnShow && mediaPipeLauncher != null)
            {
                if (!mediaPipeLauncher.enabled)
                {
                    mediaPipeLauncher.enabled = true;
                }

                mediaPipeLauncher.Launch();
            }

            if (!mediaPipeReceiver.enabled)
            {
                mediaPipeReceiver.enabled = true;
            }

            mediaPipeReceiver.StartReceiver();
        }

        private bool TryGetPokeDepth(out float depth)
        {
            depth = 0.0f;
            if (mediaPipeReceiver == null ||
                !mediaPipeReceiver.TryGetLatestLandmarks(out Vector3[] landmarks))
            {
                return false;
            }

            int handCount = Mathf.Min(MaxHands, landmarks.Length / LandmarksPerHand);
            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                int landmarkIndex = (handIndex * LandmarksPerHand) + pokeFingertipLandmarkIndex;
                if (landmarkIndex < 0 || landmarkIndex >= landmarks.Length)
                {
                    continue;
                }

                depth = landmarks[landmarkIndex].z;
                return true;
            }

            return false;
        }

        private float GetTowardCameraDepthDelta(float startDepth, float currentDepth)
        {
            return pokeTowardCameraUsesNegativeZ
                ? startDepth - currentDepth
                : currentDepth - startDepth;
        }

        private float GetAwayFromCameraDepthDelta(float triggeredDepth, float currentDepth)
        {
            return pokeTowardCameraUsesNegativeZ
                ? currentDepth - triggeredDepth
                : triggeredDepth - currentDepth;
        }

        private void SetPokeStart(float depth, float time)
        {
            hasPokeStart = true;
            pokeStartDepth = depth;
            pokeStartTime = time;
        }

        private void ResetPokeTracking()
        {
            hasPokeStart = false;
            hasLastPokeDepth = false;
            firstPokeSeenTime = -1.0f;
            isPokeArmed = true;
            forwardPokeFrameCount = 0;
        }

        private void TriggerPoke(float now, float currentDepth)
        {
            nextPokeAllowedTime = now + pokeCooldownSeconds;
            isPokeArmed = false;
            lastTriggeredPokeDepth = currentDepth;
            forwardPokeFrameCount = 0;
            PlayPokeSound();

            if (pokeRoutine != null)
            {
                StopCoroutine(pokeRoutine);
            }

            pokeRoutine = StartCoroutine(PokeModelRoutine());
        }

        private IEnumerator PokeModelRoutine()
        {
            EnsureModelInstances();
            SetActiveModel(true);

            if (pokeModelHoldSeconds > 0.0f)
            {
                yield return new WaitForSeconds(pokeModelHoldSeconds);
            }

            SetActiveModel(false);
            pokeRoutine = null;
        }

        private void StopPokeRoutine()
        {
            if (pokeRoutine == null)
            {
                return;
            }

            StopCoroutine(pokeRoutine);
            pokeRoutine = null;
        }

        private void PlayPokeSound()
        {
            AudioClip clip = PickPokeAudioClip();
            if (clip == null)
            {
                return;
            }

            ResolvePokeReferences(true);
            pokeAudioSource = HologramAudioPlaybackUtility.Resolve2DAudioSource(this, pokeAudioSource);
            if (pokeAudioSource == null)
            {
                return;
            }

            if (pokeAudioRoutine != null)
            {
                StopCoroutine(pokeAudioRoutine);
            }

            pokeAudioRoutine = StartCoroutine(PlayPokeSoundRoutine(clip));
        }

        private IEnumerator PlayPokeSoundRoutine(AudioClip clip)
        {
            if (clip == null || pokeAudioSource == null)
            {
                pokeAudioRoutine = null;
                yield break;
            }

            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }

            float loadDeadline = Time.realtimeSinceStartup + 2.0f;
            while (clip.loadState == AudioDataLoadState.Loading &&
                Time.realtimeSinceStartup < loadDeadline)
            {
                yield return null;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogWarning($"EndingHologramModelPresenter: poke audio clip was not loaded: {clip.name}");
                pokeAudioRoutine = null;
                yield break;
            }

            HologramAudioPlaybackUtility.EnsureActiveAudioListener(gameObject);
            pokeAudioSource.enabled = true;
            pokeAudioSource.playOnAwake = false;
            pokeAudioSource.loop = false;
            pokeAudioSource.mute = false;
            pokeAudioSource.volume = pokeAudioVolume;
            pokeAudioSource.spatialBlend = 0.0f;
            pokeAudioSource.ignoreListenerPause = true;
            pokeAudioSource.Stop();
            pokeAudioSource.clip = clip;
            pokeAudioSource.Play();

            pokeAudioRoutine = null;
        }

        private void StopPokeAudioRoutine()
        {
            if (pokeAudioRoutine != null)
            {
                StopCoroutine(pokeAudioRoutine);
                pokeAudioRoutine = null;
            }

            if (pokeAudioSource != null)
            {
                pokeAudioSource.Stop();
            }
        }

        private AudioClip PickPokeAudioClip()
        {
            if (pokeAudioClips == null || pokeAudioClips.Length == 0)
            {
                return null;
            }

            int availableCount = 0;
            for (int i = 0; i < pokeAudioClips.Length; i++)
            {
                if (pokeAudioClips[i] != null)
                {
                    availableCount++;
                }
            }

            if (availableCount == 0)
            {
                return null;
            }

            int skipIndex = availableCount > 1 ? lastPokeClipIndex : -1;
            if (skipIndex < 0 ||
                skipIndex >= pokeAudioClips.Length ||
                pokeAudioClips[skipIndex] == null)
            {
                skipIndex = -1;
            }

            int randomOrdinal = Random.Range(0, availableCount - (skipIndex >= 0 ? 1 : 0));
            int ordinal = 0;
            for (int i = 0; i < pokeAudioClips.Length; i++)
            {
                if (pokeAudioClips[i] == null || i == skipIndex)
                {
                    continue;
                }

                if (ordinal == randomOrdinal)
                {
                    lastPokeClipIndex = i;
                    return pokeAudioClips[i];
                }

                ordinal++;
            }

            lastPokeClipIndex = -1;
            for (int i = 0; i < pokeAudioClips.Length; i++)
            {
                if (pokeAudioClips[i] != null)
                {
                    return pokeAudioClips[i];
                }
            }

            return null;
        }

        private void EnsureOverlay()
        {
            if (canvas != null && frontPanel != null && leftPanel != null && rightPanel != null)
            {
                ApplyTargetDisplay();
                ApplyLayoutIfNeeded(false);
                return;
            }

            Transform existingCanvasTransform = transform.Find("EndingHologramModelCanvas");
            bool createdOverlay = existingCanvasTransform == null;
            GameObject canvasObject = existingCanvasTransform != null
                ? existingCanvasTransform.gameObject
                : new GameObject("EndingHologramModelCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1.0f;

            GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                raycaster = canvasObject.AddComponent<GraphicRaycaster>();
            }

            raycaster.enabled = false;

            frontPanel = FindOrCreatePanel(canvasObject.transform, "EndingModel_Front", 180.0f);
            leftPanel = FindOrCreatePanel(canvasObject.transform, "EndingModel_Left", -90.0f);
            rightPanel = FindOrCreatePanel(canvasObject.transform, "EndingModel_Right", 90.0f);

            ApplyTargetDisplay();
            if (createdOverlay || autoApplyPanelLayout)
            {
                ApplyLayoutIfNeeded(true);
            }
        }

        private static RawImage FindOrCreatePanel(Transform parent, string panelName, float zRotation)
        {
            Transform existingPanel = parent.Find(panelName);
            if (existingPanel != null && existingPanel.TryGetComponent(out RawImage existingImage))
            {
                RectTransform existingRect = existingImage.rectTransform;
                existingRect.localRotation = Quaternion.Euler(0.0f, 0.0f, zRotation);
                existingRect.pivot = new Vector2(0.5f, 0.5f);
                existingImage.raycastTarget = false;
                existingImage.color = Color.white;
                return existingImage;
            }

            return CreatePanel(parent, panelName, zRotation);
        }

        private static RawImage CreatePanel(Transform parent, string panelName, float zRotation)
        {
            GameObject panelObject = new GameObject(panelName, typeof(RectTransform));
            panelObject.transform.SetParent(parent, false);

            RawImage panel = panelObject.AddComponent<RawImage>();
            panel.raycastTarget = false;
            panel.color = Color.white;

            RectTransform rectTransform = panel.rectTransform;
            rectTransform.localRotation = Quaternion.Euler(0.0f, 0.0f, zRotation);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return panel;
        }

        private void ApplyTargetDisplay()
        {
            int displayIndex = ResolveTargetDisplayIndex();
            DisplayRoutingSettings.ActivateUnityDisplay(displayIndex);

            if (canvas != null)
            {
                canvas.targetDisplay = displayIndex;
            }
        }

        private int ResolveTargetDisplayIndex()
        {
            return DisplayRoutingSettings.ResolveUnityDisplayIndex(targetDisplayIndex);
        }

        private void ApplyLayoutIfNeeded(bool force)
        {
            if (canvas == null)
            {
                return;
            }

            if (!force && !autoApplyPanelLayout)
            {
                return;
            }

            int displayIndex = ResolveTargetDisplayIndex();
            Vector2Int displaySize = HologramPanelLayout.GetDisplaySize(displayIndex);
            if (!force && displaySize == lastLayoutSize)
            {
                return;
            }

            lastLayoutSize = displaySize;
            float panelSize = HologramPanelLayout.CalculatePanelSize(displaySize);
            ApplyPanelLayout(frontPanel, HologramPanelLayout.FrontAnchor, HologramPanelLayout.FrontOffset, panelSize);
            ApplyPanelLayout(leftPanel, HologramPanelLayout.LeftAnchor, HologramPanelLayout.LeftOffset, panelSize);
            ApplyPanelLayout(rightPanel, HologramPanelLayout.RightAnchor, HologramPanelLayout.RightOffset, panelSize);
        }

        private static void ApplyPanelLayout(RawImage panel, Vector2 anchor, Vector2 offset, float panelSize)
        {
            if (panel == null)
            {
                return;
            }

            RectTransform rectTransform = panel.rectTransform;
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.anchoredPosition = offset;
            rectTransform.sizeDelta = new Vector2(panelSize, panelSize);
        }

        private void EnsureRenderTextures()
        {
            EnsureRenderTexture(ref frontTexture, "EndingHologramModelFrontRenderTexture");
            EnsureRenderTexture(ref leftTexture, "EndingHologramModelLeftRenderTexture");
            EnsureRenderTexture(ref rightTexture, "EndingHologramModelRightRenderTexture");
            AssignPanelTextures();
        }

        private void EnsureRenderTexture(ref RenderTexture renderTexture, string textureName)
        {
            int width = Mathf.Max(16, renderTextureSize.x);
            int height = Mathf.Max(16, renderTextureSize.y);
            if (renderTexture != null &&
                renderTexture.width == width &&
                renderTexture.height == height)
            {
                return;
            }

            ReleaseRenderTexture(ref renderTexture);

            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = textureName
            };
            renderTexture.Create();
        }

        private void AssignPanelTextures()
        {
            AssignPanelTexture(frontPanel, frontTexture);
            AssignPanelTexture(leftPanel, leftTexture);
            AssignPanelTexture(rightPanel, rightTexture);
        }

        private void SetPanelVisibility(bool showFrontPanel, bool showLeftPanel, bool showRightPanel)
        {
            SetPanelVisible(frontPanel, showFrontPanel);
            SetPanelVisible(leftPanel, showLeftPanel);
            SetPanelVisible(rightPanel, showRightPanel);
        }

        private static void SetPanelVisible(RawImage panel, bool visible)
        {
            if (panel != null)
            {
                panel.enabled = visible;
            }
        }

        private static void AssignPanelTexture(RawImage panel, RenderTexture texture)
        {
            if (panel == null)
            {
                return;
            }

            panel.texture = texture;
            panel.color = Color.white;
        }

        private void ClearRenderTextures()
        {
            ClearRenderTexture(frontTexture);
            ClearRenderTexture(leftTexture);
            ClearRenderTexture(rightTexture);
        }

        private void ClearRenderTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return;
            }

            if (!renderTexture.IsCreated())
            {
                renderTexture.Create();
            }

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, clearColor);
            RenderTexture.active = previousActive;
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;

            if (canvas != null)
            {
                canvas.enabled = visible;
            }

            if (rigRoot != null)
            {
                rigRoot.SetActive(visible);
            }
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void OnDestroy()
        {
            ReleaseRenderTexture(ref frontTexture);
            ReleaseRenderTexture(ref leftTexture);
            ReleaseRenderTexture(ref rightTexture);
        }

        private void ReleaseRenderTexture(ref RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                return;
            }

            DetachCameraTarget(frontCamera, renderTexture);
            DetachCameraTarget(leftCamera, renderTexture);
            DetachCameraTarget(rightCamera, renderTexture);

            renderTexture.Release();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(renderTexture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            renderTexture = null;
        }

        private static void DetachCameraTarget(Camera targetCamera, RenderTexture renderTexture)
        {
            if (targetCamera != null && targetCamera.targetTexture == renderTexture)
            {
                targetCamera.targetTexture = null;
            }
        }
    }
}
