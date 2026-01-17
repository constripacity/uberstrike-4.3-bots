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
        public float RunSpeed = 6.0f;
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
            _botId = gameObject.GetInstanceID(); 
        }

        public void Initialize()
        {
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
                if (col.gameObject == gameObject) continue; // Skip self

                var forwarder = col.gameObject.AddComponent<DamageForwarder>();
                forwarder.TargetBot = this;
                col.gameObject.layer = 20; // RemotePlayer
            }
        }

        void CacheGameComponents()
        {
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
            if (PracticeModeDetector.Instance != null && !PracticeModeDetector.Instance.IsPracticeMode) return; 

            UpdatePerception();

            if (Time.time >= _nextDecisionTime)
            {
                UpdateDecision();
                _nextDecisionTime = Time.time + ReactionTime;
            }

            ExecuteMovement();
            ExecuteCombat();
        }

        void LateUpdate()
        {
            // --- ANIMATION SYNC FIX ---
            try {
                Component decorator = GetComponentInChildren(typeof(MonoBehaviour)); 
                if (decorator != null && decorator.GetType().Name == "AvatarDecorator")
                {
                    MethodInfo setPos = decorator.GetType().GetMethod("SetPosition");
                    if (setPos != null)
                    {
                        setPos.Invoke(decorator, new object[] { transform.position, transform.rotation });
                    }
                }
            } catch {}
        }

        // --- 4. DAMAGE HANDLING ---
        public void ApplyDamage(float damage) { ReceiveDamage(damage); }
        public void TakeDamage(float damage) { ReceiveDamage(damage); }

        public void ReceiveDamage(float damage)
        {
             Debug.Log("[BotController] ReceiveDamage called: " + damage);
             if (LocalSimulationManager.Instance != null)
            {
                LocalSimulationManager.Instance.ApplyDamage(_botId, damage, transform.position);
                StartCoroutine(FlashRed());
                
                if (_currentState != BotState.Combat)
                {
                    _currentState = BotState.Search; 
                }
            }
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
            if (_movementComponent == null) return;
            Vector3 finalMove = Vector3.zero;

            switch (_currentState)
            {
                case BotState.Combat:
                    if (_bestTarget != null)
                    {
                        Vector3 toTarget = (_bestTarget.position - transform.position).normalized;
                        Vector3 right = Vector3.Cross(toTarget, Vector3.up);
                        finalMove = (right * Mathf.Sin(Time.time * 3f)) + _strafeDir.normalized;
                        if (Vector3.Distance(transform.position, _bestTarget.position) < 5f) finalMove -= toTarget;
                        else if (Vector3.Distance(transform.position, _bestTarget.position) > 20f) finalMove += toTarget;
                    }
                    break;

                case BotState.Search:
                case BotState.Patrol:
                case BotState.Flee:
                    finalMove = (_moveDestination - transform.position).normalized;
                    break;
            }

            if (_moveMethod != null) _moveMethod.Invoke(_movementComponent, new object[] { finalMove });
            else
            {
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.SimpleMove(finalMove * RunSpeed);
            }
        }

        void ExecuteCombat()
        {
            if (_currentState != BotState.Combat || _bestTarget == null) return;

            Vector3 targetCenter = _bestTarget.position + Vector3.up * 1.5f;
            float jitterX = UnityEngine.Random.Range(-AimJitter, AimJitter);
            float jitterY = UnityEngine.Random.Range(-AimJitter, AimJitter);
            Vector3 jitterVec = new Vector3(jitterX, jitterY, 0);

            Vector3 aimDir = (targetCenter - _cameraTransform.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(aimDir);
            
            _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, targetRot * Quaternion.Euler(jitterVec), Time.deltaTime * AimSpeed);
            Vector3 bodyDir = aimDir;
            bodyDir.y = 0;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(bodyDir), Time.deltaTime * AimSpeed);

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
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.transform.position = _cameraTransform.position + _cameraTransform.forward * 0.5f;
            flash.transform.localScale = Vector3.one * 0.1f;
            flash.GetComponent<Renderer>().material.color = Color.red;
            Destroy(flash, 0.05f);

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

                    if (hit.collider.name == "LocalPlayer" || hit.collider.name == "GamePlayer" || hit.collider.CompareTag("Player"))
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
                    foreach (var comp in player.GetComponents<Component>()) {
                        if (comp == null) continue;
                        string[] methods = { "ApplyDamage", "TakeDamage", "OnDamage", "RegisterHit", "SetDamage" };
                        foreach (var mName in methods) {
                            MethodInfo dmgMethod = comp.GetType().GetMethod(mName, new[] { typeof(float) });
                            if (dmgMethod != null) {
                                dmgMethod.Invoke(comp, new object[] { damage });
                                Debug.Log("[BotController] HIT PLAYER via " + mName + " on " + comp.GetType().Name);
                                return;
                            }
                        }
                    }

                    try {
                        System.Type gsType = System.Type.GetType("GameState, Assembly-CSharp");
                        if (gsType != null) {
                            PropertyInfo currentProp = gsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                            object gameState = currentProp.GetValue(null, null);
                            if (gameState != null) {
                                object playerData = null;
                                PropertyInfo pdProp = gameState.GetType().GetProperty("PlayerData");
                                if (pdProp == null) pdProp = gameState.GetType().GetProperty("LocalPlayer");
                                if (pdProp != null) playerData = pdProp.GetValue(gameState, null);

                                if (playerData != null) {
                                    PropertyInfo healthProp = playerData.GetType().GetProperty("Health");
                                    if (healthProp != null) {
                                        int currentHp = (int)healthProp.GetValue(playerData, null);
                                        healthProp.SetValue(playerData, currentHp - (int)damage, null);
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
                        if (kill != null) {
                            try { kill.Invoke(lp, null); } catch {}
                        }
                    }
                }
            } catch (System.Exception ex) { }
        }
    }
}