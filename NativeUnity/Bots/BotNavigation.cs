using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Quake-style velocity movement system for bots, matched to original 4.3.8 CharacterMoveController.
///
/// Key mechanics from the original source:
///   - Persistent _currentVelocity vector (horizontal speed preserved across jumps)
///   - Jump only sets Y velocity — horizontal is UNTOUCHED (enables bunny hop)
///   - Air acceleration (3) allows steering but not speed gain from wishDir alone
///   - Ground friction (8) decelerates on ground — jump before friction = keep speed
///   - Speed > WalkSpeed is allowed in air (Quake-style accumulation)
///   - SphereCast wall collision + raycast ceiling/ground (prevents noclipping)
///   - Water movement: reduced speed, 10% gravity, terminal velocity -3
///
/// NavMeshAgent is used ONLY for pathfinding (updatePosition = false).
/// All actual movement is handled by this script using the agent's desired direction.
/// </summary>
public class BotNavigation : MonoBehaviour
{
    // NavMeshAgent — pathfinding only, does NOT control position
    private NavMeshAgent _agent;
    private bool _agentChecked;

    // Persistent velocity (Quake-style — horizontal preserved across jumps)
    private Vector3 _velocity;

    // Ground state
    private bool _isGrounded;
    private int _ungroundedFrames;
    private const int GROUND_GRACE_FRAMES = 5; // Original: 5-frame grace period

    // Jump / Bunny hop
    private bool _isJumping;
    private float _lastJumpTime;
    private int _consecutiveHops;

    // JumpPad / Accelerator launch
    private bool _isLaunched;
    private Vector3 _launchVelocity;
    private float _launchStartTime;

    // Crouch
    private bool _isCrouching;

    // Water
    private int _waterLevel;        // 0=dry, 1=surface, 2=wading, 3=submerged
    private float _waterPlaneY = float.MinValue;
    private bool _waterPlaneChecked;

    // Navigation target
    private Vector3 _destination;
    private bool _stopped;
    private bool _hasDestination;

    // Stuck detection
    private Vector3 _lastStuckCheckPos;
    private float _lastStuckCheckTime;
    private int _stuckCount;
    private const float STUCK_CHECK_INTERVAL = 2f;
    private const float STUCK_MOVE_THRESHOLD = 0.8f;
    private const int MAX_STUCK_BEFORE_WARP = 3;

    // Wall-stuck
    private float _wallFacingTime;
    private const float WALL_STUCK_TIMEOUT = 1.5f;

    // Original 4.3.8 friction values (from CharacterMoveController.ApplyFriction)
    private const float GROUND_FRICTION = 8f;
    private const float STOP_SPEED = 8f;

    // Raycast layer mask: exclude LocalPlayer(18), RemotePlayer(20) to avoid hitting bot colliders.
    // Also always use QueryTriggerInteraction.Ignore to skip trigger volumes.
    private static readonly int WORLD_MASK = ~((1 << 18) | (1 << 20));

    // ================================================================
    // Public Properties
    // ================================================================

    public bool IsLaunched => _isLaunched;
    public bool IsJumping => _isJumping;
    public bool IsCrouching => _isCrouching;
    public int WaterLevel => _waterLevel;
    public bool IsInWater => _waterLevel > 0;

    public bool HasNavMesh
    {
        get { return _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh; }
    }

    public bool HasReachedDestination
    {
        get
        {
            if (!_hasDestination) return true;
            Vector3 flat = _destination - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude < 1.5f * 1.5f;
        }
    }

    public float CurrentSpeed
    {
        get
        {
            Vector3 h = new Vector3(_velocity.x, 0f, _velocity.z);
            return h.magnitude;
        }
    }

    public Vector3 DesiredVelocity
    {
        get
        {
            if (_stopped) return Vector3.zero;
            return new Vector3(_velocity.x, 0f, _velocity.z);
        }
    }

    // ================================================================
    // Init
    // ================================================================

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _destination = transform.position;
        _lastStuckCheckPos = transform.position;
        _lastStuckCheckTime = Time.time;

