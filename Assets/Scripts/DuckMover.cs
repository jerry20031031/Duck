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
    [SerializeField] private string pickupWeaponName = "weapon1";
    [SerializeField] private float weaponPickupDistance = 1.25f;
    [SerializeField] private Vector3 heldWeaponLocalPosition = new Vector3(0.08f, 0.03f, 0.02f);
    [SerializeField] private Vector3 heldWeaponLocalScale = Vector3.one;

    private const string SpeedParameterName = "Speed";
    private const string GroundedParameterName = "Grounded";
    private const string JumpParameterName = "Jump";
    private static readonly Vector3 HeldWeaponLocalEulerAngles = new Vector3(-90f, 180f, -90f);

    private Camera mainCamera;
    private Animator animator;
    private Rigidbody body;
    private int idleStateHash;
    private int walkingStateHash;
    private int runningStateHash;
    private int jumpStateHash;
    private int speedParameterHash;
    private int groundedParameterHash;
    private int jumpParameterHash;
    private int currentStateHash;

    private bool isGrounded = true;
    private bool jumpQueued;
    private bool wantsToRun;
    private bool hasMoveInput;
    private bool hasWeapon;
    private Vector3 desiredMoveDirection;
    private Transform pickupWeapon;
    private Transform heldWeapon;
    private Transform rightHand;
    private Vector3 heldWeaponWorldScale = Vector3.one;
    private Collider[] pickupWeaponColliders = System.Array.Empty<Collider>();
    private Rigidbody pickupWeaponBody;

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
            SyncAnimatorParameters();
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
            SetJumpTrigger();
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            TryPickupWeapon();
        }

        SyncAnimatorParameters();
        PlayState(GetAnimationState());
    }

    private void LateUpdate()
    {
        if (hasWeapon)
        {
            KeepWeaponInRightHand();
        }
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
            SyncAnimatorParameters();
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
                SyncAnimatorParameters();
                return;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        isGrounded = false;
        SyncAnimatorParameters();
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
        speedParameterHash = Animator.StringToHash(SpeedParameterName);
        groundedParameterHash = Animator.StringToHash(GroundedParameterName);
        jumpParameterHash = Animator.StringToHash(JumpParameterName);
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
        Camera movementCamera = GetMovementCamera();
        if (!cameraRelativeMovement || movementCamera == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = movementCamera.transform.forward;
        Vector3 right = movementCamera.transform.right;
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        return (forward * input.y + right * input.x).normalized;
    }

    private Camera GetMovementCamera()
    {
        if (mainCamera == null || !mainCamera.isActiveAndEnabled)
        {
            mainCamera = Camera.main;
        }

        return mainCamera;
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

    private void TryPickupWeapon()
    {
        if (hasWeapon)
        {
            return;
        }

        Transform weapon = GetPickupWeapon();
        if (weapon == null || IsHeldByAnotherDuck(weapon) || !IsWeaponCloseEnough(weapon))
        {
            return;
        }

        rightHand = GetRightHand();
        if (rightHand == null)
        {
            return;
        }

        pickupWeaponBody = weapon.GetComponent<Rigidbody>();
        if (pickupWeaponBody != null)
        {
            pickupWeaponBody.isKinematic = true;
            pickupWeaponBody.detectCollisions = false;
        }

        pickupWeaponColliders = weapon.GetComponentsInChildren<Collider>();
        foreach (Collider weaponCollider in pickupWeaponColliders)
        {
            weaponCollider.enabled = false;
        }

        heldWeapon = weapon;
        heldWeaponWorldScale = Vector3.Scale(weapon.lossyScale, heldWeaponLocalScale);
        hasWeapon = true;
        wantsToRun = false;
        SetWeaponVisible(heldWeapon, true);
        KeepWeaponInRightHand();
        PlayState(GetAnimationState());
    }

    private Transform GetRightHand()
    {
        if (rightHand != null)
        {
            return rightHand;
        }

        rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        return rightHand;
    }

    private void KeepWeaponInRightHand()
    {
        if (heldWeapon == null)
        {
            return;
        }

        Transform hand = GetRightHand();
        if (hand == null)
        {
            return;
        }

        if (heldWeapon.parent != hand)
        {
            heldWeapon.SetParent(hand, false);
        }

        heldWeapon.localPosition = heldWeaponLocalPosition;
        heldWeapon.localRotation = Quaternion.Euler(HeldWeaponLocalEulerAngles);
        SetWorldScale(heldWeapon, heldWeaponWorldScale);
    }

    private static void SetWeaponVisible(Transform weapon, bool isVisible)
    {
        if (weapon == null)
        {
            return;
        }

        weapon.gameObject.SetActive(isVisible);
        foreach (Renderer weaponRenderer in weapon.GetComponentsInChildren<Renderer>(true))
        {
            weaponRenderer.enabled = isVisible;
        }
    }

    private Transform GetPickupWeapon()
    {
        if (pickupWeapon != null)
        {
            return pickupWeapon;
        }

        GameObject weaponObject = GameObject.Find(pickupWeaponName);
        pickupWeapon = weaponObject != null ? weaponObject.transform : null;
        return pickupWeapon;
    }

    private bool IsWeaponCloseEnough(Transform weapon)
    {
        Collider closestCollider = GetClosestWeaponCollider(weapon);
        if (closestCollider == null)
        {
            return Vector3.Distance(transform.position, weapon.position) <= weaponPickupDistance;
        }

        Vector3 closestPoint = GetClosestPoint(closestCollider);
        return Vector3.Distance(transform.position, closestPoint) <= weaponPickupDistance;
    }

    private Collider GetClosestWeaponCollider(Transform weapon)
    {
        Collider[] weaponColliders = weapon.GetComponentsInChildren<Collider>();
        Collider closestCollider = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Collider weaponCollider in weaponColliders)
        {
            if (!weaponCollider.enabled)
            {
                continue;
            }

            Vector3 closestPoint = GetClosestPoint(weaponCollider);
            float distance = (transform.position - closestPoint).sqrMagnitude;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestCollider = weaponCollider;
        }

        return closestCollider;
    }

    private Vector3 GetClosestPoint(Collider weaponCollider)
    {
        return weaponCollider.bounds.ClosestPoint(transform.position);
    }

    private static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        Transform parent = target.parent;
        if (parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        target.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
    }

    private bool IsHeldByAnotherDuck(Transform weapon)
    {
        DuckMover holder = weapon.GetComponentInParent<DuckMover>();
        return holder != null && holder != this;
    }

    private void SyncAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat(speedParameterHash, GetAnimatorSpeed());
        animator.SetBool(groundedParameterHash, isGrounded);
    }

    private float GetAnimatorSpeed()
    {
        if (!hasMoveInput)
        {
            return 0f;
        }

        return wantsToRun ? 1f : 0.5f;
    }

    private void SetJumpTrigger()
    {
        if (animator != null)
        {
            animator.SetTrigger(jumpParameterHash);
        }
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
    }
}
