// Kwinkit-VS / Core / Character.cs
// Add to each character. Only the selected Is Player character receives input.
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Kwinkit.VS
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Kwinkit-VS/Character")]
    public class Character : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Enable on exactly one active character. Other characters are NPCs.")]
        public bool isPlayer;

        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 4.5f;
        [Min(0f)] public float turnSpeed = 12f;
        [Min(0f)] public float jumpHeight = 1.2f;
        [Tooltip("Gravity is negative (e.g. -20).")]
        public float gravity = -20f;

        [Header("Optional Animation")]
        [Tooltip("Uses the Animator's Speed (float) and Grounded (bool) parameters if present.")]
        public Animator animator;
        [Min(0f)] public float animationSmoothTime = 0.12f;

        static readonly List<Character> active = new List<Character>();
        static bool warnedMultiple;
        CharacterController controller;
        float verticalVelocity;
        float animatedSpeed;
        float speedVelocity;
        bool ownsController;
        bool hasSpeedParameter;
        bool hasGroundedParameter;
        bool hasJumpParameter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCharacters()
        {
            active.Clear();
            warnedMultiple = false;
        }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (controller == null && isPlayer)
            {
                controller = gameObject.AddComponent<CharacterController>();
                ownsController = true;
                // Standard human-sized collider; customize on the component for a specific avatar.
                controller.height = 1.65f;
                controller.radius = 0.25f;
                controller.center = new Vector3(0f, 0.825f, 0f);
                controller.stepOffset = 0.3f;
            }
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();
        }

        void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        void OnDisable()
        {
            active.Remove(this);
            verticalVelocity = 0f;
        }

        void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            turnSpeed = Mathf.Max(0f, turnSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Min(-0.01f, gravity);
        }

        public static Character Player
        {
            get
            {
                Character found = null;
                foreach (var character in active)
                {
                    if (character == null || !character.isPlayer || !character.isActiveAndEnabled) continue;
                    if (found == null) found = character;
                    else if (!warnedMultiple)
                    {
                        warnedMultiple = true;
                        Debug.LogWarning("[Kwinkit-VS] Multiple active Characters have Is Player checked. Only the first can be controlled.");
                    }
                }
                return found;
            }
        }

        void Update()
        {
            if (!isPlayer || Player != this) return;
            if (controller == null)
            {
                // Supports setting Is Player at runtime after Awake.
                controller = GetComponent<CharacterController>();
                if (controller == null)
                {
                    controller = gameObject.AddComponent<CharacterController>();
                    ownsController = true;
                    controller.height = 1.65f;
                    controller.radius = 0.25f;
                    controller.center = new Vector3(0f, 0.825f, 0f);
                }
            }
            if (!controller.enabled) return;

            Vector2 input = ReadMovement();
            Vector3 direction = CameraDirection(input);
            bool grounded = controller.isGrounded;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            bool jumped = grounded && JumpPressed();
            if (jumped) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * Mathf.Min(gravity, -0.01f));
            verticalVelocity += gravity * Time.deltaTime;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            }

            Vector3 velocity = direction * moveSpeed;
            velocity.y = verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            if (animator != null)
            {
                float targetSpeed = input.magnitude;
                animatedSpeed = Mathf.SmoothDamp(animatedSpeed, targetSpeed, ref speedVelocity, Mathf.Max(0.001f, animationSmoothTime));
                if (hasSpeedParameter) animator.SetFloat("Speed", animatedSpeed);
                if (hasGroundedParameter) animator.SetBool("Grounded", controller.isGrounded);
                if (jumped && hasJumpParameter) animator.SetTrigger("Jump");
            }
        }

        static Vector2 ReadMovement()
        {
            float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            }
            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                if (stick.sqrMagnitude > x*x + y*y) return Vector2.ClampMagnitude(stick, 1f);
            }
#else
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
#endif
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        static bool JumpPressed()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        static Vector3 CameraDirection(Vector2 input)
        {
            Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;
            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        }

        void CacheAnimatorParameters()
        {
            if (animator == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) hasSpeedParameter = true;
                if (p.name == "Grounded" && p.type == AnimatorControllerParameterType.Bool) hasGroundedParameter = true;
                if (p.name == "Jump" && p.type == AnimatorControllerParameterType.Trigger) hasJumpParameter = true;
            }
        }
    }
}
