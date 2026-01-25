using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UberStrikeBot
{
    public enum BotState
    {
        Idle,
        Patrol,
        Combat,
        Search,
        Flee
    }

    public class MemoryRecord
    {
        public Vector3 Position;
        public float Timestamp;
        public bool IsVisual; // True if seen, False if heard/shared
    }

    /// <summary>
    /// Helper script attached to body parts (Head, Arm, Leg) to forward damage to the main controller.
    /// </summary>
    public class DamageForwarder : MonoBehaviour
    {
        public BotController TargetBot;

        // Catch standard Unity messages
        public void ApplyDamage(float damage) 
        { 
            if(TargetBot) TargetBot.ReceiveDamage(damage); 
        }
        
        public void TakeDamage(float damage) 
        { 
            if(TargetBot) TargetBot.ReceiveDamage(damage); 
        }
        
        public void OnHit(float damage) 
        { 
            if(TargetBot) TargetBot.ReceiveDamage(damage); 
        }
        
        // Catch UberStrike specific "DamageInfo" message (via reflection to avoid dependency issues)
        void OnMessage(object msg) 
        {
             // If we get a generic object message, check if it has a 'Damage' property or assume fallback
             if (TargetBot) TargetBot.ReceiveDamage(20f); // Fallback amount
        }
    }

    /// <summary>
    /// Advanced Bot Controller with Perception, Decision, and Execution layers.
    /// </summary>
    public class BotController : MonoBehaviour
    {
        // --- 1. CONFIGURATION & TUNING ---
        public float ViewAngle = 120f;
        public float ViewDistance = 100f;
        public float HearingRangeGunshot = 100f;
        public float HearingRangeFootstep = 20f;
        public float MemoryDuration = 15f;
        public float CalloutRange = 50f;
        public float Aggression = 0.7f;
        public float ObjectiveFocus = 0.6f;
        public float ReactionTime = 0.2f;
        public float RunSpeed = 3.5f;
        public float StrafeInterval = 1.5f;
        public float AimSpeed = 8.0f;
        public float AimJitter = 0.5f;
        public float RecoilRecovery = 2.0f;
        public float BaseDamage = 15f;

        // --- 2. INTERNAL STATE ---
        internal BotState _currentState = BotState.Idle;
        internal Dictionary<Transform, MemoryRecord> _targetMemory = new Dictionary<Transform, MemoryRecord>();
        internal Transform _bestTarget;
        internal Vector3 _moveDestination;
        internal Vector3 _strafeDir;
        internal float _nextDecisionTime;
        internal float _nextStrafeTime;
        internal float _lastFireTime;
        internal int _botId;
        public string BotName = "Bot";
        public float Health = 100f; // Local Health Tracking

        // --- 3. COMPONENTS ---
        private Component _movementComponent;
        private Component _shootingComponent;
        private Transform _cameraTransform;
        
        // Reflection Hooks
        private MethodInfo _moveMethod;
        private MethodInfo _jumpMethod;
        private MethodInfo _fireMethod;
        
        // CRITICAL FIX: Add manual movement components
        private CharacterController _characterController;
        private Rigidbody _rigidbody;
        private bool _hasMovementComponent = false;
        
        // Logging Hook
        private InjectionTester _tester;

        void Awake()
        {
            _botId = gameObject.GetInstanceID(); 
            _tester = UnityEngine.Object.FindObjectOfType(typeof(InjectionTester)) as InjectionTester;
        }
        
        void Log(string msg) {
             if (_tester != null) _tester.Log("[" + BotName + "] " + msg);
             else Debug.Log("[BotController] " + msg);
        }

        public void Initialize()
        {
            string[] names = { "ShadowKiller", "AimBot", "NoobSlayer", "TGPIG", "Striker", "HeadHunter", "Bot_404" };
            BotName = names[UnityEngine.Random.Range(0, names.Length)] + "_" + UnityEngine.Random.Range(10, 99);
            gameObject.name = BotName; // Update Unity Object name
            
            // CRITICAL FIX #1: Enable AI immediately
            this.enabled = true;
            Log("initialized and AI ENABLED");
            
            // Announce Join (non-critical - catch exceptions)
            try {
                GameFacade.SendKillMessage(BotName, "joined", "the match");
            } catch (System.Exception ex) {
                Debug.LogWarning("[BotController] GameFacade.SendKillMessage failed: " + ex.Message);
            }

            Log("calling CacheGameComponents...");
            CacheGameComponents();
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) _cameraTransform = cam.transform;
            if (_cameraTransform == null) _cameraTransform = transform;
            
            _nextDecisionTime = Time.time + UnityEngine.Random.Range(0, ReactionTime);
            _currentState = BotState.Patrol;

            if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.RegisterBot(_botId, this);
            }

            // --- DAMAGE FORWARDING SETUP ---
            // Attach a forwarder to every single collider on this bot (Limbs, Head, etc.)
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.gameObject == gameObject) continue; // Skip self for now

                var forwarder = col.gameObject.AddComponent<DamageForwarder>();
                forwarder.TargetBot = this;
            }

            // Also attach to SELF (The SphereCollider)
            var selfForwarder = gameObject.AddComponent<DamageForwarder>();
            selfForwarder.TargetBot = this;
            
            // CRITICAL FIX: Set bot AND ALL CHILDREN (weapons, body parts) to Layer 8 (Player)
            // Layer 20 (RemotePlayer) might have physics disabled in the Collision Matrix!
            SetLayerRecursively(gameObject, 8); // Player Layer
            Log("set all children to Layer 8 (Player) for physics test");

            // FIX INVINCIBILITY: Ensure Projectiles (26) hit Players (8)
            Physics.IgnoreLayerCollision(26, 8, false);
            Physics.IgnoreLayerCollision(24, 8, false);
            
            // REMOVED DIAGNOSTIC: FPS Fix
            // gameObject.AddComponent<TriggerDiagnostic>();

            // FIX INVINCIBILITY: Attach CharacterHitArea if available
            try {
                System.Type hitAreaType = System.Type.GetType("CharacterHitArea, Assembly-CSharp");
                if ((object)hitAreaType != null) {
                    var hitArea = gameObject.GetComponent(hitAreaType);
                    if (hitArea == null) hitArea = gameObject.AddComponent(hitAreaType);
                    
                    // SMART CONFIGURATION: Copy values from LocalPlayer's HitArea
                    GameObject player = GameObject.Find("LocalPlayer");
                    if (player == null) player = GameObject.FindWithTag("Player");
                    
                    if (player != null) {
                        var playerHitArea = player.GetComponent(hitAreaType);
                        if ((object)playerHitArea != null) {
                            foreach (var field in hitAreaType.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                                try {
                                    object val = field.GetValue(playerHitArea);
                                    field.SetValue(hitArea, val);
                                } catch {}
                            }
                            Debug.Log("[BotController] Copied CharacterHitArea configuration from Player!");
                        }
                    }
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[BotController] Failed to attach CharacterHitArea: " + ex);
            }
            
            // CRITICAL FIX #5: Set initial patrol destination
            if (_moveDestination == Vector3.zero)
            {
                _moveDestination = transform.position + UnityEngine.Random.insideUnitSphere * 20f;
                _moveDestination.y = transform.position.y;
                Debug.Log("[BotController] Initial patrol destination set to " + _moveDestination);
            }
        }

        void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child == null) continue;
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        void CacheGameComponents()
        {
            try {
                Log("CacheGameComponents START");
                
                // CRITICAL FIX #2: Restore movement component detection with fallbacks
                
                // Try to find movement components
                _movementComponent = GetComponent("PlayerMovement");
                Log("Got PlayerMovement: " + (_movementComponent != null));
                
                if (_movementComponent == null) {
                    _movementComponent = GetComponent("CharacterController");
                    Log("Fallback CharacterController: " + (_movementComponent != null));
                }
                
                // Try Unity's built-in components as fallback
                _characterController = GetComponent<CharacterController>();
                Log("Got CharacterController: " + (_characterController != null));
                
                _rigidbody = GetComponent<Rigidbody>();
                Log("Got Rigidbody: " + (_rigidbody != null));
                
                // CRITICAL: Configure Rigidbody for manual movement control and TRIGGER detection
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = true; // Prevent Unity physics from interfering
                    _rigidbody.useGravity = false; // We handle gravity manually
                    _rigidbody.detectCollisions = true; // MUST be true for OnTriggerEnter
                    _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous; // Better detection
                    _rigidbody.WakeUp(); // Ensure it's active
                    Log("Configured Rigidbody as kinematic (collision detection enabled)");
                }
                else
                {
                    // Fallback: Add a Rigidbody if missing to enable OnTriggerEnter
                    _rigidbody = gameObject.AddComponent<Rigidbody>();
                    _rigidbody.isKinematic = true;
                    _rigidbody.useGravity = false;
                    _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    Log("Added kinematic Rigidbody for trigger detection");
                }
                
                if (_movementComponent != null || _characterController != null || _rigidbody != null)
                {
                    _hasMovementComponent = true;
                    Log("Found movement: " + (_movementComponent != null ? _movementComponent.GetType().Name : "NULL") + 
                              ", CharacterController: " + (_characterController != null) + ", Rigidbody: " + (_rigidbody != null));
                }
                else
                {
                    Log("No movement components found! Using manual position updates.");
                    _hasMovementComponent = false;
                }
                
                _shootingComponent = GetComponent("WeaponSystem");
                if (_shootingComponent == null) _shootingComponent = GetComponent("PlayerShooting");
                Log("Got ShootingComponent: " + (_shootingComponent != null));

                // Cache movement methods if found
                if (_movementComponent != null)
                {
                    var t = _movementComponent.GetType();
                    _moveMethod = t.GetMethod("Move", new[] { typeof(Vector3) });
                    _jumpMethod = t.GetMethod("Jump");
                    
                    // UNITY 3.5 FIX: Use (object) cast for MethodInfo null checks
                    Log("Move Method: " + ((object)_moveMethod != null ? _moveMethod.Name : "NULL") + 
                              ", Jump Method: " + ((object)_jumpMethod != null ? _jumpMethod.Name : "NULL"));
                }

                if (_shootingComponent != null)
                {
                    var t = _shootingComponent.GetType();
                    _fireMethod = t.GetMethod("Fire");
                    if ((object)_fireMethod == null) _fireMethod = t.GetMethod("Shoot");
                    // UNITY 3.5 FIX: Use (object) cast for MethodInfo null checks
                    Log("Shooting Component: " + _shootingComponent.GetType().Name + 
                              ", Fire Method: " + ((object)_fireMethod != null ? _fireMethod.Name : "NULL"));
                }
                
                Log("CacheGameComponents COMPLETE");
            } catch (System.Exception ex) {
                Log("CacheGameComponents CRASHED: " + ex.ToString());
            }
        }

        // --- 4. DAMAGE HANDLING ---
        public void ApplyDamage(float damage) { ReceiveDamage(damage); }
        public void TakeDamage(float damage) { ReceiveDamage(damage); }

        public void ReceiveDamage(float damage)
        {
             Log("💔 RECEIVED " + damage + " DAMAGE! Health: " + Health.ToString("F0") + " -> " + (Health - damage).ToString("F0"));
             
             Health -= damage;
             if (Health <= 0)
             {
                 Log("☠️ DEATH! Health depleted (" + Health.ToString("F0") + ")");
                 Die();
                 return;
             }

             if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.ApplyDamage(_botId, damage, transform.position);
            }
            
            StartCoroutine(FlashRed());
            
            // Enter combat/search mode when damaged
            if (_currentState != BotState.Combat)
            {
                Log("Entering Search mode due to damage");
                _currentState = BotState.Search; 
            }
        }

        public void TriggerHitEffects()
        {
            StartCoroutine(FlashRed());
        }

        public void Die() {
            Log(BotName + " died!");
            GameFacade.SendKillMessage("You", "pwned", BotName);
            Destroy(gameObject);
        }

        IEnumerator FlashRed()
        {
            Renderer[] rends = GetComponentsInChildren<Renderer>();
            foreach(var r in rends) {
                 if (r.material.HasProperty("_Color")) {
                    Color old = r.material.color;
                    r.material.color = Color.red;
                    yield return new WaitForSeconds(0.1f);
                    r.material.color = old;
                 }
            }
        }
        
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.gameObject.name.Contains("Projectile") || hit.gameObject.name.Contains("Rocket"))
            {
                 Debug.Log("[BotController] Hit by Projectile: " + hit.gameObject.name);
                 ReceiveDamage(50f);
            }
        }

        // CRITICAL FIX: Handle Jump Pads / Accelerators
        void OnTriggerEnter(Collider other)
        {
            string name = other.gameObject.name.ToLower();
            
            // Filter noise (Ignore known non-jump triggers)
            if (name.Contains("sound") || name.Contains("area") || name.Contains("zone") || name.Contains("door") || name.Contains("room")) return;

            // Log("OnTriggerEnter: " + other.gameObject.name + " (Tag: " + other.tag + ", Layer: " + other.gameObject.layer + ")");
            
            // BLIND GUESS: If it's not a sound/area, assume it's something interesting like a jump pad!
            // Check for jump pads, accelerators, or "antigravity", OR just activate on unknown triggers
            bool isSpecificJumpPad = name.Contains("accel") || name.Contains("jump") || name.Contains("pad") || 
                                     name.Contains("antigrav") || name.Contains("boost") || name.Contains("grenade");

            if (isSpecificJumpPad || true) // FORCE TRUE for now to test "Blind Jump"
            {
                // Debounce: Don't jump if we just jumped
                if (_isJumping) return;

                Log(">>> !!! BLIND JUMP ACTIVATED ON: " + other.gameObject.name + " !!! <<<");
                
                // Disable ground snapping temporarily
                _isJumping = true;
                Invoke("ResetJump", 2.0f); // Longer jump window

                // Attempt to trigger the pad's own logic if it has any
                other.gameObject.SendMessage("OnTriggerEnter", GetComponent<Collider>(), SendMessageOptions.DontRequireReceiver);

                // Manual Jump Logic
                if (_rigidbody != null)
                {
                    _rigidbody.velocity = Vector3.up * 28f + transform.forward * 18f; 
                }
                
                StartCoroutine(SimulateJumpPad());
            }
        }

        private bool _isJumping = false;
        void ResetJump() { _isJumping = false; }

        IEnumerator SimulateJumpPad()
        {
            float duration = 0.8f;
            float time = 0;
            Vector3 startPos = transform.position;
            Vector3 forward = transform.forward;
            
            while (time < duration)
            {
                // Parabolic arc
                float progress = time / duration;
                float height = Mathf.Sin(progress * Mathf.PI) * 10f; // Peak at 10m
                
                Vector3 move = forward * 15f * Time.deltaTime;
                move.y = (height - (Mathf.Sin((progress - Time.deltaTime/duration) * Mathf.PI) * 10f)); // Delta height
                
                // Apply
                if (_rigidbody != null) _rigidbody.MovePosition(transform.position + move);
                else transform.position += move;

                time += Time.deltaTime;
                yield return null;
            }
        }

        void Update()
        {
            // DEBUG: Log state and EXACT position periodically
            /*
            if (Time.frameCount % 300 == 0)
            {
                Log("State: " + _currentState + ", Pos: " + transform.position + ", Dest: " + _moveDestination);
            }
            */
            
            try
            {
                if (_currentState == BotState.Idle) return;

                // 1. Perception
                // if (Time.frameCount % 600 == 0) Log("Step 1: UpdatePerception");
                UpdatePerception();
                
                // 2. Decision
                // if (Time.frameCount % 600 == 0) Log("Step 2: UpdateDecision");
                if (Time.time > _nextDecisionTime)
                {
                    UpdateDecision();
                    _nextDecisionTime = Time.time + ReactionTime;
                }

                // 3. Execution
                // if (Time.frameCount % 600 == 0) Log("Step 3: ExecuteMovement");
                ExecuteMovement();
                
                // if (Time.frameCount % 600 == 0) Log("Step 4: ExecuteCombat");
                ExecuteCombat();
            }
            catch (System.Exception ex)
            {
                Log("CRASH in Update: " + ex.ToString());
            }
        }

        // ==================================================================================
        // PERCEPTION
        // ==================================================================================
        void UpdatePerception()
        {
            // .NET 2.0 COMPAT: Manual loop instead of LINQ
            var expired = new System.Collections.Generic.List<Transform>();
            foreach (var kvp in _targetMemory)
            {
                if (Time.time - kvp.Value.Timestamp > MemoryDuration)
                {
                    expired.Add(kvp.Key);
                }
            }
            foreach (var key in expired) _targetMemory.Remove(key);

            var potentialTargets = Physics.OverlapSphere(transform.position, ViewDistance); 
            foreach (var col in potentialTargets)
            {
                if (!IsEnemy(col)) continue;
                Transform target = col.transform;
                float dist = Vector3.Distance(transform.position, target.position);

                if (CheckVisibility(target, dist))
                {
                    UpdateMemory(target, target.position, true);
                    BroadcastContact(target.position);
                }
                else if (CheckAudio(target, dist))
                {
                    UpdateMemory(target, target.position, false);
                }
            }
        }

        bool IsEnemy(Collider col)
        {
            if (col.transform == transform) return false;
            if (col.GetComponent<BotController>() != null) return true;
            if (col.name == "LocalPlayer" || col.name == "GamePlayer") return true;
            try { if (col.CompareTag("Player")) return true; } catch {}
            return false;
        }

        bool CheckVisibility(Transform target, float distance)
        {
            if (distance > ViewDistance) return false;
            Vector3 dirToTarget = (target.position - _cameraTransform.position).normalized;
            if (Vector3.Angle(_cameraTransform.forward, dirToTarget) < ViewAngle / 2f)
            {
                RaycastHit hit;
                if (Physics.Raycast(_cameraTransform.position, dirToTarget, out hit, distance))
                {
                    return hit.transform == target || hit.transform.root == target.root;
                }
            }
            return false;
        }

        bool CheckAudio(Transform target, float distance)
        {
            if (distance < HearingRangeFootstep) return true;
            return false;
        }

        void UpdateMemory(Transform target, Vector3 pos, bool isVisual)
        {
            if (!_targetMemory.ContainsKey(target)) _targetMemory[target] = new MemoryRecord();
            _targetMemory[target].Position = pos;
            _targetMemory[target].Timestamp = Time.time;
            _targetMemory[target].IsVisual = isVisual;
        }

        public void ReceiveCallout(Vector3 enemyPos)
        {
            if (_currentState != BotState.Combat)
            {
                _currentState = BotState.Search;
                _moveDestination = enemyPos;
            }
        }

        void BroadcastContact(Vector3 pos)
        {
            var nearby = Physics.OverlapSphere(transform.position, CalloutRange);
            foreach (var col in nearby)
            {
                var bot = col.GetComponent<BotController>();
                if (bot != null && bot != this) bot.ReceiveCallout(pos);
            }
        }

        // ==================================================================================
        // DECISION
        // ==================================================================================
        void UpdateDecision()
        {
            _bestTarget = GetBestTarget();
            if (_bestTarget != null && _targetMemory.ContainsKey(_bestTarget))
            {
                var memory = _targetMemory[_bestTarget];
                bool isVisible = (Time.time - memory.Timestamp < 1.0f) && memory.IsVisual;

                if (isVisible)
                {
                    float health = LocalSimulationManager.Instance != null ? LocalSimulationManager.Instance.GetHealth(_botId) : 100f;
                    if (health < 40f && UnityEngine.Random.value > Aggression) _currentState = BotState.Flee;
                    else _currentState = BotState.Combat;
                }
                else
                {
                    _currentState = BotState.Search;
                    _moveDestination = memory.Position;
                }
            }
            else
            {
                if (_currentState == BotState.Combat || _currentState == BotState.Flee) _currentState = BotState.Patrol;
            }

            switch (_currentState)
            {
                case BotState.Combat:
                    if (Time.time > _nextStrafeTime)
                    {
                        _strafeDir = UnityEngine.Random.insideUnitSphere * 5f;
                        _nextStrafeTime = Time.time + StrafeInterval;
                    }
                    break;

                case BotState.Search:
                case BotState.Patrol:
                    if (Vector3.Distance(transform.position, _moveDestination) < 2f || _moveDestination == Vector3.zero)
                    {
                        _moveDestination = transform.position + UnityEngine.Random.insideUnitSphere * 20f;
                        _moveDestination.y = transform.position.y;
                        Log("Picked new patrol dest: " + _moveDestination);
                    }
                    break;
            }
        }

        Transform GetBestTarget()
        {
            Transform best = null;
            float closestTime = float.MinValue;
            foreach (var kvp in _targetMemory)
            {
                if (kvp.Value.Timestamp > closestTime)
                {
                    closestTime = kvp.Value.Timestamp;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // ==================================================================================
        // EXECUTION
        // ==================================================================================
        void ExecuteMovement()
        {
            Vector3 moveDir = Vector3.zero;

            // 1. Calculate Move Direction
            switch (_currentState)
            {
                case BotState.Combat:
                    if (_bestTarget != null)
                    {
                        Vector3 toTarget = (_bestTarget.position - transform.position).normalized;
                        Vector3 right = Vector3.Cross(toTarget, Vector3.up);
                        moveDir = (right * Mathf.Sin(Time.time * 3f)) + _strafeDir.normalized;
                        
                        float dist = Vector3.Distance(transform.position, _bestTarget.position);
                        if (dist < 5f) moveDir -= toTarget;
                        else if (dist > 20f) moveDir += toTarget;
                        
                        // Face Target
                        Vector3 lookDir = toTarget; lookDir.y = 0;
                        if(lookDir != Vector3.zero) {
                            transform.rotation = Quaternion.LookRotation(lookDir); 
                        }
                    }
                    break;

                case BotState.Search:
                case BotState.Patrol:
                case BotState.Flee:
                    if (_moveDestination != Vector3.zero) {
                        moveDir = (_moveDestination - transform.position).normalized;
                        // Face Move
                        Vector3 lookDir = moveDir; lookDir.y = 0;
                        if(lookDir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 5f);
                    }
                    break;
            }

            // CRITICAL FIX #4: Enhanced movement execution with multi-tier fallbacks
            // if (moveDir.magnitude > 0.1f && Time.frameCount % 600 == 0) Log("Moving: " + moveDir);

            // TIER 1: Try reflection-based movement first (Legacy)
            if ((object)_moveMethod != null && _movementComponent != null && moveDir.magnitude > 0.1f)
            {
                try
                {
                    _moveMethod.Invoke(_movementComponent, new object[] { moveDir * RunSpeed * Time.deltaTime });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[BotController] Reflection move failed: " + ex.Message);
                    _moveMethod = null; // Disable for future
                }
            }
            // TIER 2: Try CharacterController
            else if (_characterController != null && _characterController.enabled)
            {
                // Must add gravity manually for CharacterController.Move
                Vector3 velocity = moveDir * RunSpeed;
                velocity.y = -9.8f; // Simple gravity
                _characterController.Move(velocity * Time.deltaTime);
            }
            // TIER 3 & 4: Rigidbody or Transform (Fallback)
            else
            {
                // Calculate Target Position (Horizontal)
                Vector3 targetPos = transform.position;
                if (moveDir.magnitude > 0.1f) {
                    targetPos += moveDir * RunSpeed * Time.deltaTime;
                }

                // ALWAYS Apply Gravity/Ground Snap (Fixes Floating)
                ApplyGravityAndGroundSnap(ref targetPos);

                if (_rigidbody != null)
                {
                    _rigidbody.MovePosition(targetPos);
                }
                else
                {
                    transform.position = targetPos;
                }
            }
            
            // Simple Bot Avoidance (Anti-Clumping)
            foreach(var otherBot in FindObjectsOfType(typeof(BotController)) as BotController[]) {
                if (otherBot != this && Vector3.Distance(transform.position, otherBot.transform.position) < 1.0f) {
                    Vector3 push = (transform.position - otherBot.transform.position).normalized;
                    if (_rigidbody != null) _rigidbody.MovePosition(_rigidbody.position + push * 2.0f * Time.deltaTime);
                    else transform.position += push * 2.0f * Time.deltaTime;
                }
            }

            // Wall Check: Don't walk through walls
            if (moveDir != Vector3.zero) {
                // Fix: Ignore own layer (8) and IgnoreRaycast (2)
                int layerMask = ~((1 << 2) | (1 << 8) | (1 << 20)); 
                
                if (Physics.Raycast(transform.position + Vector3.up, moveDir, 1.0f, layerMask)) {
                     if(Time.frameCount % 60 == 0) Log("Movement blocked by wall!"); 
                     _moveDestination = Vector3.zero; // Pick new spot later
                }
            }
        }
        
        // CRITICAL FIX #6: Extracted gravity/ground handling to reusable method
        private void ApplyGravityAndGroundSnap(ref Vector3 targetPos)
        {
            if (_isJumping) return; // Skip gravity during jump pad usage

            RaycastHit hit;
            // Mask: Ignore Layer 2 (Self), Layer 8 (Player), and Layer 20
            int layerMask = ~((1 << 2) | (1 << 8) | (1 << 20)); 

            // Cast from target position downwards
            float raycastStartHeight = 2.0f; // Start 2 meters above target
            float maxGroundDistance = 50.0f;
            
            if (Physics.Raycast(targetPos + Vector3.up * raycastStartHeight, Vector3.down, out hit, maxGroundDistance, layerMask)) 
            {
                // CRITICAL FIX: MATERIAL DETECTION (The "Google Antigravity" Solution)
                // If triggers fail, maybe the floor MATERIAL is what launches us?
                if (hit.collider.renderer != null && hit.collider.renderer.sharedMaterial != null)
                {
                    string matName = hit.collider.renderer.sharedMaterial.name.ToLower();
                    if (matName.Contains("jump") || matName.Contains("bounce") || matName.Contains("antigrav") || matName.Contains("accel"))
                    {
                        if (!_isJumping)
                        {
                            Log(">>> DETECTED JUMP MATERIAL: " + matName + " <<<");
                            _isJumping = true;
                            Invoke("ResetJump", 2.0f);
                            if (_rigidbody != null) _rigidbody.velocity = Vector3.up * 28f + transform.forward * 18f;
                            StartCoroutine(SimulateJumpPad());
                            return; // Skip gravity
                        }
                    }
                }

                float groundY = hit.point.y;
                float heightAboveGround = targetPos.y - groundY;
                
                // CRITICAL FIX: Ensure bot stands at correct height (approx 1.05m for center pivot)
                // Previous logic caused bouncing because gravity threshold (0.5) was below target height (1.0)
                float desiredHeight = 1.05f; 
                float snapThreshold = 1.3f; // Tolerance to keep snapped while walking

                /*
                if (Time.frameCount % 600 == 0) 
                {
                    Log("GroundCheck: H=" + heightAboveGround.ToString("F2") + ", GroundY=" + groundY.ToString("F2") + ", DesiredY=" + (groundY + desiredHeight).ToString("F2"));
                }
                */

                // If we're within snap range (standing or small step), snap to target height
                if (heightAboveGround < snapThreshold)
                {
                    targetPos.y = groundY + desiredHeight; 
                }
                else if (heightAboveGround < 10f)
                {
                    //  Fall with gravity (normal)
                    targetPos.y -= 9.8f * Time.deltaTime;
                }
                else
                {
                    // Far above ground - fast fall
                    targetPos.y -= 20f * Time.deltaTime;
                }
            }
            else
            {
                // No ground found - apply gravity
                if(Time.frameCount % 60 == 0) Debug.Log("[BotController] No ground found in raycast!");
                targetPos.y -= 9.8f * Time.deltaTime * Time.deltaTime;
            }

            // Safety Net (Respawn if fell out of world)
            if (targetPos.y < -50f) {
                Debug.LogWarning("[BotController] " + BotName + " fell out of world! Respawning...");
                targetPos = Vector3.zero;
                targetPos.y = 10f;
            }
        }

        void ExecuteCombat()
        {
            // DIAGNOSTIC: Check entry conditions
            if (Time.frameCount % 120 == 0)
            {
                Log("ExecuteCombat: State=" + _currentState + ", HasTarget=" + (_bestTarget != null));
            }
            
            if (_currentState != BotState.Combat || _bestTarget == null)
            {
                if (Time.frameCount % 120 == 0 && _currentState == BotState.Combat)
                {
                    Log("ExecuteCombat: Combat state but no target!");
                }
                return;
            }

            Vector3 targetCenter = _bestTarget.position + Vector3.up * 1.5f;
            
            // FORCE AIMING
            Vector3 lookPos = _bestTarget.position;
            lookPos.y = transform.position.y; // Keep body level
            transform.LookAt(lookPos);
            
            // Aim Camera (Virtual)
            _cameraTransform.LookAt(targetCenter);

            Vector3 aimDir = (targetCenter - _cameraTransform.position).normalized;
            float aimAngle = Vector3.Angle(_cameraTransform.forward, aimDir);

            // DIAGNOSTIC: Log aiming status
            if (Time.frameCount % 120 == 0)
            {
                Log("Combat: AimAngle=" + aimAngle.ToString("F1") + "°, TimeSinceFire=" + (Time.time - _lastFireTime).ToString("F2") + "s");
            }

            if (aimAngle < 5f)
            {
                if (Time.time - _lastFireTime > 0.1f)
                {
                    if (UnityEngine.Random.value > 0.1f)
                    {
                        Log("ATTEMPTING TO FIRE WEAPON!");
                        FireWeapon();
                    }
                }
            }
        }

        void FireWeapon()
        {
            Log("🔫 FIRING WEAPON! Camera pos: " + _cameraTransform.position);
            
            _lastFireTime = Time.time; // Update fire time

            // Visual feedback (muzzle flash)
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.transform.position = _cameraTransform.position + _cameraTransform.forward * 0.5f;
            flash.transform.localScale = Vector3.one * 0.2f;
            flash.GetComponent<Renderer>().material.color = Color.yellow;
            Destroy(flash, 0.1f);

            // Perform raycast
            RaycastHit hit;
            bool didHit = Physics.Raycast(_cameraTransform.position, _cameraTransform.forward, out hit, 500f);
            
            Log("Raycast: Hit=" + didHit + ", Distance=" + hit.distance.ToString("F1"));
            
            if (didHit)
            {
                Log("Hit: " + hit.collider.name + ", Tag: " + hit.collider.tag + ", Layer: " + hit.collider.gameObject.layer);
                
                // Check if hit another bot
                var targetBot = hit.collider.GetComponent<BotController>();
                if (targetBot != null)
                {
                    Log("💥 HIT BOT: " + targetBot.BotName + " for " + BaseDamage + " damage!");
                    targetBot.ReceiveDamage(BaseDamage);
                    return;
                }

                // Check if hit player
                Transform root = hit.transform.root;
                bool isPlayer = root.name == "LocalPlayer" || root.name == "GamePlayer" || root.CompareTag("Player") ||
                                hit.collider.name == "LocalPlayer" || hit.collider.CompareTag("Player");
                
                if (isPlayer)
                {
                    Log("💥 HIT PLAYER for " + BaseDamage + " damage!");
                    DamageLocalPlayer(BaseDamage);
                }
                else
                {
                    Log("Hit object: " + hit.collider.name + " (not a target)");
                }
            }
            else
            {
                Log("Raycast MISS - no hit detected");
            }
        }

        void DamageLocalPlayer(float damage)
        {
            try {
                GameObject player = GameObject.Find("LocalPlayer");
                if (player == null) player = GameObject.Find("GamePlayer");

                if (player != null) {
                    // FALLBACK 1: Unity SendMessage (Broadest attempt)
                    player.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
                    player.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

                    foreach (var comp in player.GetComponents<Component>()) {
                        if (comp == null) continue;
                        string[] methods = { "ApplyDamage", "TakeDamage", "OnDamage", "RegisterHit", "SetDamage" };
                        foreach (var mName in methods) {
                            MethodInfo dmgMethod = comp.GetType().GetMethod(mName, new[] { typeof(float) });
                            if ((object)dmgMethod != null) {
                                dmgMethod.Invoke(comp, new object[] { damage });
                                Debug.Log("[BotController] HIT PLAYER via " + mName + " on " + comp.GetType().Name);
                                return;
                            }
                        }
                    }

                    try {
                        System.Type gsType = System.Type.GetType("GameState, Assembly-CSharp");
                        if ((object)gsType != null) {
                            PropertyInfo currentProp = gsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                            object gameState = currentProp.GetValue(null, null);
                            if (gameState != null) {
                                object playerData = null;
                                PropertyInfo pdProp = gameState.GetType().GetProperty("PlayerData");
                                // UNITY 3.5 FIX: Use (object) cast for PropertyInfo null checks
                                if ((object)pdProp == null) pdProp = gameState.GetType().GetProperty("LocalPlayer");
                                if ((object)pdProp != null) playerData = pdProp.GetValue(gameState, null);

                                if (playerData != null) {
                                    PropertyInfo healthProp = playerData.GetType().GetProperty("Health");
                                    // UNITY 3.5 FIX: Use (object) cast for PropertyInfo null checks
                                    if ((object)healthProp != null) {
                                        int currentHp = (int)healthProp.GetValue(playerData, null);
                                        int newHp = currentHp - (int)damage;
                                        healthProp.SetValue(playerData, newHp, null);
                                        
                                        // Check Kill
                                        if (newHp <= 0) {
                                            GameFacade.SendKillMessage(BotName, "pwned", "You");
                                        }

                                        Debug.Log("[BotController] Decreased HP via GameState.PlayerData!");
                                        return;
                                    }
                                }
                            }
                        }
                    } catch {}

                    var lp = player.GetComponent("LocalPlayer");
                    if (lp != null) {
                        MethodInfo kill = lp.GetType().GetMethod("SetPlayerDead");
                        if ((object)kill != null) {
                            try { kill.Invoke(lp, null); } catch {}
                        }
                    }
                }
            } catch (System.Exception ex) { }
        }
    }
}