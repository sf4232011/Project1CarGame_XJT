using System;
using UnityEngine;

namespace BumperCars
{
    public enum BumperCarPlayer
    {
        Player1,
        Player2
    }

    [RequireComponent(typeof(Rigidbody))]
    public sealed class BumperCarController : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private BumperCarPlayer player = BumperCarPlayer.Player1;
        [SerializeField] private bool useNegativeZAsForward = true;

        [Header("Movement")]
        [SerializeField] private float speedMultiplier = 2.25f;
        [SerializeField] private float acceleration = 60f;
        [SerializeField] private float reverseAcceleration = 32f;
        [SerializeField] private float maxSpeed = 26f;
        [SerializeField] private float maxReverseSpeed = 10f;

        [Header("Steering")]
        [SerializeField] private float turnSpeed = 120f;
        [SerializeField] private float maxSteerAngle = 35f;
        [SerializeField] private float steerSensitivity = 1f;
        [SerializeField] private float minSpeedToTurn = 0.5f;
        [SerializeField] private float turnSpeedFactor = 1f;
        [SerializeField] private float steerSmoothTime = 0.08f;
        [SerializeField] private bool invertSteeringWhileReversing = true;

        [Header("Physics Feel")]
        [SerializeField] private float accelerationFadeAtSpeed = 1.15f;
        [SerializeField] private float lateralGrip = 7f;
        [SerializeField] private float impactGripMultiplier = 0.35f;
        [SerializeField] private float rollingResistance = 0.55f;
        [SerializeField] private float downforce = 18f;
        [SerializeField] private float highSpeedSteeringReduction = 0.45f;
        [SerializeField] private float maxLateralSpeed = 9f;
        [SerializeField] private float impactSpinTorque = 2.4f;
        [SerializeField] private float idleDrag = 1.2f;
        [SerializeField] private float acceleratingDrag = 0.35f;

        [Header("Grounding")]
        [SerializeField] private float groundCheckDistance = 0.85f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float airborneControlMultiplier = 0.2f;

        [Header("Collision Recovery")]
        [SerializeField] private float impactRecoveryTime = 0.18f;
        [SerializeField] private float groundedExtraForce = 12f;
        [SerializeField] private float maxBackwardDrift = 1.2f;

        private Rigidbody body;
        private float throttleInput;
        private float steerInput;
        private float smoothedSteerInput;
        private float steerInputVelocity;
        private float impactRecoveryTimer;
        private float previousPlanarSpeed;
        private float speedModifierMultiplier = 1f;
        private float speedModifierTimer;
        private float externalReverseTimer;
        private bool controlsEnabled = true;

        public event Action<float> ImpactReceived;

        public BumperCarPlayer Player => player;
        public Rigidbody Body => body;
        public float CurrentPlanarSpeed => GetPlanarVelocity(body.velocity).magnitude;
        public float CurrentForwardSpeed => Mathf.Max(0f, Vector3.Dot(GetPlanarVelocity(body.velocity), DriveForward));
        public float CurrentReverseSpeed => Mathf.Max(0f, -Vector3.Dot(GetPlanarVelocity(body.velocity), DriveForward));
        public float ImpactSpeedSample => Mathf.Max(CurrentPlanarSpeed, previousPlanarSpeed);
        public Vector3 DriveForwardDirection => DriveForward;
        public float CurrentSpeedModifier => speedModifierMultiplier;

