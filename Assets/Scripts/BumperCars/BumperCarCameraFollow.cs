using UnityEngine;

namespace BumperCars
{
    [RequireComponent(typeof(Camera))]
    public sealed class BumperCarCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 5.2f, -8.2f);
        [SerializeField] private float lookHeight = 1.1f;
        [SerializeField] private Vector3 lookOffset = Vector3.zero;
        [SerializeField] private float positionSmoothTime = 0.08f;
        [SerializeField] private float rotationSharpness = 14f;
        [SerializeField] private float shakeDecay = 4.5f;
        [SerializeField] private float maxShakeOffset = 0.28f;

        private Vector3 followVelocity;
        private float shakeIntensity;

        public Camera Camera { get; private set; }

        private void Awake()
        {
            Camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            BumperCarController controller = target == null ? null : target.GetComponent<BumperCarController>();
            if (controller != null)
            {
                controller.ImpactReceived += Shake;
            }
        }

        private void OnDisable()
        {
            BumperCarController controller = target == null ? null : target.GetComponent<BumperCarController>();
            if (controller != null)
            {
                controller.ImpactReceived -= Shake;
            }
        }

        private void LateUpdate()
        {
            UpdateFollow(Time.deltaTime);
        }

        private void UpdateFollow(float deltaTime)
        {
            if (target == null)
            {
                return;
            }

            // Camera distances are world units, independent of imported model scale.
            Vector3 desiredPosition = target.position + target.rotation * localOffset;
            Vector3 shakeOffset = Random.insideUnitSphere * (shakeIntensity * maxShakeOffset);
            shakeOffset.y *= 0.4f;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition + shakeOffset, ref followVelocity,
                positionSmoothTime, Mathf.Infinity, deltaTime);

            Vector3 lookPoint = target.position + target.rotation * lookOffset + Vector3.up * lookHeight;
            Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * deltaTime));

            shakeIntensity = Mathf.MoveTowards(shakeIntensity, 0f, shakeDecay * deltaTime);
        }

        public void SetTarget(Transform newTarget)
        {
            if (target == newTarget)
            {
                return;
            }

            if (isActiveAndEnabled && target != null)
            {
                BumperCarController oldController = target.GetComponent<BumperCarController>();
                if (oldController != null)
                {
                    oldController.ImpactReceived -= Shake;
                }
            }

            target = newTarget;

            if (isActiveAndEnabled && target != null)
            {
                BumperCarController newController = target.GetComponent<BumperCarController>();
                if (newController != null)
                {
                    newController.ImpactReceived += Shake;
                }
            }
        }

        public void Shake(float intensity)
        {
            shakeIntensity = Mathf.Max(shakeIntensity, Mathf.Clamp01(intensity));
        }
    }
}
