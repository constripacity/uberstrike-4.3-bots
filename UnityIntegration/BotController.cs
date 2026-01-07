using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace UberStrikeBots
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
    /// Advanced Bot Controller with Perception, Decision, and Execution layers.
    /// Replaces the human input controller while driving existing game components.
    /// Enhanced for Offline Practice Mode.
    /// </summary>
    public class BotController : MonoBehaviour
    {
        // --- 1. CONFIGURATION & TUNING ---
        [Header("Perception")]
        public float ViewAngle = 120f;
        public float ViewDistance = 100f;
        public float HearingRangeGunshot = 100f;
        public float HearingRangeFootstep = 20f;
        public float MemoryDuration = 15f;
        public float CalloutRange = 50f;

        [Header("Personality")]
        [Range(0, 1)] public float Aggression = 0.7f; // 0.7 = 70% chance to engage vs cover
        [Range(0, 1)] public float ObjectiveFocus = 0.6f;
        public float ReactionTime = 0.2f;

        [Header("Movement")]
        public float RunSpeed = 6.0f;
        public float StrafeInterval = 1.5f;

        [Header("Aiming")]
        public float AimSpeed = 8.0f;
        public float AimJitter = 0.5f;
        public float RecoilRecovery = 2.0f;
        public float BaseDamage = 15f; // For offline simulation

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

        // --- 3. COMPONENTS ---
        private Component _movementComponent;
        private Component _shootingComponent;
        private Transform _cameraTransform;
        
        // Reflection Hooks
        private MethodInfo _moveMethod;
        private MethodInfo _jumpMethod;
        private MethodInfo _fireMethod;

        void Awake()
        {
            _botId = gameObject.GetInstanceID(); // Simple ID for local sim
        }

        public void Initialize()
        {
            CacheGameComponents();
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) _cameraTransform = cam.transform;
            if (_cameraTransform == null) _cameraTransform = transform;
            
            _nextDecisionTime = Time.time + UnityEngine.Random.Range(0, ReactionTime);
            _currentState = BotState.Patrol;

            // Register with Local Simulation
            if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.RegisterBot(_botId, gameObject);
            }
        }

        void CacheGameComponents()
        {
            // Try to find common UberStrike movement scripts
            _movementComponent = GetComponent("PlayerMovement");
            if (_movementComponent == null) _movementComponent = GetComponent("CharacterController");

            _shootingComponent = GetComponent("WeaponSystem");
            if (_shootingComponent == null) _shootingComponent = GetComponent("PlayerShooting");

            if (_movementComponent != null)
            {
                var t = _movementComponent.GetType();
                _moveMethod = t.GetMethod("Move", new[] { typeof(Vector3) });
                _jumpMethod = t.GetMethod("Jump");
            }

            if (_shootingComponent != null)
            {
                var t = _shootingComponent.GetType();
                _fireMethod = t.GetMethod("Fire");
                if (_fireMethod == null) _fireMethod = t.GetMethod("Shoot");
            }
        }

        void Update()
        {
            // SAFETY CHECK: Only run if we are in practice mode or offline
            if (PracticeModeDetector.Instance != null && !PracticeModeDetector.Instance.IsPracticeMode)
            {
                return; 
            }

            // 1. Perception Layer: Update what we know
            UpdatePerception();

            // 2. Decision Layer: Decide what to do
            if (Time.time >= _nextDecisionTime)
            {
                UpdateDecision();
                _nextDecisionTime = Time.time + ReactionTime;
            }

            // 3. Execution Layer: Do it
            ExecuteMovement();
            ExecuteCombat();
        }

        // ==================================================================================
        // LAYER 1: PERCEPTION
        // ==================================================================================
        void UpdatePerception()
        {
            // Prune old memories
            var expired = _targetMemory.Where(kvp => Time.time - kvp.Value.Timestamp > MemoryDuration).ToList();
            foreach (var ex in expired) _targetMemory.Remove(ex.Key);

            // Scan for enemies
            // In a real hook, we'd use a more specific layer mask or list
            var potentialTargets = Physics.OverlapSphere(transform.position, ViewDistance); 
            
            foreach (var col in potentialTargets)
            {
                if (!IsEnemy(col)) continue;
                Transform target = col.transform;
                float dist = Vector3.Distance(transform.position, target.position);

                // A. Visual Check
                if (CheckVisibility(target, dist))
                {
                    UpdateMemory(target, target.position, true);
                    BroadcastContact(target.position); // Callout
                }
                // B. Audio Check (Simulation)
                else if (CheckAudio(target, dist))
                {
                    UpdateMemory(target, target.position, false);
                }
            }
        }

        bool IsEnemy(Collider col)
        {
            // Updated to be more generic for practice mode
            // Any player or bot that isn't ME is an enemy in Free For All
            if (col.transform == transform) return false;
            
            // Check if it has a bot controller or is the local player
            if (col.GetComponent<BotController>() != null) return true;
            if (col.CompareTag("Player") || col.CompareTag("LocalPlayer")) return true;

            return false;
        }

        bool CheckVisibility(Transform target, float distance)
        {
            if (distance > ViewDistance) return false;

            Vector3 dirToTarget = (target.position - _cameraTransform.position).normalized;
            float angle = Vector3.Angle(_cameraTransform.forward, dirToTarget);

            if (angle < ViewAngle / 2f)
            {
                RaycastHit hit;
                // Raycast to chest height roughly
                if (Physics.Raycast(_cameraTransform.position, dirToTarget, out hit, distance))
                {
                    return hit.transform == target || hit.transform.root == target.root;
                }
            }
            return false;
        }

        bool CheckAudio(Transform target, float distance)
        {
            // Simplified Audio Simulation
            if (distance < HearingRangeFootstep) return true; // Hear footsteps
            return false;
        }

        void UpdateMemory(Transform target, Vector3 pos, bool isVisual)
        {
            if (!_targetMemory.ContainsKey(target))
            {
                _targetMemory[target] = new MemoryRecord();
            }
            _targetMemory[target].Position = pos;
            _targetMemory[target].Timestamp = Time.time;
            _targetMemory[target].IsVisual = isVisual;
        }

        public void ReceiveCallout(Vector3 enemyPos)
        {
            // Received intel from a teammate (or cheat/global shared knowledge in practice)
            if (_currentState != BotState.Combat)
            {
                _currentState = BotState.Search;
                _moveDestination = enemyPos;
            }
        }

        void BroadcastContact(Vector3 pos)
        {
            // Simulate radio/voice callout to nearby bots
            var nearby = Physics.OverlapSphere(transform.position, CalloutRange);
            foreach (var col in nearby)
            {
                var bot = col.GetComponent<BotController>();
                if (bot != null && bot != this)
                {
                    bot.ReceiveCallout(pos);
                }
            }
        }

        // ==================================================================================
        // LAYER 2: DECISION
        // ==================================================================================
        void UpdateDecision()
        {
            // Evaluate best target from memory
            _bestTarget = GetBestTarget();

            // Behavior Tree / State Machine Hybrid
            if (_bestTarget != null && _targetMemory.ContainsKey(_bestTarget))
            {
                var memory = _targetMemory[_bestTarget];
                bool isVisible = (Time.time - memory.Timestamp < 1.0f) && memory.IsVisual; // Fresh visual

                if (isVisible)
                {
                    // Combat Logic
                    float health = LocalSimulationManager.Instance != null ? LocalSimulationManager.Instance.GetHealth(_botId) : 100f;
                    
                    if (health < 40f && UnityEngine.Random.value > Aggression)
                    {
                        _currentState = BotState.Flee;
                    }
                    else
                    {
                        _currentState = BotState.Combat;
                    }
                }
                else
                {
                    // Search / Hunt
                    _currentState = BotState.Search;
                    _moveDestination = memory.Position;
                }
            }
            else
            {
                if (_currentState == BotState.Combat || _currentState == BotState.Flee)
                {
                    _currentState = BotState.Patrol;
                }
            }

            // Execute State Logic
            switch (_currentState)
            {
                case BotState.Combat:
                    // Strafing update
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
                        // Pick random patrol point
                        _moveDestination = transform.position + UnityEngine.Random.insideUnitSphere * 20f;
                        _moveDestination.y = transform.position.y; // Keep level for now
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
                // Prefer most recent contacts
                if (kvp.Value.Timestamp > closestTime)
                {
                    closestTime = kvp.Value.Timestamp;
                    best = kvp.Key;
                }
            }
            return best;
        }

        // ==================================================================================
        // LAYER 3: EXECUTION
        // ==================================================================================
        void ExecuteMovement()
        {
            if (_movementComponent == null) return;

            Vector3 finalMove = Vector3.zero;

            switch (_currentState)
            {
                case BotState.Combat:
                    // Strafe logic: Move perpendicular to target + some randomness
                    if (_bestTarget != null)
                    {
                        Vector3 toTarget = (_bestTarget.position - transform.position).normalized;
                        Vector3 right = Vector3.Cross(toTarget, Vector3.up);
                        
                        // Mix of strafe and closing/maintaining distance
                        finalMove = (right * Mathf.Sin(Time.time * 3f)) + _strafeDir.normalized;
                        
                        // Don't get too close
                        if (Vector3.Distance(transform.position, _bestTarget.position) < 5f)
                            finalMove -= toTarget;
                        else if (Vector3.Distance(transform.position, _bestTarget.position) > 20f)
                            finalMove += toTarget;
                    }
                    break;

                case BotState.Search:
                case BotState.Patrol:
                case BotState.Flee:
                    // Simple pathfinding (direct line for now, NavMesh later)
                    finalMove = (_moveDestination - transform.position).normalized;
                    break;
            }

            // Apply Move
            if (_moveMethod != null)
                _moveMethod.Invoke(_movementComponent, new object[] { finalMove });
            else
            {
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.SimpleMove(finalMove * RunSpeed);
            }
        }

        void ExecuteCombat()
        {
            if (_currentState != BotState.Combat || _bestTarget == null) return;

            // 1. Aim Smoothing & Jitter
            Vector3 targetCenter = _bestTarget.position + Vector3.up * 1.5f;
            
            // Apply Human Jitter (Fatigue/Recoil)
            float jitterX = UnityEngine.Random.Range(-AimJitter, AimJitter);
            float jitterY = UnityEngine.Random.Range(-AimJitter, AimJitter);
            Vector3 jitterVec = new Vector3(jitterX, jitterY, 0);

            Vector3 aimDir = (targetCenter - _cameraTransform.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(aimDir);
            
            // Smooth Rotation
            _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, targetRot * Quaternion.Euler(jitterVec), Time.deltaTime * AimSpeed);
            
            // Body Rotation (Y-axis only)
            Vector3 bodyDir = aimDir;
            bodyDir.y = 0;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(bodyDir), Time.deltaTime * AimSpeed);

            // 2. Burst Fire Logic
            if (Vector3.Angle(_cameraTransform.forward, aimDir) < 5f)
            {
                if (Time.time - _lastFireTime > 0.1f) // Fire rate cap
                {
                     // Random burst control
                    if (UnityEngine.Random.value > 0.1f) // 90% chance to continue burst
                    {
                        FireWeapon();
                    }
                }
            }
        }

        void FireWeapon()
        {
            // Visuals
            if (_fireMethod != null && _shootingComponent != null)
            {
                _fireMethod.Invoke(_shootingComponent, null);
                _lastFireTime = Time.time;
            }

            // Logic (Offline Hit Detection)
            if (LocalSimulationManager.Instance != null)
            {
                RaycastHit hit;
                if (Physics.Raycast(_cameraTransform.position, _cameraTransform.forward, out hit, 500f))
                {
                    // Check if we hit another bot or player
                    // In a real scenario, we'd check components or tags
                    var targetBot = hit.collider.GetComponent<BotController>();
                    if (targetBot != null)
                    {
                        LocalSimulationManager.Instance.ApplyDamage(targetBot._botId, BaseDamage, hit.point);
                    }
                    // TODO: Handle Player damage if local player
                }
            }
        }
    }
}
