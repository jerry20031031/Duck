using UnityEngine;
using UnityEngine.InputSystem;

public class DuckMover : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float jumpSpeed = 10f;
    [SerializeField] private bool cameraRelativeMovement = true;
    [SerializeField] private string idleStateName = "idle";
    [SerializeField] private string walkingStateName = "Walking";
    [SerializeField] private string runningStateName = "Running";
    [SerializeField] private string jumpStateName = "jump";
    [SerializeField] private string wandAttackStateName = "stand attack";
    [SerializeField, Min(0.05f)] private float wandAttackAnimationDuration = DuckWandAttack.MinimumWandAttackWindup;
    [SerializeField] private string pickupWeaponName = "weapon1";
    [SerializeField] private float weaponPickupDistance = 1.25f;
    [SerializeField] private Vector3 heldWeaponLocalPosition = new Vector3(0.08f, 0.03f, 0.02f);
    [SerializeField] private Vector3 heldWeaponLocalScale = Vector3.one;
    [SerializeField, Min(0f)] private float groundProbeDistance = 0.12f;
    [SerializeField, Min(0f)] private float gravityAcceleration = 24f;
    [SerializeField, Min(0f)] private float groundedStickSpeed = 1f;

    private const string SpeedParameterName = "Speed";
    private const string GroundedParameterName = "Grounded";
    private const string JumpParameterName = "Jump";
    private static readonly Vector3 HeldWeaponLocalEulerAngles = new Vector3(-90f, 180f, -90f);

    private Camera mainCamera;
    private Animator animator;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private int idleStateHash;
    private int walkingStateHash;
    private int runningStateHash;
    private int jumpStateHash;
    private int wandAttackStateHash;
    private int speedParameterHash;
    private int groundedParameterHash;
    private int jumpParameterHash;
    private int currentStateHash;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private bool isGrounded;
    private bool jumpQueued;
    private bool wantsToRun;
    private bool hasMoveInput;
    private bool hasWeapon;
    private float verticalSpeed;
    private Vector3 desiredMoveDirection;
    private Transform pickupWeapon;
    private Transform heldWeapon;
    private Transform rightHand;
    private Vector3 heldWeaponWorldScale = Vector3.one;
    private Collider[] pickupWeaponColliders = System.Array.Empty<Collider>();
    private Rigidbody pickupWeaponBody;
    private Unit1WandPickup networkWand;
    private float wandAttackUntil;

    public float NetworkAnimationSpeed => GetAnimatorSpeed();
    public bool NetworkAnimationGrounded => isGrounded;
    public int NetworkAnimationStateHash => GetAnimationState();
    /// <summary>True after this duck has picked up a wand.</summary>
    public bool HasWeapon => hasWeapon;
    /// <summary>The synchronized UNIT1 wand in this duck's hand, if any.</summary>
    public Unit1WandPickup HeldNetworkWand => networkWand;
    public bool IsPlayingWandAttack => Time.time < wandAttackUntil;

    private void Awake()
    {
        RefreshCharacterReferences();
    }

    public void RefreshCharacterReferences()
    {
        mainCamera = Camera.main;
        animator = GetComponentInChildren<Animator>();
        animator ??= GetComponentInChildren<Animator>(true);
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        ConfigurePhysics();
        CacheAnimatorStates();
        currentStateHash = 0;

        if (animator != null)
        {
            animator.applyRootMotion = false;
            SyncAnimatorParameters();
            PlayState(GetGroundedState());
        }
    }

    private void Update()
    {
        if (SceneSettingsOverlay.IsOpen)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        // Keep a one-frame jump press until Fusion's next simulation tick.
        // Continuous keyboard movement is intentionally read in
        // SimulateNetworkMovement(), where the networked Rigidbody is written.
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpQueued = true;
        }

        if (keyboard.eKey.wasPressedThisFrame)
        {
            TryPickupWeapon();
        }
    }

    private void LateUpdate()
    {
        if (hasWeapon)
        {
            KeepWeaponInRightHand();
        }
    }

    /// <summary>
    /// Advances this duck from FusionDuckPlayer.FixedUpdateNetwork. It is a
    /// collision-aware kinematic motor: NetworkTransform publishes the direct
    /// position change, rather than relying on Unity to integrate velocity after
    /// Fusion has captured its network state.
    /// </summary>
    public void SimulateNetworkMovement(float tickDeltaTime)
    {
        if (body == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        bool settingsOpen = SceneSettingsOverlay.IsOpen;
        Vector2 input = !settingsOpen && keyboard != null ? ReadMoveInput(keyboard) : Vector2.zero;
        hasMoveInput = input.sqrMagnitude > 0.001f;
        wantsToRun = !settingsOpen && keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        desiredMoveDirection = hasMoveInput ? GetMoveDirection(input.normalized) : Vector3.zero;

        // The new clip is deliberately a standing cast. Keep its silhouette
        // readable by pausing movement for the brief animation window.
        if (IsPlayingWandAttack)
        {
            hasMoveInput = false;
            wantsToRun = false;
            desiredMoveDirection = Vector3.zero;
        }

        UpdateGroundedState();

        float speed = wantsToRun && HasState(runningStateHash) ? runSpeed : walkSpeed;
        DuckKinematicMotor.Move(body, desiredMoveDirection * speed * tickDeltaTime);

        if (!settingsOpen && jumpQueued && isGrounded)
        {
            verticalSpeed = jumpSpeed;
            isGrounded = false;
            SetJumpTrigger();
        }

        jumpQueued = false;
        if (!isGrounded)
        {
            verticalSpeed -= gravityAcceleration * tickDeltaTime;
        }
        else if (verticalSpeed < 0f)
        {
            verticalSpeed = -groundedStickSpeed;
        }

        bool blockedVertically = DuckKinematicMotor.Move(body, Vector3.up * verticalSpeed * tickDeltaTime);
        if (blockedVertically)
        {
            verticalSpeed = 0f;
        }

        // Player and AI ducks use a kinematic collision motor. Unity reports
        // an error every tick if angularVelocity is assigned on such a body.
        if (!body.isKinematic)
        {
            body.angularVelocity = Vector3.zero;
        }

        if (hasMoveInput)
        {
            FaceDirection(desiredMoveDirection);
        }

        UpdateGroundedState();
        SyncAnimatorParameters();
        PlayState(GetAnimationState());
    }

    private void UpdateGroundedState()
    {
        bool wasGrounded = isGrounded;
        isGrounded = IsGroundBelowFeet();
        if (wasGrounded != isGrounded)
        {
            SyncAnimatorParameters();
        }
    }

    private bool IsGroundBelowFeet()
    {
        if (body == null || capsule == null || verticalSpeed > 0.05f)
        {
            return false;
        }

        float probeRadius = Mathf.Max(0.02f, capsule.radius * 0.9f);
        float lowerSphereCenterY = capsule.center.y - (capsule.height * 0.5f - capsule.radius);
        Vector3 origin = transform.TransformPoint(new Vector3(capsule.center.x, lowerSphereCenterY, capsule.center.z));
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            probeRadius,
            Vector3.down,
            groundHits,
            groundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = groundHits[i].collider;
            if (hitCollider == null || hitCollider == capsule || hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (Vector3.Dot(groundHits[i].normal, Vector3.up) > 0.55f)
            {
                return true;
            }
        }

        return false;
    }

    private void ConfigurePhysics()
    {
        if (body == null)
        {
            return;
        }

        // Position is deliberately advanced by DuckKinematicMotor during a
        // Fusion tick. Unity physics must not integrate a second motion path.
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
        }

        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void CacheAnimatorStates()
    {
        idleStateHash = Animator.StringToHash(idleStateName);
        walkingStateHash = Animator.StringToHash(walkingStateName);
        runningStateHash = Animator.StringToHash(runningStateName);
        jumpStateHash = Animator.StringToHash(jumpStateName);
        wandAttackStateHash = Animator.StringToHash(wandAttackStateName);
        speedParameterHash = Animator.StringToHash(SpeedParameterName);
        groundedParameterHash = Animator.StringToHash(GroundedParameterName);
        jumpParameterHash = Animator.StringToHash(JumpParameterName);
    }

    private int GetAnimationState()
    {
        if (IsPlayingWandAttack && HasState(wandAttackStateHash))
        {
            return wandAttackStateHash;
        }

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

        Vector3 forward = Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up);
        // A top-down camera has no horizontal forward vector.  In that case
        // WASD must still work rather than turning W/S into a zero movement.
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

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
            if (stateHash == wandAttackStateHash && IsPlayingWandAttack)
            {
                return;
            }

            KeepLooping(stateHash);
            return;
        }

        currentStateHash = stateHash;
        animator.CrossFadeInFixedTime(stateHash, 0.08f);
    }

    /// <summary>Plays the controller's non-looping stand-attack state after a wand cast.</summary>
    public float PlayWandAttackAnimation(float duration = -1f)
    {
        float clipWindow = duration > 0f
            ? duration
            : Mathf.Max(wandAttackAnimationDuration, DuckWandAttack.ResolveWandAttackDuration(animator));
        if (!HasState(wandAttackStateHash))
        {
            return clipWindow;
        }

        wandAttackUntil = Mathf.Max(wandAttackUntil, Time.time + clipWindow);
        currentStateHash = wandAttackStateHash;
        animator.CrossFadeInFixedTime(wandAttackStateHash, 0.06f);
        return clipWindow;
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

        Unit1WandPickup networkPickup = GetClosestNetworkWand();
        if (networkPickup != null)
        {
            networkPickup.RequestPickup();
            return;
        }

        Transform weapon = GetPickupWeapon();
        if (weapon == null || IsHeldByAnotherDuck(weapon) || !IsWeaponCloseEnough(weapon))
        {
            pickupWeapon = null;
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
        FusionDuckPlayer networkPlayer = GetComponent<FusionDuckPlayer>();
        networkPlayer?.SetHasWand(true);
        wantsToRun = false;
        SetWeaponVisible(heldWeapon, true);
        KeepWeaponInRightHand();
        PlayState(GetAnimationState());
    }

    /// <summary>
    /// The replicated scene wand calls this on every client. Only the duck
    /// holding the same networked wand may attack; replicas only receive the
    /// visual state and never write Fusion state.
    /// </summary>
    public void SetNetworkWandHeld(Unit1WandPickup wand, bool held)
    {
        if (wand == null)
        {
            return;
        }

        if (held)
        {
            networkWand = wand;
            hasWeapon = true;
            GetComponent<FusionDuckPlayer>()?.SetHasWand(true);
            return;
        }

        if (networkWand != wand)
        {
            return;
        }

        networkWand = null;
        hasWeapon = heldWeapon != null;
        GetComponent<FusionDuckPlayer>()?.SetHasWand(hasWeapon);
    }

    /// <summary>Used by the player health system when this duck is defeated.</summary>
    public void ReleaseNetworkWand()
    {
        networkWand?.RequestRelease();
    }

    private Unit1WandPickup GetClosestNetworkWand()
    {
        Unit1WandPickup[] wands = FindObjectsByType<Unit1WandPickup>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Unit1WandPickup closest = null;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < wands.Length; i++)
        {
            Unit1WandPickup candidate = wands[i];
            if (candidate == null || !candidate.CanBePickedUpBy(transform.position))
            {
                continue;
            }

            float distance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = candidate;
            }
        }

        return closest;
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

        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject candidate = objects[i];
            if (candidate == null || !IsKnownWandName(candidate.name) || IsHeldByAnotherDuck(candidate.transform))
            {
                continue;
            }

            float distance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                pickupWeapon = candidate.transform;
            }
        }

        return pickupWeapon;
    }

    private bool IsKnownWandName(string objectName)
    {
        return objectName == pickupWeaponName || objectName == "blue" || objectName == "purplr" || objectName == "purple";
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
