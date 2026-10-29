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
        [SerializeField, Min(0f), Tooltip("Peak commanded yaw speed in degrees per second.")] private float turnSpeed = 140f;
        [SerializeField, Range(0f, 90f), Tooltip("Maximum front-wheel steering command in degrees.")] private float maxSteerAngle = 90f;
        [SerializeField] private float steerSensitivity = 1f;
        [SerializeField, Min(0f)] private float minSpeedToTurn = 0.15f;
        [SerializeField] private float turnSpeedFactor = 1f;
        [SerializeField, Min(0.01f)] private float steerSmoothTime = 0.12f;
        [SerializeField, Min(0.1f)] private float steeringFullSpeed = 10f;
        [SerializeField, Min(1f)] private float steeringAcceleration = 540f;
        [SerializeField, Min(1f)] private float steeringReturnAcceleration = 720f;
        [SerializeField, Min(0f)] private float turningGripBoost = 2f;
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
        [SerializeField] private Transform groundCheckOrigin;
        [SerializeField] private float groundCheckDistance = 0.85f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float airborneControlMultiplier = 0.2f;

        [Header("Road Alignment")]
        [SerializeField] private bool alignToGround = true;
        [SerializeField, Min(0f)] private float groundAlignmentSpeed = 10f;
        [SerializeField, Range(0f, 65f)] private float maxGroundAngle = 55f;
        [SerializeField, Min(0.1f)] private float groundProbeLift = 2f;
        [SerializeField, Min(0.1f)] private float groundProbeDepth = 8f;

        [Header("Collision Recovery")]
        [SerializeField] private float impactRecoveryTime = 0.18f;
        [SerializeField] private float groundedExtraForce = 12f;
        [SerializeField] private float maxBackwardDrift = 1.2f;

        private Rigidbody body;
        private float throttleInput;
        private float steerInput;
        private float smoothedSteerInput;
        private float steerInputVelocity;
        private float steeringYawRate;
        private float impactRecoveryTimer;
        private float previousPlanarSpeed;
        private float speedModifierMultiplier = 1f;
        private float speedModifierTimer;
        private float externalReverseTimer;
        private bool controlsEnabled = true;
        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private readonly Vector3[] groundPoints = new Vector3[4];
        private BoxCollider chassisCollider;

        public event Action<float> ImpactReceived;

        public BumperCarPlayer Player => player;
        public bool ControlsEnabled => controlsEnabled && isActiveAndEnabled;
        public float SteeringInput => smoothedSteerInput;
        public float MaxSteeringAngle => Mathf.Clamp(maxSteerAngle, 0f, 90f);
        public float SteeringCommandAngle => smoothedSteerInput * MaxSteeringAngle;
        public float SteeringAngle => SteeringCommandAngle * SteeringSpeedDamping;
        public float SteeringYawRate => steeringYawRate;
        public float ThrottleInput => throttleInput;
        public float MaxDrivingSpeed => Mathf.Max(1f, maxSpeed * speedMultiplier);
        public bool IsOnGround { get; private set; }
        public Rigidbody Body => body;
        public float CurrentPlanarSpeed => GetPlanarVelocity(body.velocity).magnitude;
        public float CurrentForwardSpeed => Mathf.Max(0f, Vector3.Dot(GetPlanarVelocity(body.velocity), DriveForward));
        public float CurrentReverseSpeed => Mathf.Max(0f, -Vector3.Dot(GetPlanarVelocity(body.velocity), DriveForward));
        public float ImpactSpeedSample => Mathf.Max(CurrentPlanarSpeed, previousPlanarSpeed);
        public Vector3 DriveForwardDirection => DriveForward;
        public float CurrentSpeedModifier => speedModifierMultiplier;

        private Vector3 PlanarForward => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        private Vector3 DriveForward => useNegativeZAsForward ? -PlanarForward : PlanarForward;
        private Vector3 DriveRight => Vector3.Cross(Vector3.up, PlanarForward);
        private float EffectiveAcceleration => acceleration * speedMultiplier * speedModifierMultiplier;
        private float EffectiveReverseAcceleration => reverseAcceleration * speedMultiplier * speedModifierMultiplier;
        private float EffectiveMaxSpeed => maxSpeed * speedMultiplier * speedModifierMultiplier;
        private float EffectiveMaxReverseSpeed => maxReverseSpeed * speedMultiplier * speedModifierMultiplier;
        private float SteeringSpeedDamping => Mathf.Lerp(1f, Mathf.Clamp01(highSpeedSteeringReduction),
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(CurrentPlanarSpeed / MaxDrivingSpeed)));

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            chassisCollider = GetComponent<BoxCollider>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
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
            // Steering and road pitch are applied explicitly with MoveRotation.
            // Static road contacts must not add a persistent steering impulse.
            body.constraints = impactRecoveryTimer > 0f
                ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
                : RigidbodyConstraints.FreezeRotation;

            if (externalReverseTimer > 0f)
            {
                externalReverseTimer -= Time.fixedDeltaTime;
            }

            UpdateSpeedModifier();

            bool grounded = IsGrounded();
            IsOnGround = grounded;
            float controlMultiplier = grounded ? 1f : airborneControlMultiplier;

            ApplyForwardAcceleration(controlMultiplier);
            ApplyRoadRotation(GetSteeringDegrees(controlMultiplier));
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
                steerInputVelocity = 0f;
                steeringYawRate = 0f;
            }
        }

        public void ReceiveImpact(Vector3 impulse, float shakeIntensity)
        {
            impactRecoveryTimer = impactRecoveryTime;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.AddForce(impulse, ForceMode.Impulse);
            Vector3 spinAxis = Vector3.Cross(Vector3.up, impulse.normalized);
            body.AddTorque(Vector3.up * Vector3.Dot(spinAxis, DriveRight) * impactSpinTorque, ForceMode.Impulse);
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

        private float GetSteeringDegrees(float controlMultiplier)
        {
            if (!ControlsEnabled)
            {
                smoothedSteerInput = 0f;
                steerInputVelocity = 0f;
                steeringYawRate = 0f;
                return 0f;
            }

            float targetInput = Mathf.Clamp(steerInput * Mathf.Max(0f, steerSensitivity), -1f, 1f);
            smoothedSteerInput = Mathf.SmoothDamp(smoothedSteerInput, targetInput, ref steerInputVelocity,
                Mathf.Max(0.01f, steerSmoothTime), Mathf.Infinity, Time.fixedDeltaTime);

            float drivingSpeed = Mathf.Max(CurrentForwardSpeed, CurrentReverseSpeed);
            if (drivingSpeed <= Mathf.Max(0.01f, minSpeedToTurn))
            {
                steeringYawRate = 0f;
                return 0f;
            }

            float authority = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minSpeedToTurn,
                Mathf.Max(minSpeedToTurn + 0.1f, steeringFullSpeed), drivingSpeed));
            authority = Mathf.Clamp01(authority * Mathf.Max(0f, turnSpeedFactor));
            float reverse = invertSteeringWhileReversing && CurrentReverseSpeed > CurrentForwardSpeed ? -1f : 1f;
            // Sine stays finite through the complete 0..90 degree steering range.
            float targetYaw = Mathf.Sin(SteeringAngle * Mathf.Deg2Rad) * Mathf.Max(0f, turnSpeed)
                * authority * SteeringSpeedDamping * controlMultiplier * reverse;
            bool returning = Mathf.Abs(targetYaw) < Mathf.Abs(steeringYawRate)
                || targetYaw * steeringYawRate < 0f;
            float accelerationLimit = returning ? steeringReturnAcceleration : steeringAcceleration;
            steeringYawRate = Mathf.MoveTowards(steeringYawRate, targetYaw,
                Mathf.Max(1f, accelerationLimit) * Time.fixedDeltaTime);
            return steeringYawRate * Time.fixedDeltaTime;
        }

        private void ApplyRoadRotation(float turnDegrees)
        {
            // Native rotation is locked on the road. Explicit MoveRotation
            // follows its slope and applies player steering once per step.
            Quaternion nextRotation = Quaternion.AngleAxis(turnDegrees, Vector3.up) * body.rotation;
            if (alignToGround && TryGetRoadNormal(out Vector3 roadNormal))
            {
                Vector3 heading = Vector3.ProjectOnPlane(nextRotation * Vector3.forward, Vector3.up).normalized;
                if (heading.sqrMagnitude > 0.001f)
                {
                    float blend = 1f - Mathf.Exp(-groundAlignmentSpeed * Time.fixedDeltaTime);
                    Vector3 up = Vector3.Slerp(nextRotation * Vector3.up, roadNormal, blend).normalized;
                    // Preserve the player's horizontal heading on cross-slopes.
                    Vector3 forward = heading - Vector3.up * (Vector3.Dot(heading, up) / Mathf.Max(0.01f, up.y));
                    nextRotation = Quaternion.LookRotation(forward.normalized, up);
                }
            }
            body.MoveRotation(nextRotation);
        }

        private bool TryGetRoadNormal(out Vector3 normal)
        {
            normal = Vector3.up;
            if (chassisCollider == null) return false;
            Vector3 center = chassisCollider.center;
            Vector3 size = chassisCollider.size;
            float closestGround = float.PositiveInfinity;
            int count = 0;
            Vector3 fallbackNormal = Vector3.zero;
            float minimumNormalY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = center + new Vector3((corner & 1) == 0 ? -size.x * 0.42f : size.x * 0.42f,
                    -size.y * 0.5f, (corner & 2) == 0 ? -size.z * 0.4f : size.z * 0.4f);
                Vector3 origin = transform.TransformPoint(local) + Vector3.up * groundProbeLift;
                int hits = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                    groundProbeLift + groundProbeDepth, groundMask, QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                RaycastHit support = default;
                for (int hit = 0; hit < hits; hit++)
                {
                    RaycastHit candidate = groundHits[hit];
                    if (candidate.collider.attachedRigidbody == body || candidate.normal.y < minimumNormalY || candidate.distance >= nearest)
                        continue;
                    support = candidate;
                    nearest = candidate.distance;
                }
                if (float.IsPositiveInfinity(nearest)) continue;
                groundPoints[count++] = support.point;
                fallbackNormal += support.normal;
                closestGround = Mathf.Min(closestGround, nearest - groundProbeLift);
            }

            // Distant scenery below an airborne car must not dictate its pose.
            if (count < 3 || closestGround > groundCheckDistance + 0.25f) return false;
            Vector3 fitted;
            if (count == 4)
            {
                Vector3 frontBack = groundPoints[2] + groundPoints[3] - groundPoints[0] - groundPoints[1];
                Vector3 rightLeft = groundPoints[1] + groundPoints[3] - groundPoints[0] - groundPoints[2];
                fitted = Vector3.Cross(frontBack, rightLeft);
            }
            else
            {
                fitted = Vector3.Cross(groundPoints[1] - groundPoints[0], groundPoints[2] - groundPoints[0]);
            }
            if (fitted.sqrMagnitude < 0.001f) fitted = fallbackNormal;
            if (fitted.y < 0f) fitted = -fitted;
            normal = Vector3.RotateTowards(Vector3.up, fitted.normalized, maxGroundAngle * Mathf.Deg2Rad, 0f);
            return true;
        }

        private void ApplyLateralGrip(float controlMultiplier)
        {
            Vector3 planarVelocity = GetPlanarVelocity(body.velocity);
            Vector3 lateralVelocity = Vector3.Project(planarVelocity, DriveRight);
            float grip = (lateralGrip + Mathf.Abs(smoothedSteerInput) * Mathf.Max(0f, turningGripBoost)) * controlMultiplier;
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
            Vector3 right = DriveRight;
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
            Vector3 origin = (groundCheckOrigin != null ? groundCheckOrigin.position : transform.position) + Vector3.up * 0.08f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (groundHits[i].collider.attachedRigidbody != body && groundHits[i].normal.y > 0.3f)
                {
                    return true;
                }
            }
            return false;
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