        if (_agent != null)
        {
            // NavMeshAgent for pathfinding ONLY — we control the actual position
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }

        SnapToGround();
        _isGrounded = true;
    }

    // ================================================================
    // Main Update
    // ================================================================

    void Update()
    {
        // First frame: validate NavMeshAgent
        if (!_agentChecked)
        {
            _agentChecked = true;
            if (_agent != null && _agent.enabled && !_agent.isOnNavMesh)
            {
                Debug.LogWarning("[BotNav] NavMeshAgent not on NavMesh — disabling agent, using direct navigation");
                _agent.enabled = false;
            }
        }

        // JumpPad launch overrides everything
        if (_isLaunched)
        {
            UpdateLaunch();
            return;
        }

        if (_stopped) return;

        // Death floor check
        if (transform.position.y < BotConfig.DeathFloorY)
        {
            var bot = GetComponent<BotController>();
            if (bot != null && bot.Health > 0)
                bot.KillByEnvironment();
            return;
        }

        // Detect water level
        DetectWater();

        // Get desired movement direction (from NavMeshAgent pathfinding or direct)
        Vector3 wishDir = GetWishDirection();

        // Update ground state
        UpdateGroundState();

        // Apply movement physics based on environment
        if (_waterLevel >= 3)
        {
            // Fully submerged — water swimming
            MoveInWater(wishDir);
        }
        else if (_waterLevel >= 2)
        {
            // Wading — water rim movement
            MoveOnWaterRim(wishDir);
        }
        else if (_isGrounded)
        {
            MoveOnGround(wishDir);
        }
        else
        {
            MoveInAir(wishDir);
        }

        // Apply velocity to position + handle collisions
        ApplyMovement();

        // Keep NavMeshAgent synced to our position (for pathfinding)
        SyncAgentPosition();

        // Check for stuck
        CheckStuck();
    }

    // ================================================================
    // Water Detection
    // ================================================================

    /// <summary>
    /// Detect water level using MapConfiguration.WaterPlaneHeight.
    /// Enclosure-based thresholds match original CharacterMoveController:
    /// 0=dry, 1=surface (<40%), 2=wading (40-80%), 3=submerged (80%+)
    /// </summary>
    private void DetectWater()
    {
        // Cache water plane height (only check once per map load)
        if (!_waterPlaneChecked)
        {
            _waterPlaneChecked = true;
            try
            {
                var space = GameState.CurrentSpace;
                if (space != null && space.HasWaterPlane)
                    _waterPlaneY = space.WaterPlaneHeight;
                else
                    _waterPlaneY = -9999f;
            }
            catch { _waterPlaneY = -9999f; }
        }

        float feetY = transform.position.y;

        if (feetY >= _waterPlaneY)
        {
            _waterLevel = 0;
            return;
        }

        // Calculate enclosure: what fraction of bot height is below water surface
        float enclosure = (_waterPlaneY - feetY) / BotConfig.NormalHeight;

        if (enclosure >= 0.8f)
            _waterLevel = 3;      // Fully submerged
        else if (enclosure >= 0.4f)
            _waterLevel = 2;      // Wading
        else
            _waterLevel = 1;      // Surface / feet in water
    }

    // ================================================================
    // Wish Direction (from NavMeshAgent or fallback)
    // ================================================================

    /// <summary>
    /// Get the desired horizontal movement direction.
    /// NavMeshAgent provides intelligent pathfinding; fallback uses direct line to target.
    /// </summary>
    private Vector3 GetWishDirection()
    {
        if (!_hasDestination) return Vector3.zero;

        // Try NavMeshAgent first (smart pathfinding around obstacles)
        if (HasNavMesh && _agent.hasPath && !_agent.pathPending)
        {
            Vector3 agentDir = _agent.desiredVelocity;
            agentDir.y = 0f;
            if (agentDir.sqrMagnitude > 0.01f)
                return agentDir.normalized;
        }

        // Fallback: direct line to destination
        Vector3 toTarget = _destination - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 1.5f * 1.5f)
            return Vector3.zero; // Close enough

        return toTarget.normalized;
    }

    // ================================================================
    // Ground Detection (matches original 5-frame grace period)
    // ================================================================

    private void UpdateGroundState()
    {
        // Raycast-based ground detection (reliable, layer-independent)
        RaycastHit hit;
        bool groundBelow = Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out hit, 0.5f,
            WORLD_MASK, QueryTriggerInteraction.Ignore);

        if (groundBelow && _velocity.y <= 0.1f)
        {
            if (_isJumping && _ungroundedFrames > GROUND_GRACE_FRAMES)
            {
                // Landing from a jump
                _isJumping = false;
            }
            _ungroundedFrames = 0;
            _isGrounded = true;
        }
        else
        {
            _ungroundedFrames++;
            if (_ungroundedFrames > GROUND_GRACE_FRAMES)
            {
                _isGrounded = false;
            }
        }
    }

    // ================================================================
    // Ground Movement (original MoveOnGround)
    // ================================================================

    /// <summary>
    /// Ground movement: apply friction, accelerate, then bunny hop.
    /// Original sequence: friction -> acceleration -> check jump -> apply gravity.
    /// Water level 1 (surface) applies slight speed reduction.
    /// </summary>
    private void MoveOnGround(Vector3 wishDir)
    {
        // Apply friction (decelerates horizontal speed)
        ApplyFriction();

        // Apply ground acceleration (15 — fast, builds to WalkSpeed quickly)
        float wishSpeed = BotConfig.WalkSpeed;
        if (_isCrouching) wishSpeed *= BotConfig.CrouchSpeedScale;
        if (_waterLevel == 1) wishSpeed *= BotConfig.WadeSpeedScale; // Feet in water
        ApplyAcceleration(wishDir, wishSpeed, BotConfig.GroundAcceleration);

        // Bunny hop: jump if we have a direction, not crouching, and interval passed
        if (!_isCrouching && _waterLevel == 0 && wishDir.sqrMagnitude > 0.01f
            && Time.time - _lastJumpTime >= BotConfig.JumpInterval)
        {
            // ONLY set Y velocity — horizontal velocity is PRESERVED (this is the key!)
            // Ceiling collision handled by ApplyMovement's raycast (kills upward velocity)
            _velocity.y = BotConfig.JumpSpeed;
            _isJumping = true;
            _isGrounded = false;
            _lastJumpTime = Time.time;
            _consecutiveHops++;
        }
        else
        {
            // Stay grounded: small downward velocity
            _velocity.y = -BotConfig.JumpGravity * Time.deltaTime;
        }
    }

    // ================================================================
    // Air Movement (original MoveInAir)
    // ================================================================

    /// <summary>
    /// Air movement: reduced acceleration (3 vs 15), gravity applied.
    /// Horizontal velocity is preserved from ground — this enables bunny hop speed gain.
    /// Air accel only allows STEERING, not raw speed boost from wishDir.
    /// </summary>
    private void MoveInAir(Vector3 wishDir)
    {
        // Air acceleration (3) — can steer direction but speed gain is limited
        ApplyAcceleration(wishDir, BotConfig.WalkSpeed, BotConfig.AirAcceleration);

        // Apply gravity (50 * deltaTime, matching original EnviromentSettings.Gravity)
        _velocity.y -= BotConfig.JumpGravity * Time.deltaTime;
    }

    // ================================================================
    // Water Movement (from original CharacterMoveController.MoveInWater)
    // ================================================================

    /// <summary>
    /// Fully submerged water movement (WaterLevel 3).
    /// Gravity reduced to 10% (50 * 0.1 = 5), terminal velocity -3,
    /// acceleration 6 (from EnviromentSettings.WaterAcceleration).
    /// Bots try to swim upward toward the water surface.
    /// </summary>
    private void MoveInWater(Vector3 wishDir)
    {
        // Apply friction (same as ground, matching original MoveInWater)
        ApplyFriction();

        // Reduced speed in water
        float wishSpeed = BotConfig.WalkSpeed * BotConfig.SwimSpeedScale;
        ApplyAcceleration(wishDir, wishSpeed, BotConfig.WaterAcceleration);

        // Buoyancy: bot tries to surface (swim upward)
        _velocity.y += BotConfig.WaterSurfaceForce * Time.deltaTime;

        // Gravity in water (10% of normal)
        if (_velocity.y > -BotConfig.WaterTerminalVelocity)
            _velocity.y -= BotConfig.JumpGravity * BotConfig.WaterGravityScale * Time.deltaTime;
        else
            _velocity.y = Mathf.Lerp(_velocity.y, BotConfig.WaterTerminalVelocity, Time.deltaTime * 6f);

        // No bunny hop in water
        _isJumping = false;
    }

    /// <summary>
    /// Wading water movement (WaterLevel 2, 40-80% enclosure).
    /// Speed reduced by wade scale (0.8), reduced gravity.
    /// Matches original MoveOnWaterRim.
    /// </summary>
    private void MoveOnWaterRim(Vector3 wishDir)
    {
        ApplyFriction();

        float wishSpeed = BotConfig.WalkSpeed * BotConfig.WadeSpeedScale;
        if (_isCrouching) wishSpeed *= BotConfig.CrouchSpeedScale;
        ApplyAcceleration(wishDir, wishSpeed, BotConfig.WaterAcceleration);

        // Reduced gravity while wading
        if (_velocity.y > BotConfig.WaterTerminalVelocity)
            _velocity.y -= BotConfig.JumpGravity * BotConfig.WaterGravityScale * Time.deltaTime;
        else
            _velocity.y = Mathf.Lerp(_velocity.y, BotConfig.WaterTerminalVelocity, Time.deltaTime * 6f);

        // No bunny hop while wading
        _isJumping = false;
    }

    // ================================================================
    // Quake-Style Acceleration (from original ApplyAcceleration)
    // ================================================================

    /// <summary>
    /// Quake engine acceleration formula from original 4.3.8 CharacterMoveController.
    /// Key property: speed can EXCEED wishSpeed in air because we only cap addSpeed,
    /// not total speed. This is what enables bunny hop speed accumulation.
    /// </summary>
    private void ApplyAcceleration(Vector3 wishDir, float wishSpeed, float accel)
    {
        if (wishDir.sqrMagnitude < 0.001f) return;

        // Project current horizontal velocity onto wish direction
        Vector3 horizontalVel = new Vector3(_velocity.x, 0f, _velocity.z);
        float currentSpeed = Vector3.Dot(horizontalVel, wishDir);

        // How much speed we need to add to reach wishSpeed in this direction
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0f) return; // Already at or above wish speed in this direction

        // Acceleration this frame (capped at addSpeed to prevent overshoot)
        float accelAmount = accel * wishSpeed * Time.deltaTime;
        if (accelAmount > addSpeed)
            accelAmount = addSpeed;

        // Add acceleration in wish direction (preserves perpendicular velocity!)
        _velocity.x += accelAmount * wishDir.x;
        _velocity.z += accelAmount * wishDir.z;
    }

    // ================================================================
    // Ground Friction (from original ApplyFriction)
    // ================================================================

    /// <summary>
    /// Ground friction from original 4.3.8 CharacterMoveController.
    /// drop = max(STOP_SPEED, speed) * GROUND_FRICTION * deltaTime
    /// This is what bunny hop exploits: jump BEFORE friction fully decelerates.
    /// </summary>
    private void ApplyFriction()
    {
        float speed = Mathf.Sqrt(_velocity.x * _velocity.x + _velocity.z * _velocity.z);
        if (speed < 0.1f)
        {
            _velocity.x = 0f;
            _velocity.z = 0f;
            return;
        }

        float drop = Mathf.Max(speed, STOP_SPEED) * GROUND_FRICTION * Time.deltaTime;
        float newSpeed = Mathf.Max(0f, speed - drop);
        float scale = newSpeed / speed;

        _velocity.x *= scale;
        _velocity.z *= scale;
    }

    // ================================================================
    // Apply Movement + Collision
    // ================================================================

    /// <summary>
    /// Apply velocity to position using raycasts for ground-following and SphereCast
    /// for wall/ceiling collision to prevent noclipping through geometry.
    /// </summary>
    private void ApplyMovement()
    {
        float dt = Time.deltaTime;
        Vector3 move = _velocity * dt;
        Vector3 origin = transform.position;

        // --- Wall collision (SphereCast — volumetric, prevents clipping through walls) ---
        Vector3 horizontal = new Vector3(move.x, 0f, move.z);
        float hMag = horizontal.magnitude;
        if (hMag > 0.001f)
        {
            RaycastHit wallHit;
            // Cast a sphere from chest height in the movement direction
            if (Physics.SphereCast(origin + Vector3.up * 0.8f, 0.3f, horizontal.normalized,
                out wallHit, hMag + 0.05f, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                // Clamp horizontal movement to wall distance
                float safeDist = Mathf.Max(0f, wallHit.distance - 0.05f);
                horizontal = horizontal.normalized * safeDist;

                // Slide along wall surface
                Vector3 slideDir = Vector3.ProjectOnPlane(new Vector3(_velocity.x, 0f, _velocity.z), wallHit.normal);
                _velocity.x = slideDir.x;
                _velocity.z = slideDir.z;
                _consecutiveHops = 0;

                _wallFacingTime += dt;
                if (_wallFacingTime > WALL_STUCK_TIMEOUT)
                {
                    EscapeWallStuck();
                    _wallFacingTime = 0f;
                }
            }
            else
            {
                _wallFacingTime = Mathf.Max(0f, _wallFacingTime - dt);
            }
        }

        // --- Ceiling collision (raycast up from head) ---
        if (move.y > 0f)
        {
            RaycastHit ceilHit;
            if (Physics.Raycast(origin + Vector3.up * 1.6f, Vector3.up, out ceilHit,
                move.y + 0.1f, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                move.y = Mathf.Max(0f, ceilHit.distance - 0.05f);
                _velocity.y = 0f;
            }
        }

        // Compute next position
        Vector3 nextPos = origin + new Vector3(horizontal.x, move.y, horizontal.z);

        // --- Ground following ---
        // CRITICAL: Cast from ORIGINAL position (origin), not from nextPos.
        // When falling fast, nextPos can be well below the floor. Raycasting from
        // nextPos+0.3 would start inside/below floor geometry and miss the surface.
        // Casting from origin+0.3 (above the floor) detects the surface reliably.
        if (_velocity.y <= 0f)
        {
            float fallDist = Mathf.Max(0f, origin.y - nextPos.y);
            RaycastHit groundHit;
            if (Physics.Raycast(origin + Vector3.up * 0.3f, Vector3.down, out groundHit,
                0.6f + fallDist, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                // Snap to ground if it's between origin and nextPos (or just below feet)
                if (groundHit.point.y >= nextPos.y && groundHit.point.y <= origin.y + 0.15f)
                    nextPos.y = groundHit.point.y;
            }
        }

        transform.position = nextPos;
    }

    // ================================================================
    // NavMeshAgent Sync
    // ================================================================

    /// <summary>
    /// Keep NavMeshAgent's internal position synced to our actual position
    /// so its pathfinding stays accurate.
    /// </summary>
    private void SyncAgentPosition()
    {
        if (_agent == null || !_agent.enabled) return;

        _agent.nextPosition = transform.position;

        // If agent fell off NavMesh, try to re-place it
        if (!_agent.isOnNavMesh)
        {
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(transform.position, out navHit, 5f, NavMesh.AllAreas))
            {
                _agent.Warp(navHit.position);
            }
        }
    }

    // ================================================================
    // Wall-Stuck Escape
    // ================================================================

    private void EscapeWallStuck()
    {
        Vector3 escapeDir = -transform.forward;
        Vector3 escape = transform.position + escapeDir * 8f;

        RaycastHit hit;
        if (Physics.Raycast(escape + Vector3.up * 2f, Vector3.down, out hit, 50f,
            WORLD_MASK, QueryTriggerInteraction.Ignore))
            escape.y = hit.point.y;

        SetDestination(escape);
        _consecutiveHops = 0;
        Debug.Log("[BotNav] Wall-stuck — escaping behind");
    }

    // ================================================================
    // Stuck Detection
    // ================================================================

    private void CheckStuck()
    {
        if (Time.time - _lastStuckCheckTime < STUCK_CHECK_INTERVAL) return;
        _lastStuckCheckTime = Time.time;

        float moved = Vector3.Distance(transform.position, _lastStuckCheckPos);
        if (moved < STUCK_MOVE_THRESHOLD)
        {
            _stuckCount++;
            if (_stuckCount >= MAX_STUCK_BEFORE_WARP)
            {
                _stuckCount = 0;

                if (HasNavMesh)
                {
                    NavMeshHit navHit;
                    Vector3 behind = transform.position - transform.forward * 5f;
                    if (NavMesh.SamplePosition(behind, out navHit, 10f, NavMesh.AllAreas))
                    {
                        transform.position = navHit.position;
                        _agent.Warp(navHit.position);
                        Debug.Log("[BotNav] Warped to " + navHit.position);
                    }
                }

                PickRandomPatrolPoint(transform.position);
            }
            else
            {
                PickRandomPatrolPoint(transform.position);
            }
        }
        else
        {
            _stuckCount = 0;
        }

        _lastStuckCheckPos = transform.position;
    }

    // ================================================================
    // Crouch
    // ================================================================

    /// <summary>
    /// Toggle crouch. When enabling crouch, kills vertical velocity and clears jump state.
    /// Does NOT snap to ground — lets normal gravity handle landing smoothly.
    /// Cannot crouch while launched from JumpPad (matches 4.3.8 CheckDuck).
    /// </summary>
    public void SetCrouching(bool crouch)
    {
        if (crouch && _isLaunched) return; // Can't crouch while launched

        _isCrouching = crouch;

        if (crouch)
        {
            // Kill vertical velocity and clear jump state
            // (bots bunny hop constantly, so _isJumping is almost always true)
            _velocity.y = 0f;
            _isJumping = false;
            _isGrounded = true; // Force grounded so MoveOnGround runs (not MoveInAir)
            _consecutiveHops = 0;
        }
    }

    // ================================================================
    // JumpPad / Accelerator Launch
    // ================================================================

    /// <summary>
    /// Called by ForceField.OnTriggerEnter when a bot hits a JumpPad or Accelerator.
    /// Disables NavMeshAgent and applies ballistic velocity.
    /// Both JumpPads and Accelerators use ForceField with different force vectors:
    /// - JumpPad: high vertical, moderate horizontal (upward launch)
    /// - Accelerator: moderate vertical, higher horizontal ratio (speed boost)
    /// </summary>
    public void ApplyJumpPadForce(Vector3 force)
    {
        _isCrouching = false;

        // Compute launch velocity (matching player's ForceType.Exclusive * LevelEnviroment.Modifier)
        Vector3 launchVel = force * 0.035f;

        // If already launched, only update if new force is stronger
        if (_isLaunched && launchVel.magnitude < _launchVelocity.magnitude * 0.5f)
            return;

        if (_agent != null && _agent.enabled)
            _agent.enabled = false;

        _launchVelocity = launchVel;
        _isLaunched = true;
        _launchStartTime = Time.time;
        _isJumping = false;
        _consecutiveHops = 0;
        _velocity = Vector3.zero; // Clear regular velocity during launch
        _waterLevel = 0; // Exit water during launch

        Debug.Log("[BotNav] JumpPad launched! velocity=" + _launchVelocity);
    }

    /// <summary>
    /// Update launch trajectory. Applies gravity, detects landing via sweep raycast.
    /// Includes timeout safety (10s max flight) and death floor check.
    /// On landing, carries a portion of horizontal momentum for smooth transition.
    /// </summary>
    private void UpdateLaunch()
    {
        float dt = Time.deltaTime;

        // Apply gravity
        _launchVelocity.y -= BotConfig.JumpGravity * dt;

        // Timeout safety: max flight time
        if (Time.time - _launchStartTime > BotConfig.LaunchTimeout)
        {
            Debug.LogWarning("[BotNav] Launch timeout — force-landing");
            EndLaunch(transform.position);
            return;
        }

        // Death floor check
        Vector3 projected = transform.position + _launchVelocity * dt;
        if (projected.y < BotConfig.DeathFloorY)
        {
            var bot = GetComponent<BotController>();
            if (bot != null && bot.Health > 0)
                bot.KillByEnvironment();
            _isLaunched = false;
            return;
        }

        // Ceiling collision during upward launch
        if (_launchVelocity.y > 0f)
        {
            RaycastHit ceilHit;
            if (Physics.Raycast(transform.position + Vector3.up * 1.6f, Vector3.up, out ceilHit,
                _launchVelocity.y * dt + 0.2f, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                _launchVelocity.y = 0f;
                projected.y = transform.position.y + Mathf.Max(0f, ceilHit.distance - 0.1f);
            }
        }

        // Wall collision during launch (SphereCast)
        Vector3 hMove = new Vector3(_launchVelocity.x * dt, 0f, _launchVelocity.z * dt);
        if (hMove.sqrMagnitude > 0.001f)
        {
            RaycastHit wallHit;
            if (Physics.SphereCast(transform.position + Vector3.up * 0.8f, 0.3f, hMove.normalized,
                out wallHit, hMove.magnitude + 0.05f, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                float safeDist = Mathf.Max(0f, wallHit.distance - 0.05f);
                projected.x = transform.position.x + hMove.normalized.x * safeDist;
                projected.z = transform.position.z + hMove.normalized.z * safeDist;
                // Bounce off wall slightly
                Vector3 reflected = Vector3.Reflect(new Vector3(_launchVelocity.x, 0f, _launchVelocity.z), wallHit.normal);
                _launchVelocity.x = reflected.x * 0.5f;
                _launchVelocity.z = reflected.z * 0.5f;
            }
        }

        transform.position = projected;

        // Landing detection via raycast (when descending)
        if (_launchVelocity.y < 0f)
        {
            RaycastHit hit;
            float fallSpeed = Mathf.Abs(_launchVelocity.y * dt);
            float rayLen = fallSpeed + 2.0f;
            if (Physics.Raycast(projected + Vector3.up * 1.0f, Vector3.down, out hit, rayLen + 1.0f,
                WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                if (projected.y <= hit.point.y + 0.3f)
                {
                    transform.position = new Vector3(projected.x, hit.point.y, projected.z);
                    EndLaunch(transform.position);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// End launch and restore normal movement. Carries a portion of horizontal
    /// launch velocity for smoother landing transitions.
    /// </summary>
    private void EndLaunch(Vector3 landPos)
    {
        _isLaunched = false;
        _isGrounded = true;
        _isJumping = false;

        // Carry some horizontal momentum from launch for smooth landing
        _velocity = new Vector3(
            _launchVelocity.x * BotConfig.LandingMomentumKeep,
            0f,
            _launchVelocity.z * BotConfig.LandingMomentumKeep
        );
        _launchVelocity = Vector3.zero;

        transform.position = landPos;

        // Re-enable NavMeshAgent
        if (_agent != null && !_agent.enabled)
        {
            _agent.enabled = true;
            if (_agent.isOnNavMesh)
            {
                _agent.Warp(landPos);
            }
            else
            {
                // Try to find nearest NavMesh point
                NavMeshHit navHit;
                if (NavMesh.SamplePosition(landPos, out navHit, 10f, NavMesh.AllAreas))
                {
                    _agent.Warp(navHit.position);
                    transform.position = navHit.position;
                }
                else
                {
                    _agent.enabled = false; // Give up on NavMesh for now
                }
            }
        }

        Debug.Log("[BotNav] Landed at " + landPos);
    }

    // ================================================================
    // Navigation Commands
    // ================================================================

    public void SetDestination(Vector3 target)
    {
        _stopped = false;
        _destination = target;
        _hasDestination = true;

        if (HasNavMesh)
        {
            _agent.isStopped = false;
            _agent.SetDestination(target);
        }
    }

    public void PickRandomPatrolPoint(Vector3 origin)
    {
        Vector3 randomDir = Random.insideUnitSphere * BotConfig.PatrolRadius;
        randomDir.y = 0f;
        Vector3 candidate = origin + randomDir;

        if (HasNavMesh)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, BotConfig.PatrolRadius, NavMesh.AllAreas))
            {
                SetDestination(hit.position);
                return;
            }
        }

        // Fallback: check ground exists
        RaycastHit groundHit;
        candidate.y = origin.y + 2f;
        if (Physics.Raycast(candidate + Vector3.up * 0.5f, Vector3.down, out groundHit, 50f,
            WORLD_MASK, QueryTriggerInteraction.Ignore))
        {
            candidate.y = groundHit.point.y;
            SetDestination(candidate);
        }
        else
        {
            candidate = origin + randomDir.normalized * 10f;
            candidate.y = origin.y;
            SetDestination(candidate);
        }
    }

    public void Stop()
    {
        _stopped = true;
        _velocity = Vector3.zero;

        if (HasNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
    }

    public void Resume()
    {
        _stopped = false;

        if (HasNavMesh)
            _agent.isStopped = false;
    }

    // ================================================================
    // JumpPad Navigation (J key)
    // ================================================================

    /// <summary>
    /// Navigate to the nearest JumpPad (ForceField). Uses FindObjectsOfType + distance.
    /// Sets stoppingDistance to 0 so the bot walks fully INTO the trigger volume.
    /// </summary>
    public void GoToNearestJumpPad()
    {
        ForceField[] jumpPads = Object.FindObjectsOfType<ForceField>();
        if (jumpPads.Length == 0)
        {
            Debug.Log("[BotNav] No JumpPads found in scene");
            return;
        }

        float bestDist = float.MaxValue;
        ForceField bestPad = null;

        foreach (var pad in jumpPads)
        {
            if (pad == null) continue;
            // Include BOTH JumpPads and Accelerators — they all use ForceField
            float dist = Vector3.Distance(transform.position, pad.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPad = pad;
            }
        }

        if (bestPad != null && bestDist < 500f)
        {
            Vector3 padCenter = bestPad.transform.position;
            var col = bestPad.GetComponent<Collider>();
            if (col != null)
                padCenter = col.bounds.center;

            // Set stopping distance to 0 so bot walks INTO the trigger
            if (_agent != null)
                _agent.stoppingDistance = 0f;

            SetDestination(padCenter);

            Debug.Log("[BotNav] Heading to pad '" + bestPad.gameObject.name +
                "' at " + padCenter + " (dist=" + bestDist.ToString("F1") + "m)");
        }
    }

    public void ResetStoppingDistance()
    {
        if (_agent != null)
            _agent.stoppingDistance = 1f;
    }

    // ================================================================
    // Map Reset (called on level change)
    // ================================================================

    /// <summary>
    /// Reset cached map data when changing levels.
    /// </summary>
    public void ResetMapData()
    {
        _waterPlaneChecked = false;
        _waterPlaneY = float.MinValue;
        _waterLevel = 0;
    }

    // ================================================================
    // Utility
    // ================================================================

    public void SnapToGround()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 50f,
            WORLD_MASK, QueryTriggerInteraction.Ignore))
        {
            Vector3 pos = transform.position;
            pos.y = hit.point.y;
            transform.position = pos;
        }
    }
}
