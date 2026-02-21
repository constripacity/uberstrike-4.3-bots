using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UberStrike.Realtime.Common;

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
    /// UberStrike uses CharacterHitArea.ApplyDamage(DamageInfo) - we intercept via reflection.
    /// </summary>
    public class DamageForwarder : MonoBehaviour
    {
        public BotController TargetBot;
        public string BodyPartName = "Body";

        private static System.Type _damageInfoType;
        private static PropertyInfo _damageProperty;
        private static PropertyInfo _bodyPartProperty;
        private static bool _reflectionInitialized = false;

        void Awake()
        {
            InitializeReflection();
        }

        static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            _reflectionInitialized = true;

            try {
                _damageInfoType = System.Type.GetType("DamageInfo, Assembly-CSharp");
                if ((object)_damageInfoType != null) {
                    _damageProperty = _damageInfoType.GetProperty("Damage");
                    _bodyPartProperty = _damageInfoType.GetProperty("BodyPart");
                    Debug.Log("[DamageForwarder] DamageInfo reflection initialized successfully!");
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[DamageForwarder] Reflection init failed: " + ex.Message);
            }
        }

        // PRIMARY: Catch UberStrike's CharacterHitArea.ApplyDamage(DamageInfo shot)
        // Using 'object' parameter since we can't reference DamageInfo directly
        public void ApplyDamage(object damageInfo)
        {
            if (TargetBot == null) return;

            float damage = 25f; // Default fallback
            string bodyPart = BodyPartName;

            // Extract damage from DamageInfo via reflection
            if (damageInfo != null && (object)_damageProperty != null) {
                try {
                    object dmgVal = _damageProperty.GetValue(damageInfo, null);
                    if (dmgVal != null) {
                        damage = System.Convert.ToSingle(dmgVal);
                    }

                    if ((object)_bodyPartProperty != null) {
                        object bpVal = _bodyPartProperty.GetValue(damageInfo, null);
                        if (bpVal != null) bodyPart = bpVal.ToString();
                    }
                } catch {}
            }

            Debug.Log("[DamageForwarder] " + BodyPartName + " HIT! Damage: " + damage + " (BodyPart: " + bodyPart + ")");
            TargetBot.ReceiveDamage(damage);
        }

        // Fallback: Standard float damage
        public void ApplyDamage(float damage)
        {
            Debug.Log("[DamageForwarder] " + BodyPartName + " ApplyDamage(float): " + damage);
            if(TargetBot) TargetBot.ReceiveDamage(damage);
        }

        public void TakeDamage(float damage)
        {
            Debug.Log("[DamageForwarder] " + BodyPartName + " TakeDamage: " + damage);
            if(TargetBot) TargetBot.ReceiveDamage(damage);
        }

        public void OnHit(float damage)
        {
            Debug.Log("[DamageForwarder] " + BodyPartName + " OnHit: " + damage);
            if(TargetBot) TargetBot.ReceiveDamage(damage);
        }

        // Catch any generic object message
        public void OnDamage(object msg)
        {
            Debug.Log("[DamageForwarder] " + BodyPartName + " OnDamage(object) received");
            ApplyDamage(msg);
        }
    }

    /// <summary>
    /// Advanced Bot Controller with Perception, Decision, and Execution layers.
    /// </summary>
    public class BotController : MonoBehaviour, IShootable
    {
        // --- ISHOOTABLE IMPLEMENTATION ---
        public bool IsVulnerable { get { return Health > 0; } }
        public bool IsLocal { get { return false; } }

        public void ApplyDamage(DamageInfo shot)
        {
            if (shot == null) return;
            Log("IShootable.ApplyDamage: " + shot.Damage + " (BodyPart: " + shot.BodyPart + ")");
            ReceiveDamage((float)shot.Damage);
        }

        public void ApplyForce(Vector3 position, Vector3 force)
        {
            Log("IShootable.ApplyForce: " + force);
            if (_rigidbody != null)
            {
                _isJumping = true; // Temporary disable gravity
                _rigidbody.isKinematic = false;
                _rigidbody.AddForce(force, ForceMode.Impulse);
                Invoke("ResetJump", 0.5f);
                Invoke("ForceKinematic", 0.6f);
            }
        }

        private void ForceKinematic() { if(_rigidbody != null) _rigidbody.isKinematic = true; }

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
        private float _nextPerceptionTime;
        private const float PERCEPTION_INTERVAL = 0.2f; // Run perception 5 times per second, not 60+
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
            Debug.Log("[BotController] Awake() called - GameObject created: " + gameObject.name);
        }

        void OnDestroy()
        {
            // Log when bot is destroyed and stack trace to find what destroyed it
            float aliveTime = Time.time - _spawnTime;
            string stack = System.Environment.StackTrace;

            Debug.Log("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Debug.Log("!!! BOT DESTROYED: " + BotName + " !!!");
            Debug.Log("!!! Alive for: " + aliveTime.ToString("F2") + " seconds !!!");
            Debug.Log("!!! Position at death: " + transform.position + " !!!");
            Debug.Log("!!! Stack trace: " + stack);
            Debug.Log("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

            // Also try to identify what called Destroy
            if (stack.Contains("Die")) {
                Debug.Log("[BotController] Death cause: Die() was called");
            } else if (stack.Contains("OnTrigger")) {
                Debug.Log("[BotController] Death cause: Trigger collision!");
            } else if (stack.Contains("ReceiveDamage")) {
                Debug.Log("[BotController] Death cause: ReceiveDamage -> Die");
            } else if (stack.Contains("Update")) {
                Debug.Log("[BotController] Death cause: Something in Update loop");
            } else {
                Debug.Log("[BotController] Death cause: EXTERNAL (not from BotController)");
            }
        }

        void Log(string msg) {
             if (_tester != null) _tester.Log("[" + BotName + "] " + msg);
             else Debug.Log("[BotController] " + msg);
        }

        // SPAWN PROTECTION: Prevent instant death
        private float _spawnTime;
        private const float SPAWN_PROTECTION_DURATION = 3.0f;
        private bool _isInitialized = false;

        public void Initialize()
        {
            _spawnTime = Time.time;
            _isInitialized = true;

            string[] names = { "ShadowKiller", "AimBot", "NoobSlayer", "TGPIG", "Striker", "HeadHunter", "Bot_404" };
            BotName = names[UnityEngine.Random.Range(0, names.Length)] + "_" + UnityEngine.Random.Range(10, 99);
            gameObject.name = BotName;

            this.enabled = true;
            Log("=== BOT SPAWNED === Health: " + Health + ", SpawnProtection: " + SPAWN_PROTECTION_DURATION + "s");

            // Start survival diagnostic
            StartCoroutine(SurvivalDiagnostic());

            // Announce Join (non-critical)
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

            // --- DAMAGE FORWARDING SETUP (UBERSTRIKE-SPECIFIC) ---
            // UberStrike uses CharacterHitArea.ApplyDamage(DamageInfo) for damage
            // LocalPlayer is on Layer 25 (Controller) - we should match this!

            SetupDamageSystem();
            
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

        /// <summary>
        /// Sets up the UberStrike-compatible damage system.
        /// UberStrike uses CharacterHitArea.ApplyDamage(DamageInfo) for damage delivery.
        /// LocalPlayer is on Layer 25 (Controller).
        /// </summary>
        void SetupDamageSystem()
        {
            Log("Setting up UberStrike damage system...");

            // --- LAYER SETUP ---
            // Use Layer 8 (Player) - weapons should raycast against this layer
            // Ground detection excludes layer 8 so we don't hit our own collider
            gameObject.layer = 8;
            Log("Set bot root to Layer 8 (Player) for damage/ground separation");

            // Ensure projectiles can hit Layer 25
            Physics.IgnoreLayerCollision(26, 25, false); // Projectiles hit Controller
            Physics.IgnoreLayerCollision(24, 25, false); // Additional projectile layer
            Physics.IgnoreLayerCollision(8, 25, false);  // Player-Controller collision
            Physics.IgnoreLayerCollision(26, 20, false); // Projectiles also hit RemotePlayer
            Physics.IgnoreLayerCollision(24, 20, false);

            // --- GET UBERSTRIKE TYPES VIA REFLECTION ---
            System.Type hitAreaType = null;
            System.Type bodyPartType = null;
            FieldInfo recieveProjectileField = null;
            PropertyInfo shootableProperty = null;

            try {
                hitAreaType = System.Type.GetType("CharacterHitArea, Assembly-CSharp");
                bodyPartType = System.Type.GetType("BodyPart, Assembly-CSharp");

                if ((object)hitAreaType != null) {
                    recieveProjectileField = hitAreaType.GetField("_recieveProjectileDamage",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    shootableProperty = hitAreaType.GetProperty("Shootable",
                        BindingFlags.Public | BindingFlags.Instance);
                    Log("CharacterHitArea type found! RecieveProjectile field: " + ((object)recieveProjectileField != null));
                }
            } catch (System.Exception ex) {
                Log("Failed to get UberStrike types: " + ex.Message);
            }

            // --- FIND REFERENCE HITAREAS FROM LOCALPLAYER ---
            Component[] referenceHitAreas = null;
            GameObject localPlayer = GameObject.Find("LocalPlayer");
            if (localPlayer == null) localPlayer = GameObject.Find("Player");

            if (localPlayer != null && (object)hitAreaType != null) {
                referenceHitAreas = localPlayer.GetComponentsInChildren(hitAreaType, true);
                Log("Found " + (referenceHitAreas != null ? referenceHitAreas.Length : 0) + " CharacterHitArea on LocalPlayer for reference");
            }

            // --- ATTACH DAMAGE FORWARDERS TO ALL COLLIDERS ---
            Collider[] allColliders = GetComponentsInChildren<Collider>(true);
            Log("Found " + allColliders.Length + " colliders on bot");

            foreach (Collider col in allColliders)
            {
                GameObject colObj = col.gameObject;
                string partName = GetBodyPartName(colObj.name);

                // Add DamageForwarder (catches SendMessage calls)
                DamageForwarder forwarder = colObj.GetComponent<DamageForwarder>();
                if (forwarder == null) {
                    forwarder = colObj.AddComponent<DamageForwarder>();
                }
                forwarder.TargetBot = this;
                forwarder.BodyPartName = partName;

                // --- CHARACTERHITAREA ENABLED ---
                // Adding CharacterHitArea with proper IShootable setup
                CharacterHitArea hitArea = colObj.GetComponent<CharacterHitArea>();
                if (hitArea == null) {
                    hitArea = colObj.AddComponent<CharacterHitArea>();
                }
                
                // Configure HitArea via reflection (safest for cross-assembly compatibility)
                try {
                    if ((object)hitArea != null) {
                        // Set the Shootable property back to this BotController
                        // Since BotController now implements IShootable, this works!
                        hitArea.Shootable = this;
                        
                        // Set the body part if possible
                        FieldInfo partField = typeof(CharacterHitArea).GetField("_part", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (partField != null) {
                            object partEnum = GetBodyPartEnum(typeof(BodyPart), partName);
                            if (partEnum != null) partField.SetValue(hitArea, partEnum);
                        }
                        
                        // Ensure it receives projectile damage
                        FieldInfo projField = typeof(CharacterHitArea).GetField("_recieveProjectileDamage", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (projField != null) projField.SetValue(hitArea, true);
                    }
                } catch (System.Exception ex) {
                    Log("Failed to configure CharacterHitArea: " + ex.Message);
                }
            }

            // --- ALSO ATTACH TO ROOT (main collider) ---
            DamageForwarder rootForwarder = gameObject.GetComponent<DamageForwarder>();
            if (rootForwarder == null) {
                rootForwarder = gameObject.AddComponent<DamageForwarder>();
            }
            rootForwarder.TargetBot = this;
            rootForwarder.BodyPartName = "Body";

            // CharacterHitArea on root
            CharacterHitArea rootHitArea = gameObject.GetComponent<CharacterHitArea>();
            if (rootHitArea == null) rootHitArea = gameObject.AddComponent<CharacterHitArea>();
            if (rootHitArea != null) rootHitArea.Shootable = this;

            Log("Damage system setup complete! Attached CharacterHitArea and DamageForwarder to " + allColliders.Length + " colliders");
        }

        string GetBodyPartName(string objectName)
        {
            string lower = objectName.ToLower();
            if (lower.Contains("head")) return "Head";
            if (lower.Contains("torso") || lower.Contains("chest") || lower.Contains("spine")) return "Torso";
            if (lower.Contains("arm") || lower.Contains("hand") || lower.Contains("shoulder")) return "Arms";
            if (lower.Contains("leg") || lower.Contains("foot") || lower.Contains("thigh")) return "Legs";
            if (lower.Contains("nuts") || lower.Contains("pelvis") || lower.Contains("hip")) return "Nuts";
            return "Body";
        }

        object GetBodyPartEnum(System.Type bodyPartType, string partName)
        {
            try {
                string enumName = partName;
                if (partName == "Arms") enumName = "Arms"; // Might be different in enum
                if (partName == "Legs") enumName = "Legs";

                string[] names = System.Enum.GetNames(bodyPartType);
                foreach (string name in names) {
                    if (name.ToLower().Contains(partName.ToLower())) {
                        return System.Enum.Parse(bodyPartType, name);
                    }
                }
                // Default to Body/Torso
                foreach (string name in names) {
                    if (name.ToLower().Contains("body") || name.ToLower().Contains("torso")) {
                        return System.Enum.Parse(bodyPartType, name);
                    }
                }
            } catch {}
            return null;
        }

        void CopyHitAreaConfig(System.Type hitAreaType, Component source, Component target)
        {
            if (source == null || target == null) return;
            try {
                // Copy public fields
                foreach (FieldInfo field in hitAreaType.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                    try {
                        object val = field.GetValue(source);
                        field.SetValue(target, val);
                    } catch {}
                }
            } catch {}
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
                    _rigidbody.velocity = Vector3.zero; // CRITICAL: Clear any existing velocity
                    _rigidbody.angularVelocity = Vector3.zero;
                    _rigidbody.detectCollisions = true; // MUST be true for OnTriggerEnter
                    _rigidbody.WakeUp(); // Ensure it's active
                    Log("Configured Rigidbody as kinematic (velocity zeroed)");
                }
                else
                {
                    // Fallback: Add a Rigidbody if missing to enable OnTriggerEnter
                    _rigidbody = gameObject.AddComponent<Rigidbody>();
                    _rigidbody.isKinematic = true;
                    _rigidbody.useGravity = false;
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
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

        // --- 4. DAMAGE HANDLING (WITH SPAWN PROTECTION) ---
        public void ApplyDamage(float damage) { ReceiveDamage(damage); }
        public void TakeDamage(float damage) { ReceiveDamage(damage); }

        public void ReceiveDamage(float damage)
        {
            // SPAWN PROTECTION: Ignore damage for first few seconds
            float timeSinceSpawn = Time.time - _spawnTime;
            if (timeSinceSpawn < SPAWN_PROTECTION_DURATION)
            {
                Log("SPAWN PROTECTION! Ignoring " + damage + " damage (protection ends in " +
                    (SPAWN_PROTECTION_DURATION - timeSinceSpawn).ToString("F1") + "s)");
                return;
            }

            Log("DAMAGE: " + damage + " | Health: " + Health.ToString("F0") + " -> " + (Health - damage).ToString("F0"));

            Health -= damage;
            if (Health <= 0)
            {
                Log("DEATH! Health depleted (" + Health.ToString("F0") + ")");
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

        public void TriggerHitEffects()
        {
            StartCoroutine(FlashRed());
        }

        public void Die()
        {
            // Log stack trace to see what called Die()
            Log("!!! DIE() CALLED !!! Stack: " + System.Environment.StackTrace);

            // SPAWN PROTECTION: Don't die during protection period
            float timeSinceSpawn = Time.time - _spawnTime;
            if (timeSinceSpawn < SPAWN_PROTECTION_DURATION)
            {
                Log("SPAWN PROTECTION! Preventing death (protection ends in " +
                    (SPAWN_PROTECTION_DURATION - timeSinceSpawn).ToString("F1") + "s)");
                Health = 100f; // Reset health
                return;
            }

            Log(BotName + " died for real!");
            try {
                GameFacade.SendKillMessage("You", "pwned", BotName);
            } catch {}
            Destroy(gameObject);
        }

        IEnumerator SurvivalDiagnostic()
        {
            // Log position every frame for first 10 frames to catch the upward jump
            Vector3 lastPos = transform.position;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null; // Wait one frame
                Vector3 currentPos = transform.position;
                float deltaY = currentPos.y - lastPos.y;
                if (Mathf.Abs(deltaY) > 0.5f)
                {
                    Log("!!! POSITION JUMP frame " + frame + ": Y moved " + deltaY.ToString("F1") + "m! (" + lastPos.y.ToString("F1") + " -> " + currentPos.y.ToString("F1") + ")");
                }
                lastPos = currentPos;
            }

            // Then log every second
            int tick = 0;
            while (true)
            {
                yield return new WaitForSeconds(1.0f);
                tick++;
                if (tick <= 5) // Log first 5 seconds
                {
                    Log("ALIVE tick " + tick + " | Health: " + Health + " | Pos: " + transform.position);
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

        // Handle Jump Pads / Accelerators - DISABLED blind jump to fix instant death
        void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            string name = other.gameObject.name.ToLower();

            // Filter noise (Ignore known non-jump triggers)
            if (name.Contains("sound") || name.Contains("area") || name.Contains("zone") ||
                name.Contains("door") || name.Contains("room") || name.Contains("spawn")) return;

            // DIAGNOSTIC: Log ALL triggers to find what's causing issues
            Log("OnTriggerEnter: " + other.gameObject.name + " (Tag: " + other.tag + ", Layer: " + other.gameObject.layer + ")");

            // Only activate for SPECIFIC jump pads - removed the || true that was causing issues
            bool isSpecificJumpPad = name.Contains("accel") || name.Contains("jump") || name.Contains("pad") ||
                                     name.Contains("antigrav") || name.Contains("boost");

            // REMOVED: grenade from jump pad detection - grenades shouldn't trigger jumps!
            // REMOVED: || true - this was causing blind jumps on EVERYTHING

            if (isSpecificJumpPad)
            {
                // Debounce: Don't jump if we just jumped
                if (_isJumping) return;

                Log(">>> JUMP PAD ACTIVATED: " + other.gameObject.name + " <<<");

                _isJumping = true;
                Invoke("ResetJump", 2.0f);

                // REMOVED: SendMessage to other object - this was causing cascade issues
                // other.gameObject.SendMessage("OnTriggerEnter", ...) - BAD!

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
            // CRITICAL: Force kinematic and zero velocity every frame to prevent physics drift
            if (_rigidbody != null && !_isJumping)
            {
                if (!_rigidbody.isKinematic) _rigidbody.isKinematic = true;
                if (_rigidbody.velocity.sqrMagnitude > 0.01f)
                {
                    Log("WARNING: Rigidbody had velocity " + _rigidbody.velocity + " - zeroing!");
                    _rigidbody.velocity = Vector3.zero;
                }
            }

            try
            {
                if (_currentState == BotState.Idle) return;

                // 1. Perception (Rate Limited)
                if (Time.time > _nextPerceptionTime)
                {
                    UpdatePerception();
                    _nextPerceptionTime = Time.time + PERCEPTION_INTERVAL;
                }

                // 2. Decision
                if (Time.time > _nextDecisionTime)
                {
                    UpdateDecision();
                    _nextDecisionTime = Time.time + ReactionTime;
                }

                // 3. Execution
                ExecuteMovement();
                ExecuteCombat();

                // DIAGNOSTIC: Log state every 3 seconds
                if (Time.frameCount % 180 == 0)
                {
                    string targetName = _bestTarget != null ? _bestTarget.name : "NONE";
                    string playerFound = _cachedPlayerTransform != null ? "YES" : "NO";
                    Log("STATE=" + _currentState + " | Target=" + targetName + " | PlayerCached=" + playerFound + " | Pos=" + transform.position);
                }
            }
            catch (System.Exception ex)
            {
                Log("CRASH in Update: " + ex.ToString());
            }
        }

        // ==================================================================================
        // PERCEPTION
        // ==================================================================================
        private Transform _cachedPlayerTransform;
        private float _lastPlayerSearchTime;

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

            // DIRECT PLAYER TRACKING: Always try to find and track the player
            // This is more reliable than OverlapSphere which might miss them
            if (_cachedPlayerTransform == null || Time.time - _lastPlayerSearchTime > 2.0f)
            {
                _lastPlayerSearchTime = Time.time;
                GameObject playerObj = GameObject.Find("Player");
                if (playerObj == null) playerObj = GameObject.Find("LocalPlayer");
                if (playerObj == null) playerObj = GameObject.Find("GamePlayer");
                if (playerObj != null)
                {
                    _cachedPlayerTransform = playerObj.transform;
                }
            }

            // Always track the player if we have a reference
            if (_cachedPlayerTransform != null)
            {
                float dist = Vector3.Distance(transform.position, _cachedPlayerTransform.position);
                if (dist < ViewDistance)
                {
                    // Always consider player visible for now (simplified)
                    UpdateMemory(_cachedPlayerTransform, _cachedPlayerTransform.position, true);
                }
            }

            // Also check OverlapSphere for other bots
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
            // Check various player object names
            string objName = col.name;
            if (objName == "LocalPlayer" || objName == "GamePlayer" || objName == "Player") return true;
            // Check root object name too
            string rootName = col.transform.root.name;
            if (rootName == "LocalPlayer" || rootName == "GamePlayer" || rootName == "Player") return true;
            try { if (col.CompareTag("Player")) return true; } catch {}
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
                // CRITICAL FIX: Ignore the bot's own layer to prevent hitting own body parts (e.g. HitBoxNuts)
                int layerMask = ~(1 << gameObject.layer);

                if (Physics.Raycast(_cameraTransform.position, dirToTarget, out hit, distance + 1.0f, layerMask))
                {
                    bool isTarget = hit.transform == target || hit.transform.root == target.root;
                    // REDUCED LOGGING: Only log visibility hits, not misses (was causing FPS drop)
                    return isTarget;
                }
            }
            // Removed excessive FOV logging - was spamming thousands of lines
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

                // DIAGNOSTIC: Log visibility check
                if (Time.frameCount % 180 == 0)
                {
                    float timeSince = Time.time - memory.Timestamp;
                    Log("Decision: Target=" + _bestTarget.name + ", Visible=" + isVisible + ", TimeSince=" + timeSince.ToString("F2") + "s, IsVisual=" + memory.IsVisual);
                }

                if (isVisible)
                {
                    float health = LocalSimulationManager.Instance != null ? LocalSimulationManager.Instance.GetHealth(_botId) : 100f;
                    if (health < 40f && UnityEngine.Random.value > Aggression)
                    {
                        _currentState = BotState.Flee;
                        if (Time.frameCount % 180 == 0) Log("-> FLEE (low health)");
                    }
                    else
                    {
                        _currentState = BotState.Combat;
                        if (Time.frameCount % 180 == 0) Log("-> COMBAT (target visible)");
                    }
                }
                else
                {
                    _currentState = BotState.Search;
                    _moveDestination = memory.Position;
                    if (Time.frameCount % 180 == 0) Log("-> SEARCH (target not visible, age=" + (Time.time - memory.Timestamp).ToString("F1") + "s)");
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
                        _strafeDir.y = 0; // Keep strafing horizontal
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

            // FLATTEN MOVEMENT: Ensure we never drive downwards (gravity handles that)
            moveDir.y = 0;
            if (moveDir.sqrMagnitude > 0.01f) moveDir.Normalize();

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
            
            // Simple Bot Avoidance (Anti-Clumping) - OPTIMIZED
            if (LocalSimulationManager.Instance != null) {
                foreach(var kvp in LocalSimulationManager.Instance.BotControllers) {
                    BotController otherBot = kvp.Value;
                    if (otherBot != null && otherBot != this && Vector3.Distance(transform.position, otherBot.transform.position) < 1.0f) {
                        Vector3 push = (transform.position - otherBot.transform.position).normalized;
                        if (_rigidbody != null) _rigidbody.MovePosition(_rigidbody.position + push * 2.0f * Time.deltaTime);
                        else transform.position += push * 2.0f * Time.deltaTime;
                    }
                }
            }

            // Wall Check: Don't walk through walls (REDUCED - was blocking all movement)
            if (moveDir != Vector3.zero) {
                // Only check for actual walls, not triggers or small objects
                // CRITICAL FIX: Ignore the bot's own layer (8) to prevent hitting weapons/body
                int layerMask = ((1 << 0) | (1 << 12)) & ~(1 << gameObject.layer);

                if (Physics.Raycast(transform.position + Vector3.up * 0.5f, moveDir, 0.5f, layerMask)) {
                     // Hit a wall - try to go around it instead of stopping
                     Vector3 right = Vector3.Cross(moveDir, Vector3.up);
                     _moveDestination = transform.position + right * 5f; // Sidestep
                }
            }
        }
        
        // SIMPLIFIED GRAVITY: Snap to ground surface
        private void ApplyGravityAndGroundSnap(ref Vector3 targetPos)
        {
            if (_isJumping) return; // Skip gravity during jump pad usage

            // Ground raycast - find the floor
            RaycastHit hit;
            // Include ALL layers EXCEPT: Layer 2 (IgnoreRaycast), Layer 8 (Player/Bot), Layer 26 (Projectiles)
            // This way we hit ground on any layer but NOT our own bot colliders
            int layerMask = ~((1 << 2) | (1 << 8) | (1 << 26) | (1 << 20)); // Also ignore RemotePlayer (Layer 20)

            // Cast from ABOVE the bot's head to be safe (increased from 1.0 to 1.8)
            Vector3 rayOrigin = new Vector3(targetPos.x, targetPos.y + 1.8f, targetPos.z);

            if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 50.0f, layerMask))
            {
                float groundY = hit.point.y;

                // Sanity check: ground should be below the likely center of the bot
                // If it's too high, it's likely we're hitting something we shouldn't
                if (groundY > targetPos.y + 1.5f)
                {
                    // Too high, ignore it and just fall
                    targetPos.y -= 15f * Time.deltaTime;
                }
                else
                {
                    // Found real ground - snap to it
                    float targetY = groundY + 0.05f;

                    // Smoothly move to ground level
                    float diff = targetY - targetPos.y;
                    if (Mathf.Abs(diff) < 0.5f)
                    {
                        // Close to ground - snap (increased threshold from 0.3 to 0.5)
                        targetPos.y = targetY;
                    }
                    else if (targetPos.y > targetY)
                    {
                        // Above ground - fall fast
                        targetPos.y -= 20f * Time.deltaTime;
                        if (targetPos.y < targetY) targetPos.y = targetY;
                    }
                    else
                    {
                        // Below ground - pop up immediately
                        targetPos.y = targetY;
                    }
                }
            }
            else
            {
                // No ground found in 10m range - fall (reduced from 100m to 10m to prevent catching floor below deep pits)
                targetPos.y -= 15f * Time.deltaTime;
            }

            // Safety bounds - respawn at player location if fell too far
            if (targetPos.y < -100f)
            {
                GameObject player = GameObject.Find("Player");
                if (player != null)
                {
                    targetPos = player.transform.position + Vector3.up * 2f;
                    Log("!!! FELL THROUGH WORLD - Respawning near player!");
                }
                else
                {
                    targetPos = new Vector3(0, 10, 0);
                }
            }
        }

        void ExecuteCombat()
        {
            // Removed excessive logging - was causing FPS drop
            if (_currentState != BotState.Combat || _bestTarget == null)
            {
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
            // Muzzle flash removed for performance

            // Perform raycast
            RaycastHit hit;
            int layerMask = ~(1 << gameObject.layer);
            bool didHit = Physics.Raycast(_cameraTransform.position, _cameraTransform.forward, out hit, 500f, layerMask);
            
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