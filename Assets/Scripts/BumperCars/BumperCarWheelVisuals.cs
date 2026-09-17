using System;
using UnityEngine;

namespace BumperCars
{
    // The existing arcade controller drives one Rigidbody. Wheels only animate
    // visually; adding WheelColliders would introduce a second driving model.
    [RequireComponent(typeof(BumperCarController))]
    public sealed class BumperCarWheelVisuals : MonoBehaviour
    {
        [Serializable]
        private sealed class Wheel
        {
            public Transform mesh;
            public bool steering;
            [Min(0.01f)] public float radius = 1f;
            public Vector3 meshCenter;

            [NonSerialized] public Vector3 restPosition;
            [NonSerialized] public Quaternion restRotation;
            [NonSerialized] public Vector3 axle;
            [NonSerialized] public Vector3 up;
            [NonSerialized] public float angle;
        }

        [SerializeField] private BumperCarController controller;
        [SerializeField] private float steeringAngle = 30f;
        [SerializeField] private Wheel[] wheels = Array.Empty<Wheel>();

        private void Awake()
        {
            if (controller == null) controller = GetComponent<BumperCarController>();
            foreach (Wheel wheel in wheels)
            {
                if (wheel.mesh == null) continue;
                wheel.restPosition = wheel.mesh.localPosition;
                wheel.restRotation = wheel.mesh.localRotation;
                Transform parent = wheel.mesh.parent;
                Vector3 right = Vector3.Cross(Vector3.up, controller.DriveForwardDirection).normalized;
                wheel.axle = Quaternion.Inverse(wheel.restRotation) * parent.InverseTransformDirection(right);
                wheel.up = parent.InverseTransformDirection(Vector3.up);
            }
        }

        private void LateUpdate()
        {
            AnimateWheels(Time.deltaTime);
        }

        private void AnimateWheels(float deltaTime)
        {
            if (controller == null || controller.Body == null) return;
            float speed = Vector3.Dot(controller.Body.velocity, controller.DriveForwardDirection);
            foreach (Wheel wheel in wheels)
            {
                if (wheel.mesh == null) continue;
                wheel.angle = (wheel.angle + speed * deltaTime / Mathf.Max(0.01f, wheel.radius) * Mathf.Rad2Deg) % 360f;
                Quaternion steer = Quaternion.AngleAxis(wheel.steering ? controller.SteeringInput * steeringAngle : 0f, wheel.up);
                Quaternion rotation = steer * wheel.restRotation * Quaternion.AngleAxis(wheel.angle, wheel.axle);
                // Imported tire pivots can be offset or mirrored. Rotate about the
                // mesh center so the wheel does not orbit its original pivot.
                Vector3 center = Vector3.Scale(wheel.meshCenter, wheel.mesh.localScale);
                wheel.mesh.localRotation = rotation;
                wheel.mesh.localPosition = wheel.restPosition + wheel.restRotation * center - rotation * center;
            }
        }
    }
}
