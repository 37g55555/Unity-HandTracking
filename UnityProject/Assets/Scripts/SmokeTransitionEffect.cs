using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace ShadowPrototype
{
    public class SmokeTransitionEffect : MonoBehaviour
    {
        private const float SmokeWidthRatio = 1f / 3f;

        [SerializeField] private GameStateManager stateManager;
        [SerializeField] private Color smokeColor = new Color(0.35220122f, 0.35220122f, 0.35220122f, 1f);
        [SerializeField] private float spawnInterval = 0.1f;
        [SerializeField] private Vector2 particleSizeRange = new Vector2(150f, 310f);
        [SerializeField] private float riseSpeed = 50f;
        [SerializeField] private float driftSpeed = 150f;
        [SerializeField] private float exitDuration = 2f;

        public event Action ExitCompleted;

        private readonly List<SmokeParticle> smokeParticles = new List<SmokeParticle>();
        private Canvas canvas;
        private RectTransform root;
        private Sprite smokeSprite;
        private bool isBillowing;
        private bool isExiting;
        private float spawnTimer;
        private float exitTimer;
        private int nextParticleId;

        private sealed class SmokeParticle
        {
            public RectTransform Transform;
            public Image Image;
            public float Seed;
            public float SpeedScale;
            public float Size;
            public Vector2 ExitStartPosition;
        }

        private void Awake()
        {
            CreateOverlay();
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged -= HandleStateChanged;
            }

            ClearParticles();
        }

        private void Update()
        {
            if (isExiting)
            {
                UpdateExit();
                return;
            }

            if (isBillowing)
            {
                UpdateBillow();
            }
        }

        private void HandleStateChanged(GameStateManager.PipelineState currentState)
        {
            if (currentState == GameStateManager.PipelineState.MeshExtracting)
            {
                BeginBillow();
            }
            else if (currentState == GameStateManager.PipelineState.HologramOutput)
            {
                BeginExit();
            }
        }

        private void CreateOverlay()
        {
            GameObject canvasObject = new GameObject("SmokeTransitionCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvasObject.AddComponent<CanvasScaler>();

            root = canvasObject.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            smokeSprite = CreateSmokeSprite();
        }

        private Sprite CreateSmokeSprite()
        {
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GeneratedSmokeSprite",
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                    float alpha = Mathf.SmoothStep(1f, 0f, distance);
                    alpha *= alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void BeginBillow()
        {
            isBillowing = true;
            isExiting = false;
            spawnTimer = 0f;
            exitTimer = 0f;
            nextParticleId = 0;
            ClearParticles();
            SetVisible(true);
            SpawnParticle();
        }

        private void BeginExit()
        {
            if (smokeParticles.Count == 0)
            {
                ExitCompleted?.Invoke();
                return;
            }

            isBillowing = false;
            isExiting = true;
            exitTimer = 0f;
            SetVisible(true);

            foreach (SmokeParticle particle in smokeParticles)
            {
                particle.ExitStartPosition = particle.Transform.anchoredPosition;
            }
        }

        private void UpdateBillow()
        {
            float width = Screen.width;
            float height = Screen.height;

            spawnTimer -= Time.deltaTime;
            while (spawnTimer <= 0f)
            {
                SpawnParticle();
                spawnTimer += Mathf.Max(0.01f, spawnInterval);
            }

            for (int i = smokeParticles.Count - 1; i >= 0; i--)
            {
                SmokeParticle particle = smokeParticles[i];
                Vector2 position = particle.Transform.anchoredPosition;
                position.y += riseSpeed * particle.SpeedScale * Time.deltaTime;
                position.x += Mathf.Sin(Time.time * particle.SpeedScale + particle.Seed) * driftSpeed * Time.deltaTime;

                if (position.y > height + particle.Size * 0.5f)
                {
                    DestroyParticleAt(i);
                    continue;
                }

                float heightFade = Mathf.InverseLerp(height + particle.Size * 0.5f, height * 0.08f, position.y);
                float pulse = 0.72f + Mathf.Sin(Time.time * 1.5f + particle.Seed) * 0.16f;
                SetParticleVisual(particle, particle.Size, smokeColor.a * heightFade * pulse);

                particle.Transform.anchoredPosition = new Vector2(
                    Mathf.Clamp(position.x, -width * 0.62f, width * 0.62f),
                    position.y);
            }
        }

        private void UpdateExit()
        {
            exitTimer += Time.deltaTime;
            float t = Mathf.Clamp01(exitTimer / Mathf.Max(0.01f, exitDuration));
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Vector2 target = new Vector2(Screen.width * 0.58f, -Screen.height * 0.18f);

            foreach (SmokeParticle particle in smokeParticles)
            {
                Vector2 start = particle.ExitStartPosition;
                Vector2 offset = new Vector2(
                    Mathf.Sin(particle.Seed) * 120f,
                    Mathf.Cos(particle.Seed) * 60f);

                particle.Transform.anchoredPosition = Vector2.Lerp(start, target + offset, eased);
                SetParticleVisual(particle, Mathf.Lerp(particle.Size, particle.Size * 0.72f, eased), smokeColor.a * (1f - eased));
            }

            if (t >= 1f)
            {
                isExiting = false;
                SetVisible(false);
                ClearParticles();
                ExitCompleted?.Invoke();
            }
        }

        private void SpawnParticle()
        {
            GameObject particleObject = new GameObject($"Smoke_{nextParticleId:000}", typeof(RectTransform));
            nextParticleId++;
            particleObject.transform.SetParent(root, false);

            Image image = particleObject.AddComponent<Image>();
            image.sprite = smokeSprite;
            image.raycastTarget = false;

            RectTransform particleTransform = image.rectTransform;
            particleTransform.anchorMin = new Vector2(0.5f, 0f);
            particleTransform.anchorMax = new Vector2(0.5f, 0f);
            particleTransform.pivot = new Vector2(0.5f, 0.5f);

            SmokeParticle particle = new SmokeParticle
            {
                Transform = particleTransform,
                Image = image,
                Seed = Random.Range(0f, 100f),
                SpeedScale = Random.Range(0.72f, 1.28f)
            };

            PlaceParticleAtBottom(particle);
            smokeParticles.Add(particle);
        }

        private void PlaceParticleAtBottom(SmokeParticle particle)
        {
            float width = Screen.width;
            float halfSmokeWidth = width * SmokeWidthRatio * 0.5f;
            particle.Size = GetRandomParticleSize();
            Vector2 startPosition = new Vector2(
                Random.Range(-halfSmokeWidth, halfSmokeWidth),
                Random.Range(-particle.Size * 0.45f, particle.Size * 0.1f));

            particle.Transform.anchoredPosition = startPosition;
            SetParticleVisual(particle, particle.Size, smokeColor.a * Random.Range(0.68f, 1f));
        }

        private void DestroyParticleAt(int index)
        {
            SmokeParticle particle = smokeParticles[index];
            smokeParticles.RemoveAt(index);

            if (particle.Transform != null)
            {
                Destroy(particle.Transform.gameObject);
            }
        }

        private void ClearParticles()
        {
            for (int i = smokeParticles.Count - 1; i >= 0; i--)
            {
                DestroyParticleAt(i);
            }
        }

        private void SetParticleVisual(SmokeParticle particle, float size, float alpha)
        {
            particle.Transform.sizeDelta = new Vector2(size, size);
            Color color = smokeColor;
            color.a = Mathf.Clamp01(alpha);
            particle.Image.color = color;
        }

        private float GetRandomParticleSize()
        {
            float minSize = Mathf.Min(particleSizeRange.x, particleSizeRange.y);
            float maxSize = Mathf.Max(particleSizeRange.x, particleSizeRange.y);
            return Random.Range(minSize, maxSize);
        }

        private void SetVisible(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }
        }
    }
}
