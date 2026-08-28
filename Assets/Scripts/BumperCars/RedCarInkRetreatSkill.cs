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

        [Header("Ink")]
        [SerializeField] private float inkDuration = 2.5f;
        [SerializeField] private float inkFadeTime = 0.5f;
        [SerializeField] private float inkSprayRange = 6f;
        [SerializeField] private float inkSprayWidth = 2.5f;
        [SerializeField] private float inkSprayLifetime = 0.35f;
        [SerializeField] private LayerMask targetMask = ~0;

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
        [SerializeField] private Color readyColor = new Color(0.95f, 0.2f, 0.12f, 1f);
        [SerializeField] private Color cooldownColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private float cooldownTimer;
        private bool isRetreating;

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

            if (IsReady && IsSkillPressed())
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
            cooldownTimer = cooldownTime;
            SpawnInkSpray();

            if (inkParticles != null)
            {
                inkParticles.Play();
            }

            StartCoroutine(RetreatRoutine());
        }

        private void SpawnInkSpray()
        {
            if (inkSprayPrefab != null)
            {
                Vector3 spawnPosition = GetInkSpawnPosition();
                Quaternion spawnRotation = Quaternion.LookRotation(-controller.DriveForwardDirection, Vector3.up);
                InkSprayHitbox hitbox = Instantiate(inkSprayPrefab, spawnPosition, spawnRotation);
                hitbox.Configure(controller, inkSprayLifetime, inkDuration, inkFadeTime);
                hitbox.transform.localScale = new Vector3(inkSprayWidth, hitbox.transform.localScale.y, inkSprayRange);
                return;
            }

            HitTargetsWithOverlapBox();
        }

        private Vector3 GetInkSpawnPosition()
        {
            if (inkSpawnPoint != null)
            {
                return inkSpawnPoint.position;
            }

            return transform.position - controller.DriveForwardDirection.normalized * (inkSprayRange * 0.5f);
        }

        private void HitTargetsWithOverlapBox()
        {
            Vector3 center = GetInkSpawnPosition();
            Quaternion rotation = Quaternion.LookRotation(-controller.DriveForwardDirection, Vector3.up);
            Vector3 halfExtents = new Vector3(inkSprayWidth * 0.5f, 1.25f, inkSprayRange * 0.5f);
            Collider[] hits = Physics.OverlapBox(center, halfExtents, rotation, targetMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                BumperCarController target = hits[i].GetComponentInParent<BumperCarController>();
                if (target != null && target != controller)
                {
                    InkScreenOverlay.ShowForPlayer(target.Player, inkDuration, inkFadeTime);
                }
            }
        }

        private IEnumerator RetreatRoutine()
        {
            isRetreating = true;
            float timer = 0f;
            float forceTimer = 0f;

            controller.TemporarilyAllowReverse(retreatDuration);

            while (timer < retreatDuration)
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
                cooldownFill.color = IsReady ? readyColor : cooldownColor;
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
