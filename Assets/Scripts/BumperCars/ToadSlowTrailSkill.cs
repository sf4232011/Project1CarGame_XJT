using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BumperCars
{
    [RequireComponent(typeof(BumperCarController))]
    public sealed class ToadSlowTrailSkill : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BumperCarController controller;
        [SerializeField] private SlowZone slowZonePrefab;
        [SerializeField] private Transform spawnPoint;

        [Header("Charge")]
        [SerializeField] private float maxCharge = 100f;
        [SerializeField] private float consumePerSecond = 25f;
        [SerializeField] private float recoverPerSecond = 15f;
        [SerializeField] private float minUsableCharge = 5f;

        [Header("Trail")]
        [SerializeField] private float spawnInterval = 0.15f;
        [SerializeField] private float zoneLifetime = 8f;
        [SerializeField, Range(0.05f, 1f)] private float slowMultiplier = 0.5f;
        [SerializeField] private float slowDuration = 1f;
        [SerializeField] private float spawnBackwardOffset = 0.85f;
        [SerializeField] private float groundRayHeight = 2f;
        [SerializeField] private float groundRayDistance = 5f;
        [SerializeField] private float groundOffset = 0.03f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("UI")]
        [SerializeField] private Image chargeFill;
        [SerializeField] private TMP_Text chargeText;
        [SerializeField] private Color readyColor = new Color(0.25f, 0.95f, 0.35f);
        [SerializeField] private Color usingColor = new Color(0.1f, 0.75f, 1f);
        [SerializeField] private Color depletedColor = new Color(0.45f, 0.45f, 0.45f);

        private float currentCharge;
        private float spawnTimer;
        private bool isGenerating;

        public float CurrentCharge => currentCharge;
        public float NormalizedCharge => maxCharge <= 0f ? 0f : currentCharge / maxCharge;
        public bool IsGenerating => isGenerating;

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<BumperCarController>();
            }

            currentCharge = maxCharge;
            spawnTimer = 0f;
            UpdateChargeUi();
        }

        private void Update()
        {
            bool wantsToUse = IsSkillKeyHeld();
            bool hasEnoughCharge = isGenerating ? currentCharge > 0f : currentCharge >= minUsableCharge;
            isGenerating = wantsToUse && hasEnoughCharge && slowZonePrefab != null;

            if (isGenerating)
            {
                currentCharge = Mathf.Max(0f, currentCharge - consumePerSecond * Time.deltaTime);
                spawnTimer -= Time.deltaTime;

                if (spawnTimer <= 0f)
                {
                    SpawnSlowZone();
                    spawnTimer = spawnInterval;
                }

                if (currentCharge <= 0f)
                {
                    isGenerating = false;
                    spawnTimer = 0f;
                }
            }
            else
            {
                currentCharge = Mathf.Min(maxCharge, currentCharge + recoverPerSecond * Time.deltaTime);
                spawnTimer = 0f;
            }

            UpdateChargeUi();
        }

        private bool IsSkillKeyHeld()
        {
            if (controller != null && controller.Player == BumperCarPlayer.Player2)
            {
                return Input.GetKey(KeyCode.Period);
            }

            return Input.GetKey(KeyCode.F);
        }

        private void SpawnSlowZone()
        {
            Vector3 spawnPosition = GetSpawnPosition();
            Quaternion spawnRotation = Quaternion.LookRotation(controller.DriveForwardDirection, Vector3.up);
            SlowZone zone = Instantiate(slowZonePrefab, spawnPosition, spawnRotation);
            zone.Configure(zoneLifetime, slowMultiplier, slowDuration, controller);
        }

        private Vector3 GetSpawnPosition()
        {
            Transform source = spawnPoint != null ? spawnPoint : transform;
            Vector3 position = source.position - controller.DriveForwardDirection.normalized * spawnBackwardOffset;
            Vector3 rayOrigin = position + Vector3.up * groundRayHeight;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * groundOffset;
            }

            position.y += groundOffset;
            return position;
        }

        private void UpdateChargeUi()
        {
            float normalized = NormalizedCharge;

            if (chargeFill != null)
            {
                chargeFill.fillAmount = normalized;
                chargeFill.color = currentCharge < minUsableCharge
                    ? depletedColor
                    : isGenerating ? usingColor : readyColor;
            }

            if (chargeText != null)
            {
                chargeText.text = $"{Mathf.CeilToInt(currentCharge)} / {Mathf.CeilToInt(maxCharge)}";
            }
        }
    }
}
