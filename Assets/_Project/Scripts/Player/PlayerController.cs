using Darclite.CameraSystem;
using Darclite.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.Player
{
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Darclite/Third Person Player Controller")]
    public class PlayerController : MonoBehaviour
    {
        private static readonly int MoveXParam = Animator.StringToHash("MoveX");
        private static readonly int MoveYParam = Animator.StringToHash("MoveY");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int JumpParam = Animator.StringToHash("Jump");
        private static readonly int DodgeForwardParam = Animator.StringToHash("DodgeForward");
        private static readonly int DodgeBackParam = Animator.StringToHash("DodgeBack");
        private static readonly int DodgeLeftParam = Animator.StringToHash("DodgeLeft");
        private static readonly int DodgeRightParam = Animator.StringToHash("DodgeRight");
        private static readonly int IsDodgingParam = Animator.StringToHash("IsDodging");
        private static readonly int IsMovingParam = Animator.StringToHash("IsMoving");

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float rotationSpeed = 12f;

        [Header("Gravity & Jump")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float jumpHeight = 1.5f;
        [SerializeField] private float groundedStickForce = -2f;
        [SerializeField] private float jumpAnticipationDelay = 0.15f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private float animatorSpeedDamping = 0.15f;
        [SerializeField] private float diagonalRunAnimationSpeedMultiplier = 2f;

        [Header("Hit Reaction")]
        [SerializeField, Range(0f, 1f)] private float hitStunMoveSpeedMultiplier = 0.15f;

        [Header("Dodge")]
        [SerializeField] private float doubleTapWindow = 0.3f;
        [SerializeField] private float dodgeSpeed = 54f;
        [SerializeField] private float dodgeDuration = 0.3f;
        [SerializeField] private float dodgeCooldown = 2f;
        [SerializeField, Range(0.05f, 0.9f)] private float dodgeAccelerationFraction = 0.25f;

        [Header("Dodge Feel")]
        [SerializeField] private float smearStretchAmount = 0.35f;
        [SerializeField] private float smearSquashAmount = 0.15f;
        [SerializeField] private float cameraShakeDuration = 0.15f;
        [SerializeField] private float cameraShakeMagnitude = 0.15f;
        [SerializeField] private float ghostSpawnInterval = 0.04f;
        [SerializeField] private float ghostLifetime = 0.25f;
        [SerializeField] private Color ghostColor = new Color(0.6f, 0.85f, 1f, 0.35f);

        private CharacterController _controller;
        private UnityEngine.Camera _mainCamera;
        private ThirdPersonOrbitCamera _orbitCamera;
        private Vector3 _verticalVelocity;
        private bool _isPreparingJump;
        private float _jumpAnticipationTimer;

        private bool _isDodging;
        private float _dodgeTimer;
        private Vector3 _dodgeDirection;
        private bool _dodgeStretchIsForwardAxis;
        private float _ghostSpawnTimer;
        private Vector3 _modelBaseScale = Vector3.one;
        private float _lastDodgeStartTime = -999f;
        private float _lastWTapTime = -999f;
        private float _lastATapTime = -999f;
        private float _lastSTapTime = -999f;
        private float _lastDTapTime = -999f;

        private Combatant _combatant;
        private PlayerCombat _playerCombat;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _combatant = GetComponent<Combatant>();
            _playerCombat = GetComponent<PlayerCombat>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator != null)
            {
                _modelBaseScale = animator.transform.localScale;
            }
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = UnityEngine.Camera.main;
                _orbitCamera = _mainCamera != null ? _mainCamera.GetComponent<ThirdPersonOrbitCamera>() : null;
            }

            bool isKnockedBack = _combatant != null && _combatant.IsBeingKnockedBack;
            bool isStunned = !isKnockedBack && _combatant != null && _combatant.IsStunned;
            bool isSelfAttacking = _playerCombat != null && _playerCombat.IsAttacking;
            bool canInitiateJump = !isKnockedBack && !isStunned && !isSelfAttacking;

            ApplyGravityAndJump(canInitiateJump);

            if (isKnockedBack)
            {
                // Combatant's knockback coroutine has exclusive control of the CharacterController.
                UpdateAnimator(Vector2.zero, false);
                return;
            }

            if (isSelfAttacking)
            {
                _controller.Move(new Vector3(0f, _verticalVelocity.y, 0f) * Time.deltaTime);
                UpdateAnimator(Vector2.zero, false);
                return;
            }

            if (isStunned)
            {
                // Can't attack or dodge while reeling from a hit, but still crawl at a
                // fraction of normal speed instead of being fully frozen in place.
                Vector2 stunnedMoveInput = ReadMoveInput();
                Vector3 stunnedMoveDirection = CalculateCameraRelativeDirection(stunnedMoveInput);
                Vector3 stunnedVelocity = stunnedMoveDirection * walkSpeed * hitStunMoveSpeedMultiplier;
                stunnedVelocity.y = _verticalVelocity.y;
                _controller.Move(stunnedVelocity * Time.deltaTime);
                RotateTowardsCamera();
                UpdateAnimator(Vector2.zero, false);
                return;
            }

            CheckDodgeInput();

            Vector2 moveInput = ReadMoveInput();
            Vector3 moveDirection = CalculateCameraRelativeDirection(moveInput);
            bool isSprinting = IsSprintHeld();

            if (_isDodging)
            {
                UpdateDodge();
            }
            else
            {
                Move(moveDirection, isSprinting);
            }

            RotateTowardsCamera();
            UpdateAnimator(moveInput, isSprinting);
        }

        private void CheckDodgeInput()
        {
            if (_isDodging || !_controller.isGrounded || _isPreparingJump)
            {
                return;
            }

            if (Time.time - _lastDodgeStartTime < dodgeCooldown)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.wKey.wasPressedThisFrame) TryStartDodge(ref _lastWTapTime, new Vector2(0f, 1f), DodgeForwardParam);
            else if (keyboard.sKey.wasPressedThisFrame) TryStartDodge(ref _lastSTapTime, new Vector2(0f, -1f), DodgeBackParam);
            else if (keyboard.aKey.wasPressedThisFrame) TryStartDodge(ref _lastATapTime, new Vector2(-1f, 0f), DodgeLeftParam);
            else if (keyboard.dKey.wasPressedThisFrame) TryStartDodge(ref _lastDTapTime, new Vector2(1f, 0f), DodgeRightParam);
        }

        private void TryStartDodge(ref float lastTapTime, Vector2 axis, int triggerParam)
        {
            float now = Time.time;
            if (now - lastTapTime <= doubleTapWindow)
            {
                _isDodging = true;
                _dodgeTimer = 0f;
                _lastDodgeStartTime = now;
                _dodgeDirection = CalculateCameraRelativeDirection(axis);
                _dodgeStretchIsForwardAxis = Mathf.Abs(axis.y) >= Mathf.Abs(axis.x);
                _ghostSpawnTimer = 0f;

                if (animator != null)
                {
                    animator.SetTrigger(triggerParam);
                }

                if (_orbitCamera != null)
                {
                    _orbitCamera.Shake(cameraShakeDuration, cameraShakeMagnitude);
                }

                lastTapTime = -999f;
            }
            else
            {
                lastTapTime = now;
            }
        }

        private void UpdateDodge()
        {
            _dodgeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_dodgeTimer / dodgeDuration);
            float speedMultiplier = EvaluateDodgeSpeedCurve(t);

            Vector3 velocity = _dodgeDirection * dodgeSpeed * speedMultiplier;
            velocity.y = _verticalVelocity.y;
            _controller.Move(velocity * Time.deltaTime);

            ApplySmear(speedMultiplier);
            UpdateGhostTrail();

            if (_dodgeTimer >= dodgeDuration)
            {
                _isDodging = false;
                ResetSmear();
            }
        }

        private void ApplySmear(float speedMultiplier)
        {
            if (animator == null)
            {
                return;
            }

            float stretch = 1f + smearStretchAmount * speedMultiplier;
            float squash = 1f - smearSquashAmount * speedMultiplier;

            Vector3 factor = _dodgeStretchIsForwardAxis
                ? new Vector3(squash, squash, stretch)
                : new Vector3(stretch, squash, squash);

            animator.transform.localScale = Vector3.Scale(_modelBaseScale, factor);
        }

        private void ResetSmear()
        {
            if (animator != null)
            {
                animator.transform.localScale = _modelBaseScale;
            }
        }

        private void UpdateGhostTrail()
        {
            if (animator == null)
            {
                return;
            }

            _ghostSpawnTimer -= Time.deltaTime;
            if (_ghostSpawnTimer > 0f)
            {
                return;
            }

            _ghostSpawnTimer = ghostSpawnInterval;
            DashGhostSpawner.Spawn(animator.gameObject, ghostColor, ghostLifetime);
        }

        private float EvaluateDodgeSpeedCurve(float t)
        {
            if (t < dodgeAccelerationFraction)
            {
                float rampT = t / dodgeAccelerationFraction;
                return Mathf.SmoothStep(0f, 1f, rampT);
            }

            float decayT = (t - dodgeAccelerationFraction) / (1f - dodgeAccelerationFraction);
            return Mathf.SmoothStep(1f, 0f, decayT);
        }

        private Vector2 ReadMoveInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;

            return Vector2.ClampMagnitude(input, 1f);
        }

        private static bool IsSprintHeld()
        {
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        }

        private Vector3 CalculateCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 forward = _mainCamera != null ? _mainCamera.transform.forward : Vector3.forward;
            Vector3 right = _mainCamera != null ? _mainCamera.transform.right : Vector3.right;

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            return (forward * input.y + right * input.x).normalized;
        }

        private void Move(Vector3 moveDirection, bool isSprinting)
        {
            float speed = isSprinting ? runSpeed : walkSpeed;
            Vector3 velocity = moveDirection * speed;
            velocity.y = _verticalVelocity.y;
            _controller.Move(velocity * Time.deltaTime);
        }

        private void RotateTowardsCamera()
        {
            if (_mainCamera == null)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.Euler(0f, _mainCamera.transform.eulerAngles.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        private void ApplyGravityAndJump(bool canInitiateJump)
        {
            if (_isPreparingJump)
            {
                if (!canInitiateJump)
                {
                    // Getting hit or throwing a punch cancels a jump that hasn't launched yet,
                    // so it can't fire later mid-attack/mid-hit-reaction.
                    _isPreparingJump = false;
                }
                else
                {
                    _verticalVelocity.y = groundedStickForce;
                    _jumpAnticipationTimer += Time.deltaTime;

                    if (_jumpAnticipationTimer >= jumpAnticipationDelay)
                    {
                        _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                        _isPreparingJump = false;
                    }

                    return;
                }
            }

            if (_controller.isGrounded)
            {
                _verticalVelocity.y = groundedStickForce;

                if (canInitiateJump && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    _isPreparingJump = true;
                    _jumpAnticipationTimer = 0f;

                    if (animator != null)
                    {
                        animator.SetTrigger(JumpParam);
                    }
                }
            }
            else
            {
                _verticalVelocity.y += gravity * Time.deltaTime;
            }
        }

        private void UpdateAnimator(Vector2 moveInput, bool isSprinting)
        {
            if (animator == null)
            {
                return;
            }

            float speedMultiplier = isSprinting ? 2f : 1f;
            Vector2 blendInput = moveInput * speedMultiplier;

            animator.SetFloat(MoveXParam, blendInput.x, animatorSpeedDamping, Time.deltaTime);
            animator.SetFloat(MoveYParam, blendInput.y, animatorSpeedDamping, Time.deltaTime);
            animator.SetBool(GroundedParam, _controller.isGrounded);
            animator.SetBool(IsDodgingParam, _isDodging);
            animator.SetBool(IsMovingParam, moveInput.sqrMagnitude > 0.01f);

            bool isDiagonal = Mathf.Abs(moveInput.x) > 0.01f && Mathf.Abs(moveInput.y) > 0.01f;
            bool isDiagonalRunBlend = isSprinting && isDiagonal && _controller.isGrounded && !_isPreparingJump && !_isDodging;
            animator.speed = isDiagonalRunBlend ? diagonalRunAnimationSpeedMultiplier : 1f;
        }
    }
}
