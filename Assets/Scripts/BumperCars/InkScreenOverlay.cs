using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BumperCars
{
    public sealed class InkScreenOverlay : MonoBehaviour
    {
        private static readonly Dictionary<BumperCarPlayer, InkScreenOverlay> Registry = new Dictionary<BumperCarPlayer, InkScreenOverlay>();

        [SerializeField] private BumperCarPlayer affectedPlayer = BumperCarPlayer.Player1;
        [SerializeField] private CanvasGroup overlayGroup;
        [SerializeField] private Image overlayImage;
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.82f;

        private Coroutine inkRoutine;
        private RectTransform overlayRect;
        private RectTransform[] splatRects;
        private InkSplatGraphic[] splatGraphics;
        private Color requestedColor = new Color(0.045f, 0.018f, 0.085f, 1f);

        [Header("Ink Appearance")]
        [SerializeField] private bool stylizedSplatter = true;

        private static readonly Vector2[] SplatAnchors =
        {
            new Vector2(0.51f, 0.51f), new Vector2(0.09f, 0.76f),
            new Vector2(0.90f, 0.83f), new Vector2(0.13f, 0.16f),
            new Vector2(0.91f, 0.15f), new Vector2(0.60f, 1.01f)
        };
        private static readonly Vector2[] SplatSizes =
        {
            new Vector2(0.88f, 0.69f), new Vector2(0.62f, 0.56f),
            new Vector2(0.59f, 0.53f), new Vector2(0.66f, 0.53f),
            new Vector2(0.62f, 0.61f), new Vector2(0.55f, 0.46f)
        };
        private static readonly Vector2[] DropletAnchors =
        {
            new Vector2(0.28f, 0.91f), new Vector2(0.76f, 0.66f),
            new Vector2(0.28f, 0.40f), new Vector2(0.68f, 0.24f),
            new Vector2(0.48f, 0.08f), new Vector2(0.95f, 0.48f),
            new Vector2(0.06f, 0.43f), new Vector2(0.35f, 0.68f)
        };

        private void Awake()
        {
            if (overlayGroup == null)
            {
                overlayGroup = GetComponent<CanvasGroup>();
            }

            if (overlayImage == null)
            {
                overlayImage = GetComponent<Image>();
            }

            if (overlayGroup != null)
            {
                overlayGroup.alpha = 0f;
                overlayGroup.blocksRaycasts = false;
                overlayGroup.interactable = false;
            }

            if (overlayImage != null)
            {
                requestedColor = overlayImage.color;
                overlayImage.raycastTarget = false;
            }

            overlayRect = transform as RectTransform;
            EnsureSplatter();
            ApplyOverlayColor();
        }

        private void OnEnable()
        {
            Registry[affectedPlayer] = this;
        }

        private void OnDisable()
        {
            if (Registry.TryGetValue(affectedPlayer, out InkScreenOverlay overlay) && overlay == this)
            {
                Registry.Remove(affectedPlayer);
            }

            if (inkRoutine != null)
            {
                StopCoroutine(inkRoutine);
                inkRoutine = null;
            }

            if (overlayGroup != null)
            {
                overlayGroup.alpha = 0f;
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            LayoutSplatter();
        }

        public static void ShowForPlayer(BumperCarPlayer player, float duration, float fadeTime)
        {
            InkScreenOverlay overlay = FindOverlay(player);
            if (overlay != null)
            {
                overlay.Show(duration, fadeTime);
            }
        }

        public static void SetColorForPlayer(BumperCarPlayer player, Color color)
        {
            InkScreenOverlay overlay = FindOverlay(player);
            if (overlay != null)
            {
                overlay.SetOverlayColor(color);
            }
        }

        public void SetOverlayColor(Color color)
        {
            requestedColor = color;
            if (overlayImage == null)
            {
                overlayImage = GetComponent<Image>();
            }

            ApplyOverlayColor();
        }

        public void Show(float duration, float fadeTime)
        {
            if (overlayGroup == null || !isActiveAndEnabled)
            {
                return;
            }

            EnsureSplatter();
            BumperCarAudio.PlayInkHit(affectedPlayer);
            if (inkRoutine != null)
            {
                StopCoroutine(inkRoutine);
            }

            inkRoutine = StartCoroutine(ShowRoutine(Mathf.Max(0.05f, duration), Mathf.Max(0.01f, fadeTime)));
        }

        private IEnumerator ShowRoutine(float duration, float fadeTime)
        {
            float holdTime = Mathf.Max(0f, duration - fadeTime);
            float totalTime = holdTime + fadeTime;
            float timer = 0f;
            overlayGroup.alpha = maxAlpha;
            AnimateSplatter(0f);

            while (timer < totalTime)
            {
                float fade = timer <= holdTime ? 1f : 1f - Mathf.Clamp01((timer - holdTime) / fadeTime);
                overlayGroup.alpha = maxAlpha * fade;
                AnimateSplatter(timer);
                timer += Time.deltaTime;
                yield return null;
            }

            overlayGroup.alpha = 0f;
            inkRoutine = null;
        }

        private void EnsureSplatter()
        {
            if (!stylizedSplatter || overlayRect == null || splatRects != null)
            {
                return;
            }

            // The original overlay rect is already anchored to this player's half-screen.
            if (GetComponent<RectMask2D>() == null)
            {
                gameObject.AddComponent<RectMask2D>();
            }

            int count = SplatAnchors.Length + DropletAnchors.Length;
            splatRects = new RectTransform[count];
            splatGraphics = new InkSplatGraphic[count];
            for (int i = 0; i < count; i++)
            {
                bool droplet = i >= SplatAnchors.Length;
                GameObject shape = new GameObject(droplet ? "Ink Drip" : "Ink Splat", typeof(RectTransform), typeof(CanvasRenderer), typeof(InkSplatGraphic));
                shape.layer = gameObject.layer;
                RectTransform rect = (RectTransform)shape.transform;
                rect.SetParent(transform, false);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.localRotation = Quaternion.Euler(0f, 0f, droplet ? (i % 3 - 1) * 9f : i * 39f - 12f);
                InkSplatGraphic graphic = shape.GetComponent<InkSplatGraphic>();
                graphic.raycastTarget = false;
                graphic.Configure(i + 3, droplet);
                splatRects[i] = rect;
                splatGraphics[i] = graphic;
            }

            LayoutSplatter();
            ApplyOverlayColor();
        }

        private void LayoutSplatter()
        {
            if (overlayRect == null || splatRects == null)
            {
                return;
            }

            Vector2 size = overlayRect.rect.size;
            float dropletUnit = Mathf.Min(size.x, size.y);
            for (int i = 0; i < splatRects.Length; i++)
            {
                RectTransform rect = splatRects[i];
                if (rect == null)
                {
                    continue;
                }

                bool droplet = i >= SplatAnchors.Length;
                Vector2 anchor = droplet ? DropletAnchors[i - SplatAnchors.Length] : SplatAnchors[i];
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = droplet
                    ? new Vector2(dropletUnit * (0.033f + i % 3 * 0.01f), dropletUnit * (0.072f + i % 4 * 0.015f))
                    : Vector2.Scale(size, SplatSizes[i]);
            }
        }

        private void ApplyOverlayColor()
        {
            Color tint = requestedColor;
            // Existing scenes serialize pure black; give it a subtle plum undertone.
            if (Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b)) < 0.025f)
            {
                tint = new Color(0.045f, 0.018f, 0.085f, requestedColor.a);
            }

            if (overlayImage != null)
            {
                Color wash = stylizedSplatter ? tint : requestedColor;
                if (stylizedSplatter)
                {
                    wash.a *= 0.45f;
                }
                overlayImage.color = wash;
            }

            if (splatGraphics != null)
            {
                Color body = new Color(tint.r, tint.g, tint.b, tint.a * 0.97f);
                for (int i = 0; i < splatGraphics.Length; i++)
                {
                    splatGraphics[i].color = body;
                }
            }
        }

        private void AnimateSplatter(float elapsed)
        {
            if (splatRects == null)
            {
                return;
            }

            float unit = Mathf.Min(overlayRect.rect.width, overlayRect.rect.height);
            for (int i = 0; i < splatRects.Length; i++)
            {
                bool droplet = i >= SplatAnchors.Length;
                float pop = Mathf.Clamp01((elapsed - i * 0.004f) / 0.11f);
                // Only transforms animate; the cached silhouette never rebuilds per frame.
                float scale = Mathf.Lerp(0.56f, 1f, 1f - Mathf.Pow(1f - pop, 3f)) + Mathf.Sin(pop * Mathf.PI) * 0.09f;
                float drip = Mathf.Max(0f, elapsed - 0.12f);
                float stretch = droplet ? Mathf.Min(0.30f, drip * 0.10f) : Mathf.Min(0.035f, drip * 0.014f);
                splatRects[i].localScale = new Vector3(scale, scale + stretch, 1f);
                float fall = drip * unit * (droplet ? 0.018f + i % 3 * 0.007f : 0.0025f + i % 2 * 0.0015f);
                splatRects[i].anchoredPosition = Vector2.down * fall;
            }
        }

        private static InkScreenOverlay FindOverlay(BumperCarPlayer player)
        {
            if (!Registry.TryGetValue(player, out InkScreenOverlay overlay) || overlay == null || !overlay.isActiveAndEnabled)
            {
                InkScreenOverlay[] overlays = FindObjectsOfType<InkScreenOverlay>(true);
                for (int i = 0; i < overlays.Length; i++)
                {
                    if (overlays[i].isActiveAndEnabled)
                    {
                        Registry[overlays[i].affectedPlayer] = overlays[i];
                    }
                }

                Registry.TryGetValue(player, out overlay);
            }

            return overlay;
        }
    }
}
