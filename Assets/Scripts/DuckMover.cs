using UnityEngine;
using UnityEngine.InputSystem;

public class DuckMover : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float runSpeed = 3f;
    [SerializeField] private float jumpSpeed = 5.5f;
    [SerializeField] private bool cameraRelativeMovement = true;
    [SerializeField] private string idleStateName = "idle";
    [SerializeField] private string walkingStateName = "Walking";
    [SerializeField] private string runningStateName = "Running";
    [SerializeField] private string jumpStateName = "jump";

    private Camera mainCamera;
    private Animator animator;
    private Rigidbody body;
    private int idleStateHash;
    private int walkingStateHash;
    private int runningStateHash;
    private int jumpStateHash;
    private int currentStateHash;

    private bool isGrounded = true;
    private bool jumpQueued;
    private bool wantsToRun;
    private bool hasMoveInput;
    private Vector3 desiredMoveDirection;

    private void Awake()
    {
        mainCamera = Camera.main;
        animator = GetComponent<Animator>();
        body = GetComponent<Rigidbody>();

        ConfigurePhysics();
        CacheAnimatorStates();

        if (animator != null)
        {
            animator.applyRootMotion = false;
            PlayState(GetGroundedState());
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        Vector2 input = ReadMoveInput(keyboard);
        hasMoveInput = input.sqrMagnitude > 0.001f;
        wantsToRun = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        desiredMoveDirection = hasMoveInput ? GetMoveDirection(input.normalized) : Vector3.zero;

        if (keyboard.spaceKey.wasPressedThisFrame && isGrounded)
        {
            jumpQueued = true;
        }

        PlayState(GetAnimationState());
    }

    private void FixedUpdate()
    {
        if (body == null)
        {
            return;
        }

        float speed = wantsToRun && HasState(runningStateHash) ? runSpeed : walkSpeed;
        Vector3 horizontalVelocity = desiredMoveDirection * speed;
        Vector3 velocity = body.linearVelocity;
        velocity.x = horizontalVelocity.x;
        velocity.z = horizontalVelocity.z;

        if (jumpQueued && isGrounded)
        {
            velocity.y = jumpSpeed;
            isGrounded = false;
        }

        jumpQueued = false;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;

        if (hasMoveInput)
        {
            FaceDirection(desiredMoveDirection);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.up) > 0.55f)
            {
                isGrounded = true;
                return;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        isGrounded = false;
    }

    private void ConfigurePhysics()
    {
        if (body == null)
        {
            return;
        }

        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void CacheAnimatorStates()
    {
        idleStateHash = Animator.StringToHash(idleStateName);
        walkingStateHash = Animator.StringToHash(walkingStateName);
        runningStateHash = Animator.StringToHash(runningStateName);
        jumpStateHash = Animator.StringToHash(jumpStateName);
    }

    private int GetAnimationState()
    {
        if (!isGrounded && HasState(jumpStateHash))
        {
            return jumpStateHash;
        }

        return GetGroundedState();
    }

    private int GetGroundedState()
    {
        if (!hasMoveInput)
        {
            return HasState(idleStateHash) ? idleStateHash : 0;
        }

        if (wantsToRun && HasState(runningStateHash))
        {
            return runningStateHash;
        }

        return HasState(walkingStateHash) ? walkingStateHash : HasState(idleStateHash) ? idleStateHash : 0;
    }

    private static Vector2 ReadMoveInput(Keyboard keyboard)
    {
        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            input.y += 1f;
        }

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            input.y -= 1f;
        }

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            input.x += 1f;
        }

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            input.x -= 1f;
        }

        return input;
    }

    private Vector3 GetMoveDirection(Vector2 input)
    {
        if (!cameraRelativeMovement || mainCamera == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = mainCamera.transform.forward;
        Vector3 right = mainCamera.transform.right;
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        return (forward * input.y + right * input.x).normalized;
    }

    private void FaceDirection(Vector3 direction)
    {
        Vector3 flatDirection = FlattenDirection(direction);
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        body.rotation = Quaternion.LookRotation(flatDirection, Vector3.up);
    }

    private void PlayState(int stateHash)
    {
        if (animator == null || stateHash == 0)
        {
            return;
        }

        if (currentStateHash == stateHash)
        {
            KeepLooping(stateHash);
            return;
        }

        currentStateHash = stateHash;
        animator.CrossFadeInFixedTime(stateHash, 0.08f);
    }

    private void KeepLooping(int stateHash)
    {
        if (animator.IsInTransition(0) || stateHash == jumpStateHash)
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == stateHash && !state.loop && state.normalizedTime >= 0.98f)
        {
            animator.Play(stateHash, 0, 0f);
        }
    }

    private bool HasState(int stateHash)
    {
        return animator != null && animator.HasState(0, stateHash);
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
    }
}