        private Vector3 DriveForward => useNegativeZAsForward ? -transform.forward : transform.forward;
        private float EffectiveAcceleration => acceleration * speedMultiplier * speedModifierMultiplier;
        private float EffectiveReverseAcceleration => reverseAcceleration * speedMultiplier * speedModifierMultiplier;
        private float EffectiveMaxSpeed => maxSpeed * speedMultiplier * speedModifierMultiplier;
        private float EffectiveMaxReverseSpeed => maxReverseSpeed * speedMultiplier * speedModifierMultiplier;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 10f;
        }

        private void Update()
        {
            if (!controlsEnabled)
            {
                throttleInput = 0f;
                steerInput = 0f;
                return;
            }

            throttleInput = GetThrottleInput();
            steerInput = GetSteerInput();
        }

        private void FixedUpdate()
        {
            previousPlanarSpeed = CurrentPlanarSpeed;

            if (impactRecoveryTimer > 0f)
            {
                impactRecoveryTimer -= Time.fixedDeltaTime;
            }

            if (externalReverseTimer > 0f)
            {
                externalReverseTimer -= Time.fixedDeltaTime;
            }

            UpdateSpeedModifier();

            bool grounded = IsGrounded();
            float controlMultiplier = grounded ? 1f : airborneControlMultiplier;

            ApplyForwardAcceleration(controlMultiplier);
            ApplySteering(controlMultiplier);
            ApplyLateralGrip(controlMultiplier);
            ApplyRollingResistance(grounded);
            ClampPlanarSpeed();
            LimitBackwardDrift();

            body.drag = Mathf.Abs(throttleInput) > 0f ? acceleratingDrag : idleDrag;
            body.AddForce(Vector3.down * (groundedExtraForce + CurrentPlanarSpeed * downforce * 0.08f), ForceMode.Acceleration);
        }

        public void SetControlsEnabled(bool enabled)
        {
            controlsEnabled = enabled;
            if (!enabled)
            {
                throttleInput = 0f;
                steerInput = 0f;
                smoothedSteerInput = 0f;
            }
        }

        public void ReceiveImpact(Vector3 impulse, float shakeIntensity)
        {
            impactRecoveryTimer = impactRecoveryTime;
            body.AddForce(impulse, ForceMode.Impulse);
            Vector3 spinAxis = Vector3.Cross(Vector3.up, impulse.normalized);
            body.AddTorque(Vector3.up * Vector3.Dot(spinAxis, transform.right) * impactSpinTorque, ForceMode.Impulse);
            ImpactReceived?.Invoke(shakeIntensity);
        }

        public void ApplySpeedModifier(float multiplier, float duration)
        {
            if (duration <= 0f)
            {
                return;
            }

            speedModifierMultiplier = Mathf.Min(speedModifierMultiplier, Mathf.Clamp(multiplier, 0.05f, 1f));
            speedModifierTimer = Mathf.Max(speedModifierTimer, duration);
        }

        public void TemporarilyAllowReverse(float duration)
        {
            if (duration <= 0f)
            {
                return;
            }

            externalReverseTimer = Mathf.Max(externalReverseTimer, duration);
        }

        public void ApplyExternalForce(Vector3 force, ForceMode forceMode)
        {
            body.AddForce(force, forceMode);
        }

        private void UpdateSpeedModifier()
        {
            if (speedModifierTimer <= 0f)
            {
                speedModifierMultiplier = 1f;
                return;
            }

            speedModifierTimer -= Time.fixedDeltaTime;
            if (speedModifierTimer <= 0f)
            {
                speedModifierMultiplier = 1f;
            }
        }

        private void ApplyForwardAcceleration(float controlMultiplier)
        {
            if (throttleInput > 0f)
            {
                float speedFactor = 1f - Mathf.Clamp01(CurrentForwardSpeed / (EffectiveMaxSpeed * Mathf.Max(0.01f, accelerationFadeAtSpeed)));
                body.AddForce(DriveForward * (EffectiveAcceleration * throttleInput * speedFactor * controlMultiplier), ForceMode.Acceleration);
            }
            else if (throttleInput < 0f)
            {
                float speedFactor = 1f - Mathf.Clamp01(CurrentReverseSpeed / EffectiveMaxReverseSpeed);
                body.AddForce(-DriveForward * (EffectiveReverseAcceleration * -throttleInput * speedFactor * controlMultiplier), ForceMode.Acceleration);
            }
        }

        private void ApplySteering(float controlMultiplier)
        {
            smoothedSteerInput = Mathf.SmoothDamp(smoothedSteerInput, steerInput, ref steerInputVelocity, steerSmoothTime);

            float drivingSpeed = Mathf.Max(CurrentForwardSpeed, CurrentReverseSpeed);
            if (Mathf.Abs(smoothedSteerInput) < 0.01f || drivingSpeed < minSpeedToTurn)
            {
                return;
            }

            float speedFactor = Mathf.Clamp01(drivingSpeed / Mathf.Max(minSpeedToTurn, EffectiveMaxSpeed));
            float speedInfluence = Mathf.Clamp01(speedFactor * Mathf.Max(0f, turnSpeedFactor));
            float highSpeedFactor = Mathf.Lerp(1f, highSpeedSteeringReduction, speedFactor);
            float steeringDirection = invertSteeringWhileReversing && CurrentReverseSpeed > CurrentForwardSpeed ? -smoothedSteerInput : smoothedSteerInput;
            float steerAmount = Mathf.Clamp(steeringDirection * steerSensitivity, -1f, 1f);
            float requestedDegreesPerSecond = steerAmount * turnSpeed * speedInfluence * highSpeedFactor * controlMultiplier;
            float cappedDegreesPerSecond = Mathf.Clamp(requestedDegreesPerSecond, -maxSteerAngle, maxSteerAngle);
            float turnDegrees = cappedDegreesPerSecond * Time.fixedDeltaTime;
            Quaternion nextRotation = body.rotation * Quaternion.Euler(0f, turnDegrees, 0f);
            body.MoveRotation(nextRotation);
        }

        private void ApplyLateralGrip(float controlMultiplier)
        {
            Vector3 planarVelocity = GetPlanarVelocity(body.velocity);
            Vector3 lateralVelocity = Vector3.Project(planarVelocity, transform.right);
            float grip = lateralGrip * controlMultiplier;
            if (impactRecoveryTimer > 0f)
            {
                grip *= impactGripMultiplier;
            }

            body.AddForce(-lateralVelocity * grip, ForceMode.Acceleration);
        }

        private void ApplyRollingResistance(bool grounded)
        {
            if (!grounded)
            {
                return;
            }

            Vector3 planarVelocity = GetPlanarVelocity(body.velocity);
            body.AddForce(-planarVelocity * rollingResistance, ForceMode.Acceleration);
        }

        private void ClampPlanarSpeed()
        {
            Vector3 velocity = body.velocity;
            Vector3 planarVelocity = GetPlanarVelocity(velocity);
            Vector3 forward = DriveForward;
            Vector3 right = transform.right;
            float forwardSpeed = Vector3.Dot(planarVelocity, forward);
            float lateralSpeed = Vector3.Dot(planarVelocity, right);
            float minForwardSpeed = throttleInput < 0f || externalReverseTimer > 0f
                ? -EffectiveMaxReverseSpeed
                : impactRecoveryTimer > 0f ? -maxBackwardDrift : 0f;
            float clampedForwardSpeed = Mathf.Clamp(forwardSpeed, minForwardSpeed, EffectiveMaxSpeed);
            float clampedLateralSpeed = Mathf.Clamp(lateralSpeed, -maxLateralSpeed, maxLateralSpeed);

            if (Mathf.Approximately(forwardSpeed, clampedForwardSpeed) && Mathf.Approximately(lateralSpeed, clampedLateralSpeed))
            {
                return;
            }

            Vector3 clampedPlanar = forward * clampedForwardSpeed + right * clampedLateralSpeed;
            body.velocity = new Vector3(clampedPlanar.x, velocity.y, clampedPlanar.z);
        }

        private void LimitBackwardDrift()
        {
            if (throttleInput < 0f || externalReverseTimer > 0f)
            {
                return;
            }

            if (impactRecoveryTimer > 0f)
            {
                return;
            }

            Vector3 planarVelocity = GetPlanarVelocity(body.velocity);
            float forwardSpeed = Vector3.Dot(planarVelocity, DriveForward);
            if (forwardSpeed >= 0f)
            {
                return;
            }

            Vector3 correctedPlanar = planarVelocity - DriveForward * forwardSpeed;
            body.velocity = new Vector3(correctedPlanar.x, body.velocity.y, correctedPlanar.z);
        }

        private bool IsGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.08f;
            return Physics.Raycast(origin, Vector3.down, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
        }

        private float GetThrottleInput()
        {
            if (player == BumperCarPlayer.Player1)
            {
                if (Input.GetKey(KeyCode.W))
                {
                    return 1f;
                }

                if (Input.GetKey(KeyCode.S))
                {
                    return -1f;
                }

                return 0f;
            }

            if (Input.GetKey(KeyCode.UpArrow))
            {
                return 1f;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                return -1f;
            }

            return 0f;
        }

        private float GetSteerInput()
        {
            if (player == BumperCarPlayer.Player1)
            {
                return ReadAxis(KeyCode.A, KeyCode.D);
            }

            return ReadAxis(KeyCode.LeftArrow, KeyCode.RightArrow);
        }

        private static float ReadAxis(KeyCode leftKey, KeyCode rightKey)
        {
            float value = 0f;
            if (Input.GetKey(leftKey))
            {
                value -= 1f;
            }

            if (Input.GetKey(rightKey))
            {
                value += 1f;
            }

            return value;
        }

        private static Vector3 GetPlanarVelocity(Vector3 velocity)
        {
            return new Vector3(velocity.x, 0f, velocity.z);
        }
    }
}
