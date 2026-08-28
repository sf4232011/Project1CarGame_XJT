using UnityEngine;

namespace BumperCars
{
    [RequireComponent(typeof(Camera))]
    public sealed class BumperCarCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 5.2f, -8.2f);
        [SerializeField] private float lookHeight = 1.1f;
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
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = target.TransformPoint(localOffset);
            Vector3 shakeOffset = Random.insideUnitSphere * (shakeIntensity * maxShakeOffset);
            shakeOffset.y *= 0.4f;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition + shakeOffset, ref followVelocity, positionSmoothTime);

            Vector3 lookPoint = target.position + Vector3.up * lookHeight;
            Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));

            shakeIntensity = Mathf.MoveTowards(shakeIntensity, 0f, shakeDecay * Time.deltaTime);
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
