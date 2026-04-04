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

    // Per-bot jump randomization (set from difficulty at spawn)
    private float _jumpChance = 0.025f;
    private bool _inCombat;

    // Per-bot walk speed variation (set at spawn) — patrol only, combat is full speed
    private float _walkSpeedMultiplier = 1f;

    // Patrol path variety: occasional lateral offset during long walks
    private float _lastPathAdjustTime;
    private const float PATH_ADJUST_INTERVAL = 4f; // Check every 4s of walking
    private const float PATH_ADJUST_CHANCE = 0.35f; // 35% chance to adjust

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

        // Patrol path variety: occasionally nudge destination laterally during long walks
        if (!_inCombat && _hasDestination && !HasReachedDestination
            && Time.time - _lastPathAdjustTime > PATH_ADJUST_INTERVAL)
        {
            _lastPathAdjustTime = Time.time;
            if (Random.value < PATH_ADJUST_CHANCE)
            {
                // Offset 2-5m perpendicular to current heading
                Vector3 toTarget = (_destination - transform.position).normalized;
                Vector3 lateral = Vector3.Cross(toTarget, Vector3.up);
                float offset = Random.Range(2f, 5f) * (Random.value > 0.5f ? 1f : -1f);
                Vector3 adjusted = _destination + lateral * offset;
                if (HasNavMesh)
                {
                    NavMeshHit nmHit;
                    if (NavMesh.SamplePosition(adjusted, out nmHit, 5f, NavMesh.AllAreas))
                        SetDestination(nmHit.position);
                }
            }
        }

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
        {
            // In water and "close enough" but stuck? Pick a random escape direction
            if (_waterLevel >= 2)
                return (transform.right * (Random.value > 0.5f ? 1f : -1f)).normalized;
            return Vector3.zero; // Close enough on land
        }

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
        if (!_inCombat) wishSpeed *= _walkSpeedMultiplier; // Per-bot speed variation during patrol
        if (_isCrouching) wishSpeed *= BotConfig.CrouchSpeedScale;
        if (_waterLevel == 1) wishSpeed *= BotConfig.WadeSpeedScale; // Feet in water
        ApplyAcceleration(wishDir, wishSpeed, BotConfig.GroundAcceleration);

        // Jump with per-bot randomization based on difficulty.
        // Easy/Medium bots mostly walk; Hard bots hop more aggressively.
        // _jumpChance is set per-bot at spawn from difficulty config.
        // Safety: don't jump if there's no ground ahead (prevents jumping off edges)
        bool safeToJump = true;
        if (wishDir.sqrMagnitude > 0.01f)
        {
            Vector3 jumpLandPoint = transform.position + wishDir.normalized * 3f + Vector3.up * 0.5f;
            if (!Physics.Raycast(jumpLandPoint, Vector3.down, 5f, WORLD_MASK, QueryTriggerInteraction.Ignore))
                safeToJump = false; // No ground where we'd land — don't jump
        }

        if (safeToJump && !_isCrouching && _waterLevel == 0 && wishDir.sqrMagnitude > 0.01f
            && Time.time - _lastJumpTime >= BotConfig.JumpInterval
            && Random.value < (_inCombat ? BotConfig.JumpChanceCombat : _jumpChance))
        {
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
        // Stronger push when deep, weaker near surface to prevent bouncing
        float depth = Mathf.Max(0f, _waterPlaneY - transform.position.y) / BotConfig.NormalHeight;
        float surfaceForce = BotConfig.WaterSurfaceForce * Mathf.Clamp(depth + 0.5f, 0.5f, 2f);
        _velocity.y += surfaceForce * Time.deltaTime;

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
        // Two-level check: chest height (0.8m) and knee height (0.3m) to catch low obstacles
        Vector3 horizontal = new Vector3(move.x, 0f, move.z);
        float hMag = horizontal.magnitude;
        if (hMag > 0.001f)
        {
            RaycastHit wallHit;
            Vector3 hDir = horizontal.normalized;

            // Chest-level SphereCast (radius 0.4, was 0.3)
            bool hitWall = Physics.SphereCast(origin + Vector3.up * 0.8f, 0.4f, hDir,
                out wallHit, hMag + 0.05f, WORLD_MASK, QueryTriggerInteraction.Ignore);

            // Knee-level SphereCast if chest missed (catches low geometry like ramps, steps)
            if (!hitWall)
            {
                hitWall = Physics.SphereCast(origin + Vector3.up * 0.3f, 0.35f, hDir,
                    out wallHit, hMag + 0.05f, WORLD_MASK, QueryTriggerInteraction.Ignore);
            }

            if (hitWall)
            {
                // Clamp horizontal movement to wall distance
                float safeDist = Mathf.Max(0f, wallHit.distance - 0.05f);
                horizontal = hDir * safeDist;

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

        // --- Ground following (multi-ray for complex geometry) ---
        // Primary: center ray from 1.0m above. If it misses, try foot-edge rays
        // to catch cases where the center passes through a gap in irregular geometry.
        if (_velocity.y <= 0f)
        {
            float fallDist = Mathf.Max(0f, origin.y - nextPos.y);
            float rayStart = 1.0f;
            float rayLen = rayStart + 0.3f + fallDist;
            bool foundGround = false;
            float groundY = float.MinValue;

            // Primary center ray
            RaycastHit groundHit;
            if (Physics.Raycast(origin + Vector3.up * rayStart, Vector3.down, out groundHit,
                rayLen, WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                if (groundHit.point.y >= nextPos.y && groundHit.point.y <= origin.y + 0.15f)
                {
                    groundY = groundHit.point.y;
                    foundGround = true;
                }
            }

            // Secondary foot-edge rays (front, left, right) if center missed
            if (!foundGround)
            {
                Vector3 fwd = transform.forward * 0.3f;
                Vector3 right = transform.right * 0.3f;
                Vector3[] offsets = { fwd, -fwd, right, -right };

                for (int i = 0; i < offsets.Length; i++)
                {
                    Vector3 footOrigin = origin + offsets[i] + Vector3.up * rayStart;
                    if (Physics.Raycast(footOrigin, Vector3.down, out groundHit,
                        rayLen, WORLD_MASK, QueryTriggerInteraction.Ignore))
                    {
                        if (groundHit.point.y >= nextPos.y && groundHit.point.y <= origin.y + 0.15f)
                        {
                            groundY = Mathf.Max(groundY, groundHit.point.y);
                            foundGround = true;
                        }
                    }
                }
            }

            if (foundGround)
                nextPos.y = groundY;
        }

        // --- NavMesh safety clamp (ground only — never while falling/jumping/launched) ---
        // Prevents bots from noclipping through geometry on complex maps.
        // Only fires when truly on ground (not during ground-grace frames) and not falling.
        if (_isGrounded && !_isLaunched && !_isJumping
            && _velocity.y > -2f  // Not falling — prevents mid-fall freeze
            && _ungroundedFrames == 0 // Truly grounded, not in grace period
            && HasNavMesh)
        {
            NavMeshHit nmHit;
            if (NavMesh.SamplePosition(nextPos, out nmHit, 3f, NavMesh.AllAreas))
            {
                float drift = Vector3.Distance(nextPos, nmHit.position);
                if (drift > 1.2f)
                {
                    // Significant drift — snap back to valid NavMesh
                    nextPos = nmHit.position;
                }
            }
            else
            {
                // Completely off NavMesh — emergency warp to last known good position
                if (_agent != null && _agent.isOnNavMesh)
                {
                    nextPos = _agent.nextPosition;
                }
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

    /// <summary>
    /// Set jump frequency based on difficulty. Called once at spawn.
    /// Adds per-bot randomization (±30%) so bots don't sync up.
    /// </summary>
    public void SetJumpChanceForDifficulty(BotDifficulty difficulty)
    {
        float baseChance;
        switch (difficulty)
        {
            case BotDifficulty.Easy:   baseChance = BotConfig.JumpChanceEasy; break;
            case BotDifficulty.Hard:   baseChance = BotConfig.JumpChanceHard; break;
            default:                   baseChance = BotConfig.JumpChanceMedium; break;
        }
        // ±30% randomization per bot so they never sync
        _jumpChance = baseChance * Random.Range(0.7f, 1.3f);

        // Per-bot walk speed variation (0.85-1.1x) — makes patrol feel less uniform
        _walkSpeedMultiplier = Random.Range(0.85f, 1.1f);
    }

    /// <summary>
    /// Notify navigation that bot is in combat (increases jump frequency).
    /// </summary>
    public void SetInCombat(bool combat) { _inCombat = combat; }

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
    /// Dodge jump: jump with lateral velocity for evasive combat movement.
    /// Called by BotController's combat AI when in close range.
    /// Only works when grounded and not already jumping/launched/crouching.
    /// </summary>
    public void DodgeJump(Vector3 lateralDir)
    {
        if (_isJumping || _isLaunched || _isCrouching || !_isGrounded) return;

        _velocity.y = BotConfig.JumpSpeed;
        _velocity.x += lateralDir.x * BotConfig.DodgeJumpSpeed;
        _velocity.z += lateralDir.z * BotConfig.DodgeJumpSpeed;
        _isJumping = true;
        _isGrounded = false;
        _lastJumpTime = Time.time;
    }

    /// <summary>
    /// Check for cliff/edge and death zones ahead of current movement direction.
    /// Two checks: (1) ground existence ahead, (2) DeathArea trigger overlap ahead.
    /// Returns true if danger was detected and bot reversed direction.
    /// avoidChance: probability of avoiding (0=never, 1=always). Difficulty-based.
    /// </summary>
    public bool CheckCliffAhead(float avoidChance)
    {
        // Don't check during jumps, launches, or when stopped
        if (_isLaunched || _isJumping || !_isGrounded) return false;

        Vector3 moveDir = _velocity;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.5f) return false;
        moveDir.Normalize();

        bool dangerDetected = false;

        // Check 1: Ground existence — raycast from 2m ahead, look for ground within 8m below.
        // Wider check than before to catch edges earlier.
        Vector3 checkPoint = transform.position + moveDir * 2f + Vector3.up * 0.5f;
        if (!Physics.Raycast(checkPoint, Vector3.down, 8f, WORLD_MASK, QueryTriggerInteraction.Ignore))
            dangerDetected = true;

        // Check 2: DeathArea ahead — OverlapSphere 3m in front to detect death zone triggers.
        // This catches death zones that have solid ground underneath (like lava pits on Gideon's).
        if (!dangerDetected)
        {
            Vector3 aheadPoint = transform.position + moveDir * 3f + Vector3.up * 0.5f;
            Collider[] hits = Physics.OverlapSphere(aheadPoint, 1.5f,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].GetComponent<DeathArea>() != null)
                {
                    dangerDetected = true;
                    break;
                }
            }
        }

        if (dangerDetected && Random.value < avoidChance)
        {
            // Reverse horizontal velocity
            _velocity.x *= -0.7f;
            _velocity.z *= -0.7f;

            // Suppress jumping for 2 seconds to avoid hopping off the edge
            _lastJumpTime = Time.time + 2f;

            // Pick a NEW patrol destination away from danger so the bot doesn't path back
            Vector3 safeDir = -moveDir;
            Vector3 safePoint = transform.position + safeDir * 8f;
            if (HasNavMesh)
            {
                NavMeshHit nmHit;
                if (NavMesh.SamplePosition(safePoint, out nmHit, 10f, NavMesh.AllAreas))
                    SetDestination(nmHit.position);
            }

            return true;
        }
        return false;
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

    /// <summary>
    /// Check if a position is safe for navigation — not near death zones or over voids.
    /// Used by PickRandomPatrolPoint to reject dangerous patrol destinations.
    /// </summary>
    private bool IsPositionSafe(Vector3 pos)
    {
        // Check 1: Ground exists below (not over void)
        if (!Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, 10f,
            WORLD_MASK, QueryTriggerInteraction.Ignore))
            return false;

        // Check 2: No DeathArea trigger within 3m
        Collider[] hits = Physics.OverlapSphere(pos + Vector3.up * 0.5f, 3f,
            Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].GetComponent<DeathArea>() != null)
                return false;
        }

        return true;
    }

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
        // Try up to 5 candidates to find a safe patrol point (not near death zones or cliffs)
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 randomDir = Random.insideUnitSphere * BotConfig.PatrolRadius;
            randomDir.y = 0f;
            Vector3 candidate = origin + randomDir;

            if (HasNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(candidate, out hit, BotConfig.PatrolRadius, NavMesh.AllAreas))
                {
                    // Safety check: reject destinations near death zones or over voids
                    if (IsPositionSafe(hit.position))
                    {
                        SetDestination(hit.position);
                        return;
                    }
                    continue; // Unsafe — try another candidate
                }
            }

            // Fallback: check ground exists
            RaycastHit groundHit;
            candidate.y = origin.y + 2f;
            if (Physics.Raycast(candidate + Vector3.up * 0.5f, Vector3.down, out groundHit, 50f,
                WORLD_MASK, QueryTriggerInteraction.Ignore))
            {
                candidate.y = groundHit.point.y;
                if (IsPositionSafe(candidate))
                {
                    SetDestination(candidate);
                    return;
                }
            }
        }

        // All attempts failed — stay near current position
        SetDestination(origin);
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

    // ================================================================
    // DeathArea / LevelBoundary Trigger Detection
    // ================================================================

    /// <summary>
    /// Safety net: if the bot's root collider enters a DeathArea trigger, kill it.
    /// The child BotJumpPadTrigger is on IgnoreRaycast layer which may not collide
    /// with all trigger zones depending on the physics matrix.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // Check if we entered a DeathArea or LevelBoundary
        if (other.GetComponent<DeathArea>() != null || other.GetComponent<LevelBoundary>() != null)
        {
            var bot = GetComponent<BotController>();
            if (bot != null && bot.Health > 0)
            {
                Debug.Log("[Bot] " + bot.BotName + " entered death zone: " + other.name);
                bot.KillByEnvironment();
            }
        }
    }
}
