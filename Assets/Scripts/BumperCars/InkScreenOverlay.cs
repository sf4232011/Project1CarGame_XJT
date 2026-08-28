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
                overlayImage.raycastTarget = false;
            }
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
            if (overlayImage == null)
            {
                overlayImage = GetComponent<Image>();
            }

            if (overlayImage != null)
            {
                overlayImage.color = color;
            }
        }

        public void Show(float duration, float fadeTime)
        {
            if (overlayGroup == null)
            {
                return;
            }

            if (inkRoutine != null)
            {
                StopCoroutine(inkRoutine);
            }

            inkRoutine = StartCoroutine(ShowRoutine(Mathf.Max(0.05f, duration), Mathf.Max(0.01f, fadeTime)));
        }

        private IEnumerator ShowRoutine(float duration, float fadeTime)
        {
            overlayGroup.alpha = maxAlpha;

            float holdTime = Mathf.Max(0f, duration - fadeTime);
            if (holdTime > 0f)
            {
                yield return new WaitForSeconds(holdTime);
            }

            float timer = 0f;
            while (timer < fadeTime)
            {
                timer += Time.deltaTime;
                overlayGroup.alpha = Mathf.Lerp(maxAlpha, 0f, timer / fadeTime);
                yield return null;
            }

            overlayGroup.alpha = 0f;
            inkRoutine = null;
        }

        private static InkScreenOverlay FindOverlay(BumperCarPlayer player)
        {
            if (!Registry.TryGetValue(player, out InkScreenOverlay overlay) || overlay == null)
            {
                InkScreenOverlay[] overlays = FindObjectsOfType<InkScreenOverlay>(true);
                for (int i = 0; i < overlays.Length; i++)
                {
                    Registry[overlays[i].affectedPlayer] = overlays[i];
                }

                Registry.TryGetValue(player, out overlay);
            }

            return overlay;
        }
    }
}
