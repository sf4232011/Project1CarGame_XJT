using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BumperCars
{
    [RequireComponent(typeof(BumperCarController))]
    public sealed class RedCarInkRetreatSkill : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BumperCarController controller;
        [SerializeField] private InkSprayHitbox inkSprayPrefab;
        [SerializeField] private Transform inkSpawnPoint;
        [SerializeField] private ParticleSystem inkParticles;

        [Header("Ink VFX")]
        [SerializeField] private bool showInkVfx = true;
        [SerializeField, Min(0f)] private float inkEmissionHeight = 0.55f;
        [SerializeField, Min(1f)] private float inkFlightSpeed = 24f;

        [Header("Ink")]
        [SerializeField] private float inkDuration = 2.5f;
        [SerializeField] private float inkFadeTime = 0.5f;
        [SerializeField] private float inkSprayWidth = 2.5f;
        [SerializeField] private float inkSprayHeight = 1f;

        [Header("Retreat")]
        [SerializeField] private float retreatForce = 16f;
        [SerializeField] private float retreatDuration = 0.5f;
        [SerializeField] private float retreatForceInterval = 0.08f;
        [SerializeField] private float maxRetreatSpeed = 18f;

        [Header("Cooldown")]
        [SerializeField] private float cooldownTime = 8f;

        [Header("UI")]
        [SerializeField] private Image cooldownFill;
        [SerializeField] private TMP_Text cooldownText;
        [SerializeField] private bool preserveArtworkColors = false;
        [SerializeField] private Color readyColor = new Color(0.95f, 0.2f, 0.12f, 1f);
        [SerializeField] private Color cooldownColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private float cooldownTimer;
        private bool isRetreating;
        private InkFlightPath currentInkFlight;

        public bool IsReady => cooldownTimer <= 0f && !isRetreating;
        public float CooldownNormalized => cooldownTime <= 0f ? 0f : Mathf.Clamp01(cooldownTimer / cooldownTime);

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<BumperCarController>();
            }

            UpdateCooldownUi();
        }

        private void Update()
        {
            if (cooldownTimer > 0f)
            {
                cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
            }

            if (controller != null && controller.ControlsEnabled && IsReady && IsSkillPressed())
            {
                Activate();
            }

            UpdateCooldownUi();
        }

        private bool IsSkillPressed()
        {
            if (controller != null && controller.Player == BumperCarPlayer.Player2)
            {
                return Input.GetKeyDown(KeyCode.Period);
            }

            return Input.GetKeyDown(KeyCode.F);
        }

        private void Activate()
        {
            if (controller == null || !controller.ControlsEnabled || !IsReady)
            {
                return;
            }
            cooldownTimer = cooldownTime;
            BumperCarAudio.PlayInk(controller);
            currentInkFlight = InkFlightPath.Create(GetInkMouthPosition(), GetInkDirection(), inkFlightSpeed);
            SpawnInkSpray();

            if (showInkVfx)
            {
                SkillLiquidVfx.PlayInk(currentInkFlight, inkSprayWidth);
            }

            if (inkParticles != null)
            {
                inkParticles.Play();
            }

            StartCoroutine(RetreatRoutine());
        }

        private void SpawnInkSpray()
        {
            InkSprayHitbox hitbox = inkSprayPrefab != null
                ? Instantiate(inkSprayPrefab)
                : new GameObject("Ink flight hitbox", typeof(BoxCollider), typeof(Rigidbody)).AddComponent<InkSprayHitbox>();
            hitbox.transform.localScale = new Vector3(inkSprayWidth, inkSprayHeight, 2f);
            hitbox.ConfigureFlight(controller, currentInkFlight, inkDuration, inkFadeTime);
        }

        private Vector3 GetInkDirection()
        {
            // The face and mouth point along the vehicle's driving direction.
            Vector3 forward = controller != null ? controller.DriveForwardDirection : transform.forward;
            return Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        }

        private Vector3 GetInkMouthPosition()
        {
            if (inkSpawnPoint != null)
            {
                return inkSpawnPoint.position;
            }

            return transform.position + GetInkDirection() * 0.9f + Vector3.up * inkEmissionHeight;
        }

        private IEnumerator RetreatRoutine()
        {
            isRetreating = true;
            float timer = 0f;
            float forceTimer = 0f;

            controller.TemporarilyAllowReverse(retreatDuration);

            while (timer < retreatDuration && controller.ControlsEnabled)
            {
                timer += Time.fixedDeltaTime;
                forceTimer -= Time.fixedDeltaTime;

                if (forceTimer <= 0f)
                {
                    ApplyRetreatForce();
                    forceTimer = retreatForceInterval;
                }

                yield return new WaitForFixedUpdate();
            }

            isRetreating = false;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            isRetreating = false;
        }

        private void ApplyRetreatForce()
        {
            Vector3 backward = -controller.DriveForwardDirection.normalized;
            float currentRetreatSpeed = Mathf.Max(0f, Vector3.Dot(controller.Body.velocity, backward));
            if (currentRetreatSpeed >= maxRetreatSpeed)
            {
                return;
            }

            controller.TemporarilyAllowReverse(retreatForceInterval + 0.05f);
            controller.ApplyExternalForce(backward * retreatForce, ForceMode.VelocityChange);
        }

        private void UpdateCooldownUi()
        {
            if (cooldownFill != null)
            {
                cooldownFill.enabled = true;
                cooldownFill.fillAmount = IsReady ? 1f : 1f - CooldownNormalized;
                cooldownFill.color = preserveArtworkColors ? Color.white : IsReady ? readyColor : cooldownColor;
            }

            if (cooldownText != null)
            {
                string keyLabel = controller != null && controller.Player == BumperCarPlayer.Player2 ? "." : "F";
                cooldownText.text = cooldownTimer > 0f
                    ? $"INK [{keyLabel}]  {Mathf.CeilToInt(cooldownTimer)}s"
                    : $"INK [{keyLabel}]  READY";
            }
        }
    }
}
