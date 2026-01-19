using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

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

        void Awake()
        {
            _botId = gameObject.GetInstanceID(); 
        }

        public void Initialize()
        {
            string[] names = { "ShadowKiller", "AimBot", "NoobSlayer", "TGPIG", "Striker", "HeadHunter", "Bot_404" };
            BotName = names[UnityEngine.Random.Range(0, names.Length)] + "_" + UnityEngine.Random.Range(10, 99);
            gameObject.name = BotName; // Update Unity Object name
            
            // CRITICAL FIX #1: Enable AI immediately
            this.enabled = true;
            Debug.Log("[BotController] " + BotName + " initialized and AI ENABLED");
            
            // Announce Join
            GameFacade.SendKillMessage(BotName, "joined", "the match");

            CacheGameComponents();
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) _cameraTransform = cam.transform;
            if (_cameraTransform == null) _cameraTransform = transform;
            
            _nextDecisionTime = Time.time + UnityEngine.Random.Range(0, ReactionTime);
            _currentState = BotState.Patrol;

            if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.RegisterBot(_botId, gameObject);
            }

            // --- DAMAGE FORWARDING SETUP ---
            // Attach a forwarder to every single collider on this bot (Limbs, Head, etc.)
            foreach (var col in GetComponentsInChildren<Collider>())
            {
                if (col.gameObject == gameObject) continue; // Skip self for now

                var forwarder = col.gameObject.AddComponent<DamageForwarder>();
                forwarder.TargetBot = this;
                col.gameObject.layer = 20; // RemotePlayer
            }

            // Also attach to SELF (The SphereCollider)
            var selfForwarder = gameObject.AddComponent<DamageForwarder>();
            selfForwarder.TargetBot = this;

            // FIX INVINCIBILITY: Ensure Projectiles (26) hit RemotePlayers (20)
            Physics.IgnoreLayerCollision(26, 20, false);
            Physics.IgnoreLayerCollision(24, 20, false);

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

        void CacheGameComponents()
        {
            // CRITICAL FIX #2: Restore movement component detection with fallbacks
            Debug.Log("[BotController] Caching game components...");
            
            // Try to find movement components
            _movementComponent = GetComponent("PlayerMovement");
            if (_movementComponent == null) _movementComponent = GetComponent("CharacterController");
            
            // Try Unity's built-in components as fallback
            _characterController = GetComponent<CharacterController>();
            _rigidbody = GetComponent<Rigidbody>();
            
            if (_movementComponent != null || _characterController != null || _rigidbody != null)
            {
                _hasMovementComponent = true;
                Debug.Log("[BotController] Found movement: " + (_movementComponent != null ? _movementComponent.GetType().Name : "NULL") + 
                          ", CharacterController: " + (_characterController != null) + ", Rigidbody: " + (_rigidbody != null));
            }
            else
            {
                Debug.LogWarning("[BotController] No movement components found! Using manual position updates.");
                _hasMovementComponent = false;
            }
            
            _shootingComponent = GetComponent("WeaponSystem");
            if (_shootingComponent == null) _shootingComponent = GetComponent("PlayerShooting");

            // Cache movement methods if found
            if (_movementComponent != null)
            {
                var t = _movementComponent.GetType();
                _moveMethod = t.GetMethod("Move", new[] { typeof(Vector3) });
                _jumpMethod = t.GetMethod("Jump");
                
                // UNITY 3.5 FIX: Use (object) cast for MethodInfo null checks
                Debug.Log("[BotController] Move Method: " + ((object)_moveMethod != null ? _moveMethod.Name : "NULL") + 
                          ", Jump Method: " + ((object)_jumpMethod != null ? _jumpMethod.Name : "NULL"));
            }

            if (_shootingComponent != null)
            {
                var t = _shootingComponent.GetType();
                _fireMethod = t.GetMethod("Fire");
                if ((object)_fireMethod == null) _fireMethod = t.GetMethod("Shoot");
                // UNITY 3.5 FIX: Use (object) cast for MethodInfo null checks
                Debug.Log("[BotController] Shooting Component: " + _shootingComponent.GetType().Name + 
                          ", Fire Method: " + ((object)_fireMethod != null ? _fireMethod.Name : "NULL"));
            }
            else
            {
                Debug.LogWarning("[BotController] No shooting component found!");
            }
        }

        // --- 4. DAMAGE HANDLING ---
        public void ApplyDamage(float damage) { ReceiveDamage(damage); }
        public void TakeDamage(float damage) { ReceiveDamage(damage); }

        public void ReceiveDamage(float damage)
        {
             Debug.Log("[BotController] ReceiveDamage called: " + damage);
             
             Health -= damage;
             if (Health <= 0) {
                 Die();
                 return;
             }

             if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.ApplyDamage(_botId, damage, transform.position);
            }
            
            StartCoroutine(FlashRed());
            
            if (_currentState != BotState.Combat)
            {
                _currentState = BotState.Search; 
            }
        }

        void Die() {
            Debug.Log(BotName + " died!");
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

        void Update()
        {
            try
            {
                // Enhanced debugging - log state changes
                if (Time.frameCount % 30 == 0) 
                {
                    Debug.Log("[BotController] " + BotName + " Update - State: " + _currentState + 
                              ", Health: " + Health + ", Target: " + (_bestTarget != null));
                }

                if (_currentState == BotState.Idle) return;

                // 1. Perception
                UpdatePerception();
                
                // 2. Decision
                if (Time.time > _nextDecisionTime)
                {
                    UpdateDecision();
                    _nextDecisionTime = Time.time + ReactionTime;
                }

                // 3. Execution
                ExecuteMovement();
                ExecuteCombat();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[BotController] CRASH in Update: " + ex.ToString());
            }
        }

        // ==================================================================================
        // PERCEPTION
        // ==================================================================================
        void UpdatePerception()
        {
            var expired = _targetMemory.Where(kvp => Time.time - kvp.Value.Timestamp > MemoryDuration).ToList();
            foreach (var ex in expired) _targetMemory.Remove(ex.Key);

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
                            // Force immediate rotation for testing
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
            if (moveDir.magnitude > 0.1f)
            {
                if (Time.frameCount % 120 == 0) Debug.Log("[BotController] Moving with direction: " + moveDir);
                
                // TIER 1: Try reflection-based movement first
                if ((object)_moveMethod != null && _movementComponent != null)
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
                    _characterController.Move(moveDir * RunSpeed * Time.deltaTime);
                }
                // TIER 3: Try Rigidbody
                else if (_rigidbody != null)
                {
                    _rigidbody.MovePosition(transform.position + moveDir * RunSpeed * Time.deltaTime);
                }
                // TIER 4: Fallback - Direct position update with gravity
                else
                {
                    Vector3 currentPos = transform.position;
                    Vector3 targetPos = currentPos + (moveDir * RunSpeed * Time.deltaTime);
                    
                    // Apply gravity and ground snapping
                    ApplyGravityAndGroundSnap(ref targetPos);
                    
                    transform.position = targetPos;
                }
            }
            
            // Simple Bot Avoidance (Anti-Clumping)
            foreach(var otherBot in FindObjectsOfType(typeof(BotController)) as BotController[]) {
                if (otherBot != this && Vector3.Distance(transform.position, otherBot.transform.position) < 1.0f) {
                    Vector3 push = (transform.position - otherBot.transform.position).normalized;
                    moveDir += push * 2.0f; // Push away
                }
            }

            // Wall Check: Don't walk through walls
            if (moveDir != Vector3.zero) {
                if (Physics.Raycast(transform.position + Vector3.up, moveDir, 1.0f)) {
                     moveDir = Vector3.zero; // Stop if hitting wall
                     _moveDestination = Vector3.zero; // Pick new spot later
                }
            }
        }
        
        // CRITICAL FIX #6: Extracted gravity/ground handling to reusable method
        private void ApplyGravityAndGroundSnap(ref Vector3 targetPos)
        {
            RaycastHit hit;
            // Mask: Ignore Layer 2 (Self) and Layer 20 (Body Parts)
            int layerMask = ~((1 << 2) | (1 << 20)); 

            // Cast from target position downwards
            float raycastStartHeight = 2.0f; // Start 2 meters above target
            float maxGroundDistance = 50.0f;
            
            if (Physics.Raycast(targetPos + Vector3.up * raycastStartHeight, Vector3.down, out hit, maxGroundDistance, layerMask)) 
            {
                float groundY = hit.point.y;
                float heightAboveGround = targetPos.y - groundY;
                
                // If we're reasonably close to ground, snap to it
                if (heightAboveGround < 0.5f)
                {
                    targetPos.y = groundY + 0.1f; // Small offset to prevent sinking
                }
                else if (heightAboveGround > 0.5f && heightAboveGround < 10f)
                {
                    //  Fall with gravity
                    targetPos.y -= 9.8f * Time.deltaTime * Time.deltaTime;
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
            if (_currentState != BotState.Combat || _bestTarget == null) return;

            Vector3 targetCenter = _bestTarget.position + Vector3.up * 1.5f;
            
            // FORCE AIMING
            Vector3 lookPos = _bestTarget.position;
            lookPos.y = transform.position.y; // Keep body level
            transform.LookAt(lookPos);
            
            // Aim Camera (Virtual)
            _cameraTransform.LookAt(targetCenter);

            Vector3 aimDir = (targetCenter - _cameraTransform.position).normalized;

            if (Vector3.Angle(_cameraTransform.forward, aimDir) < 5f)
            {
                if (Time.time - _lastFireTime > 0.1f)
                {
                    if (UnityEngine.Random.value > 0.1f) FireWeapon();
                }
            }
        }

        void FireWeapon()
        {
            /* REMOVED DEBUG FLASH
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.transform.position = _cameraTransform.position + _cameraTransform.forward * 0.5f;
            flash.transform.localScale = Vector3.one * 0.1f;
            flash.GetComponent<Renderer>().material.color = Color.red;
            Destroy(flash, 0.05f);
            */

            if (LocalSimulationManager.Instance != null)
            {
                RaycastHit hit;
                if (Physics.Raycast(_cameraTransform.position, _cameraTransform.forward, out hit, 500f))
                {
                    var targetBot = hit.collider.GetComponent<BotController>();
                    if (targetBot != null)
                    {
                        LocalSimulationManager.Instance.ApplyDamage(targetBot._botId, BaseDamage, hit.point);
                        return;
                    }

                    // Fix: Check ROOT for player identity (handles hitting limbs/children)
                    Transform root = hit.transform.root;
                    if (root.name == "LocalPlayer" || root.name == "GamePlayer" || root.CompareTag("Player") ||
                        hit.collider.name == "LocalPlayer" || hit.collider.CompareTag("Player"))
                    {
                        DamageLocalPlayer(BaseDamage);
                        _lastFireTime = Time.time;
                    }
                }
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