using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UberStrike.Core.Types;
using UberStrike.Realtime.Common;

public enum BotState
{
    Idle,
    Patrol,
    Chase,
    Combat,
    Dead
}

/// <summary>
/// Main bot MonoBehaviour. Implements IShootable so the local player's weapons
/// can damage bots directly without going through TrainingFpsMode.PlayerHit()
/// (which always damages the local player).
/// </summary>
public class BotController : MonoBehaviour, IShootable
{
    // ----------------------------------------------------------------
    // Static registry — never use FindObjectsOfType per-frame
    // ----------------------------------------------------------------
    public static readonly List<BotController> AllBots = new List<BotController>();

    /// <summary>
    /// Last bot that hit the local player — used for death screen "killed by X".
    /// Set by BotWeaponHandler when a bot's raycast hits LocalPlayer layer.
    /// </summary>
    public static BotController LastBotAttacker;

    /// <summary>
    /// Body part of the last bot attack on the local player (for headshot/nutshot death messages).
    /// </summary>
    public static BodyPart LastBotAttackBodyPart = BodyPart.Body;

    // ----------------------------------------------------------------
    // Health & Armor
    // ----------------------------------------------------------------
    private short _health;
    private short _armor;
    private float _graceTimer;

    // ----------------------------------------------------------------
    // FSM
    // ----------------------------------------------------------------
    private BotState _state = BotState.Idle;
    private Transform _currentTarget;
    private float _lastPerceptionTime;
    private float _respawnTime;
    private float _stateTimer;

    // Crouch dodge (hard bots only)
    private float _crouchEndTime;
    private float _lastCrouchTime;

    // ----------------------------------------------------------------
    // Components
    // ----------------------------------------------------------------
    private BotNavigation _navigation;
    private BotWeaponHandler _weaponHandler;
    private AvatarDecorator _decorator;
    private GameObject _fallbackAvatar;
    private NavMeshAgent _agent;

    // ----------------------------------------------------------------
    // Identity & Variety
    // ----------------------------------------------------------------
    public string BotName { get; private set; }
    public short Health => _health;
    public short Armor => _armor;
    public BotState State => _state;
    public int WeaponId { get; private set; }  // Primary weapon (backwards compat)
    public int[] AllWeaponIds { get; private set; } // All equipped weapons for Quick Switch
    public int BotIndex => _botIndex;
    private int _botIndex;
    private BaseWeaponDecorator[] _weaponDecorators; // Visual weapon models per slot

    // --- Difficulty ---
    public BotDifficulty Difficulty { get; private set; }
    public DifficultyPreset DifficultyStats => BotConfig.Difficulties[(int)Difficulty];

    /// <summary>Kills/Deaths exposed for end-of-match stats injection.</summary>
    public short ScoreboardKills => _characterInfo != null ? _characterInfo.Kills : (short)0;
    public short ScoreboardDeaths => _characterInfo != null ? _characterInfo.Deaths : (short)0;

    /// <summary>
    /// Name of the bot that last attacked this bot (for bot-vs-bot kill feed).
    /// Set by BotWeaponHandler when another bot's raycast hits this bot.
    /// </summary>
    public string PendingAttackerName;

    /// <summary>
    /// Body part hit by the last bot-vs-bot attack (for headshot/nutshot kill feed).
    /// </summary>
    public BodyPart PendingAttackBodyPart = BodyPart.Body;

    /// <summary>
    /// Flag set by BotWeaponHandler in the same call stack as ApplyDamage.
    /// If true when ApplyDamage runs, the hit came from another bot.
    /// If false, the hit came from the local player's weapon.
    /// </summary>
    public bool PendingAttackerIsCurrentHit;

    // Environment death flag — prevents awarding XP to player when bot suicides
    private bool _isEnvironmentDeath;

    // Splash dedup: cannon/launcher/splattergun explosions OverlapSphere hits ALL
    // CharacterHitAreas on the bot skeleton in one frame. Only process the first hit.
    private int _lastExplosionFrame = -1;

    // Match-end state: frozen during end-of-match, auto-ready for next round
    private bool _matchEndFrozen;
    private float _matchEndReadyTime;

    // Deferred init flag (AnimationController created in decorator's Start, not Awake)
    private bool _deferredInitDone;

    // Last body part and weapon class hit on this bot (for kill feed headshot/nutshot/smacked)
    private BodyPart _lastHitBodyPart = BodyPart.Body;
    private UberstrikeItemClass _lastHitWeaponClass = UberstrikeItemClass.WeaponMachinegun;

    // --- Combat AI (orbit strafe, dodge, disengage) ---
    private int _strafeDirection = 1;
    private float _nextStrafeFlipTime;
    private float _dodgeJumpCooldown;

    // --- Patrol naturalness (idle pauses + look-around) ---
    private bool _patrolIdling;
    private float _patrolIdleEndTime;
    private Quaternion _patrolLookTarget;

    // Stale damage timeout — if last damage was >3s ago, treat death as environment
    private float _lastDamageTime;
    private const float STALE_DAMAGE_TIMEOUT = 3f;

    // Kill attribution: set by CharacterHitArea.ApplyDamage ONLY when player weapons
    // deal damage through the standard weapon pipeline. Bot weapons bypass CharacterHitArea
    // and call Shootable.ApplyDamage directly, so this flag is NEVER set for bot damage.
    // This is the single source of truth for "did the player fire this shot?"
    [System.NonSerialized] public bool DamageFromPlayerWeapon;

    // Set when the killing blow came from the player's weapon (DamageFromPlayerWeapon was
    // true at the moment health dropped to 0). This is consumed by Die().
    private bool _killingBlowFromPlayer;

    // Active death zone scanning (fallback for when trigger callbacks don't fire)
    private float _nextDeathZoneScan;

    /// <summary>
    /// CharacterInfo registered in the game's Players dict for scoreboard display.
    /// </summary>
    internal UberStrike.Realtime.Common.CharacterInfo _characterInfo;

    // ================================================================
    // IShootable Implementation
    // ================================================================

    public void ApplyDamage(DamageInfo shot)
    {
        if (_health <= 0 || _graceTimer > 0f) return;

        _lastDamageTime = Time.time;

        // Splash dedup: explosive weapon OverlapSphere hits ALL CharacterHitAreas on the
        // bot's skeleton (head, body, arms, legs) in a single frame. Without this guard,
        // a cannon explosion applies 65 damage * 5-7 hit areas = 325-455 instant kill.
        // Only process the first hit per explosion frame. Hitscan weapons (MG, shotgun,
        // sniper) are unaffected — each pellet/ray hits one hit area.
        if (shot.WeaponClass == UberstrikeItemClass.WeaponCannon ||
            shot.WeaponClass == UberstrikeItemClass.WeaponLauncher ||
            shot.WeaponClass == UberstrikeItemClass.WeaponSplattergun)
        {
            if (Time.frameCount == _lastExplosionFrame) return;
            _lastExplosionFrame = Time.frameCount;
        }

        // Determine if this hit came from a bot or the player.
        // BotWeaponHandler sets PendingAttackerIsCurrentHit=true BEFORE calling ApplyDamage.
        // Player weapons go through CharacterHitArea directly — flag stays false.
        bool hitFromBot = PendingAttackerIsCurrentHit;
        PendingAttackerIsCurrentHit = false; // consume the flag

        if (!hitFromBot)
        {
            // Verify this is actually from the player — check they exist and are alive.
            bool playerCanShoot = GameState.LocalCharacter != null
                && GameState.LocalCharacter.IsAlive
                && GameState.LocalPlayer != null;

            if (!playerCanShoot)
                return; // Reject damage from non-existent or dead player

            // Player hit this bot — check for friendly fire (same team = no damage)
            if (_characterInfo != null && _characterInfo.TeamID != TeamID.NONE
                && GameState.LocalCharacter.TeamID == _characterInfo.TeamID)
            {
                return; // Friendly fire blocked
            }

            // Player hit this bot — clear any stale bot attacker tracking
            PendingAttackerName = null;
            PendingAttackBodyPart = BodyPart.Body;
        }

        // Track body part + weapon class for kill feed
        _lastHitBodyPart = shot.BodyPart;
        _lastHitWeaponClass = shot.WeaponClass;

        short rawDamage = shot.Damage;

        // Armor absorption: armor absorbs 66% of damage, rest goes to health
        if (_armor > 0)
        {
            short armorAbsorb = (short)Mathf.Min(_armor,
                (short)(rawDamage * BotConfig.ArmorAbsorptionRate));
            _armor -= armorAbsorb;
            rawDamage -= armorAbsorb;
        }

        _health -= rawDamage;

        // Track if the KILLING BLOW came from the player's weapon specifically.
        // DamageFromPlayerWeapon is ONLY set by CharacterHitArea.ApplyDamage,
        // which is ONLY called by the player's weapon system (not by bot weapons).
        if (_health <= 0 && DamageFromPlayerWeapon)
            _killingBlowFromPlayer = true;

        // Health bar feedback
        if (_decorator != null && _decorator.HudInformation != null)
        {
            float ratio = Mathf.Clamp01((float)_health / BotConfig.MaxHealth);
            _decorator.HudInformation.SetHealthBarValue(ratio);
        }

        // Hit feedback animation
        if (_decorator != null && _decorator.AnimationController != null && _health > 0)
        {
            _decorator.AnimationController.TriggerAnimation(AnimationIndex.gotHit);
        }

        // If we were idle/patrolling, react to being shot — find the attacker
        if (_health > 0 && (_state == BotState.Idle || _state == BotState.Patrol))
        {
            Transform attacker = FindAttacker();
            if (attacker != null)
            {
                _currentTarget = attacker;
                SetState(BotState.Chase);
            }
        }

        if (_health <= 0)
        {
            Die(shot.Force);
        }
    }

    /// <summary>
    /// Find who attacked this bot. Checks PendingAttackerName (bot-vs-bot) first,
    /// falls back to local player.
    /// </summary>
    private Transform FindAttacker()
    {
        // Check if another bot attacked us
        if (!string.IsNullOrEmpty(PendingAttackerName))
        {
            foreach (var bot in AllBots)
            {
                if (bot != null && bot != this && bot.BotName == PendingAttackerName && bot.Health > 0)
                    return bot.transform;
            }
        }

        // Fall back to local player
        var localPlayer = GameState.LocalPlayer;
        if (localPlayer != null)
            return localPlayer.transform;

        return null;
    }

    public void ApplyForce(Vector3 position, Vector3 force)
    {
        // Optional: apply knockback to NavMeshAgent
    }

    public bool IsVulnerable
    {
        get { return _graceTimer <= 0f && _health > 0; }
    }

    public bool IsLocal
    {
        get { return false; } // Always false for bots
    }

    // ================================================================
    // Lifecycle
    // ================================================================

    void Awake()
    {
        AllBots.Add(this);

        // Ensure bot root has a kinematic Rigidbody so OnTriggerEnter fires
        // with DeathArea/LevelBoundary triggers (Unity requires at least one
        // Rigidbody in a trigger pair for callbacks to work)
        if (GetComponent<Rigidbody>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void OnDestroy()
    {
        AllBots.Remove(this);
        UnregisterFromScoreboard();
        if (_decorator != null)
            Object.Destroy(_decorator.gameObject);
        if (_fallbackAvatar != null)
            Object.Destroy(_fallbackAvatar);
    }

    public void Initialize(string botName, int botIndex)
    {
        _botIndex = botIndex;

        // Assign difficulty based on index and current mix setting
        Difficulty = BotConfig.GetDifficultyForBot(botIndex);
        BotName = "[" + DifficultyStats.Tag + "] " + botName;

        _health = BotConfig.MaxHealth;
        _armor = BotConfig.GetLoadoutArmor(_botIndex);
        _graceTimer = BotConfig.SpawnGraceTime;

        // Set per-bot jump frequency from difficulty (with ±30% randomization)
        if (_navigation != null)
            _navigation.SetJumpChanceForDifficulty(Difficulty);

        // Assign random weapons from different weapon classes for variety
        AllWeaponIds = BotConfig.GetRandomWeaponLoadout();
        WeaponId = AllWeaponIds[0]; // Primary for backwards compat

        // Add sub-components
        _navigation = gameObject.AddComponent<BotNavigation>();
        _weaponHandler = gameObject.AddComponent<BotWeaponHandler>();
        _weaponHandler.Initialize(this);
        _weaponHandler.SetWeapons(AllWeaponIds);

        // Cache NavMeshAgent (added by BotSpawner before Initialize)
        _agent = GetComponent<NavMeshAgent>();

        // Create avatar
        InitializeAvatar();

        // Register bot in the game's Players dict for scoreboard + match count
        RegisterInScoreboard();

        // Announce bot joining (kill feed)
        try
        {
            TeamID botTeam = _characterInfo != null ? _characterInfo.TeamID : TeamID.NONE;
            EventStreamHud.Instance.AddEventText(BotName, botTeam, "joined the game");
        }
        catch (System.Exception) { }

        // Start patrolling
        SetState(BotState.Patrol);
    }

    // ================================================================
    // Scoreboard Registration
    // ================================================================

    /// <summary>
    /// Create a CharacterInfo and add it to the game's Players dict so the bot
    /// appears on the Tab scoreboard and counts toward the match player total.
    /// Uses ActorIds 900+ to avoid collision with real player IDs.
    /// </summary>
    private void RegisterInScoreboard()
    {
        try
        {
            if (GameState.CurrentGame == null) return;

            _characterInfo = new UberStrike.Realtime.Common.CharacterInfo();
            _characterInfo.ActorId = 900 + _botIndex;
            _characterInfo.PlayerName = BotName;
            _characterInfo.Level = 10 + (_botIndex % 20); // varied bot levels
            _characterInfo.Cmid = -(900 + _botIndex); // negative = bot

            // Assign team based on game mode
            int gameMode = GameState.CurrentGame.GameData != null ? GameState.CurrentGame.GameData.GameMode : 0;
            bool isTeamMode = (gameMode == (int)GameMode.TeamDeathMatch || gameMode == (int)GameMode.TeamElimination);
            if (isTeamMode)
            {
                // Alternate bots between teams, put first bot on opposite team from player
                TeamID playerTeam = GameState.LocalCharacter != null ? GameState.LocalCharacter.TeamID : TeamID.BLUE;
                TeamID oppositeTeam = (playerTeam == TeamID.BLUE) ? TeamID.RED : TeamID.BLUE;
                _characterInfo.TeamID = (_botIndex % 2 == 0) ? oppositeTeam : playerTeam;
            }
            else
            {
                _characterInfo.TeamID = TeamID.NONE;
            }
            _characterInfo.Channel = ApplicationDataManager.Channel;
            _characterInfo.Kills = 0;
            _characterInfo.Deaths = 0;
            _characterInfo.Ping = 0;
            _characterInfo.Health = BotConfig.MaxHealth;

            // PlayerState: alive (no Dead flag). Ready=0x20 in the PlayerStates flags enum.
            _characterInfo.PlayerState = (PlayerStates)0x20;

            // Weapon display on scoreboard: register all Quick Switch weapons
            // Maps to slots: Primary(1), Secondary(2), Tertiary(3)
            WeaponInfo.SlotType[] slotTypes = new WeaponInfo.SlotType[]
            {
                WeaponInfo.SlotType.Primary,
                WeaponInfo.SlotType.Secondary,
                WeaponInfo.SlotType.Tertiary
            };
            for (int i = 0; i < AllWeaponIds.Length && i < slotTypes.Length; i++)
            {
                short tmpDmg; float tmpRate; UberstrikeItemClass tmpWc;
                BotConfig.GetWeaponStats(AllWeaponIds[i], out tmpDmg, out tmpRate, out tmpWc);
                _characterInfo.Weapons.SetWeaponSlot(slotTypes[i], AllWeaponIds[i], tmpWc);
            }
            _characterInfo.CurrentWeaponSlot = 1; // Start with Primary (slot 1)

            GameState.CurrentGame.Players[_characterInfo.ActorId] = _characterInfo;
            Debug.Log("[Bot] Registered " + BotName + " in scoreboard (ActorId=" + _characterInfo.ActorId + ")");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Bot] Scoreboard registration failed for " + BotName + ": " + e.Message);
        }
    }

    /// <summary>
    /// Remove bot from the scoreboard Players dict on destroy.
    /// </summary>
    private void UnregisterFromScoreboard()
    {
        try
        {
            if (GameState.CurrentGame != null && _characterInfo != null)
            {
                GameState.CurrentGame.Players.Remove(_characterInfo.ActorId);
            }
        }
        catch (System.Exception) { }
    }

    // ================================================================
    // Avatar
    // ================================================================

    private void InitializeAvatar()
    {
        // Step 1: Ensure the camera renders RemotePlayer layer (20)
        EnsureCameraRendersRemotePlayers();

        // Step 2: Get per-bot gear loadout + skin color for variety
        int[] gear = BotConfig.GearLoadouts[_botIndex % BotConfig.GearLoadouts.Length];
        Color skinColor = BotConfig.SkinColors[_botIndex % BotConfig.SkinColors.Length];
        Debug.Log("[Bot] Using gear loadout #" + (_botIndex % BotConfig.GearLoadouts.Length) +
            " skin=" + skinColor);

        // Step 3: Create avatar via AvatarBuilder
        try
        {
            _decorator = AvatarBuilder.Instance.CreateRemoteAvatar(gear, skinColor);
            Debug.Log("[Bot] Avatar created: " + (_decorator != null ? _decorator.gameObject.name : "NULL"));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Bot] CreateRemoteAvatar FAILED: " + e.Message + "\n" + e.StackTrace);
        }

        // Step 4: If avatar creation failed, create a fallback capsule
        if (_decorator == null)
        {
            Debug.LogWarning("[Bot] Avatar is null — creating fallback capsule");
            CreateFallbackAvatar();
            return;
        }

        // Step 5: Validate mesh
        var smr = _decorator.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null)
        {
            smr.enabled = true;
            smr.updateWhenOffscreen = true; // Prevent culling when not in camera frustum
        }
        else
        {
            Debug.LogWarning("[Bot] No SkinnedMeshRenderer! Creating fallback capsule.");
            Object.Destroy(_decorator.gameObject);
            _decorator = null;
            CreateFallbackAvatar();
            return;
        }

        _decorator.transform.SetParent(transform, false);
        _decorator.transform.localPosition = Vector3.zero;
        _decorator.transform.localRotation = Quaternion.identity;

        // Set layer to RemotePlayer (20) — makes bot hittable by ShootMask
        _decorator.SetLayers(UberstrikeLayer.RemotePlayer);
        gameObject.layer = (int)UberstrikeLayer.RemotePlayer;

        // Ensure mesh renderer is enabled
        if (_decorator.MeshRenderer != null)
        {
            _decorator.MeshRenderer.enabled = true;
        }

        // Wire hit areas to THIS bot controller (NOT CharacterConfig)
        if (_decorator.HitAreas != null)
        {
            foreach (CharacterHitArea hitArea in _decorator.HitAreas)
            {
                hitArea.Shootable = this;
                SetPrivateField(hitArea, "_recieveProjectileDamage", true);
            }
            Debug.Log("[Bot] Wired " + _decorator.HitAreas.Length + " hit areas");
        }

        // Set up HUD name tag with team info
        if (_decorator.HudInformation != null)
        {
            if (_characterInfo != null)
                _decorator.HudInformation.SetCharacterInfo(_characterInfo);
            else
                _decorator.HudInformation.SetAvatarLabel(BotName);
            _decorator.HudInformation.SetHealthBarValue(1f);
        }

        // Team outline: teammates get a white outline (same as real remote players)
        if (_characterInfo != null && _characterInfo.TeamID != TeamID.NONE
            && GameState.LocalCharacter != null)
        {
            bool isFriendly = _characterInfo.TeamID == GameState.LocalCharacter.TeamID;
            _decorator.EnableOutline(isFriendly);
        }

        Debug.Log("[Bot] Avatar initialized: " + BotName + " at " + transform.position +
            " layer=" + gameObject.layer);
    }

    /// <summary>
    /// Ensure the main camera's culling mask includes RemotePlayer layer (20).
    /// </summary>
    private static void EnsureCameraRendersRemotePlayers()
    {
        Camera cam = null;
        if (LevelCamera.Exists && LevelCamera.Instance.MainCamera != null)
            cam = LevelCamera.Instance.MainCamera;
        if (cam == null)
            cam = Camera.main;

        if (cam != null)
        {
            int bit = 1 << (int)UberstrikeLayer.RemotePlayer;
            if ((cam.cullingMask & bit) == 0)
            {
                cam.cullingMask |= bit;
                Debug.Log("[Bot] Added RemotePlayer (layer 20) to camera culling mask");
            }
        }
    }

    /// <summary>
    /// Creates a visible red capsule as a fallback when AvatarBuilder fails.
    /// </summary>
    private void CreateFallbackAvatar()
    {
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "BotFallbackAvatar";
        capsule.transform.SetParent(transform, false);
        capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        capsule.layer = (int)UberstrikeLayer.RemotePlayer;
        gameObject.layer = (int)UberstrikeLayer.RemotePlayer;

        var hitArea = capsule.AddComponent<CharacterHitArea>();
        hitArea.Shootable = this;
        SetPrivateField(hitArea, "_part", BodyPart.Body);
        SetPrivateField(hitArea, "_recieveProjectileDamage", true);

        var renderer = capsule.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = Color.red;
            renderer.enabled = true;
        }

        _fallbackAvatar = capsule;
    }

    // ================================================================
    // Deferred Init — runs one frame after spawn (AnimationController needs Start)
    // ================================================================

    private void DeferredAvatarInit()
    {
        // AnimationController is created in AvatarDecorator.Start() — check if ready
        if (_decorator.AnimationController != null)
        {
            Debug.Log("[Bot] AnimationController ready for " + BotName);
        }
        else
        {
            // Start() hasn't created it — do it manually via reflection (private setter)
            var anim = _decorator.GetComponent<Animation>();
            if (anim != null)
            {
                Debug.LogWarning("[Bot] AnimationController null — creating manually. Clips: " + anim.GetClipCount());
                var controller = new AvatarAnimationController(anim);
                var field = typeof(AvatarDecorator).GetField(
                    "<AnimationController>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(_decorator, controller);
                    Debug.Log("[Bot] AnimationController set via reflection for " + BotName);
                }
            }
        }

        // Force idle animation to unstick the T-pose
        if (_decorator.AnimationController != null)
        {
            _decorator.AnimationController.PlayAnimation(AnimationIndex.idle);
        }

        // Equip a visible weapon on the bot
        EquipBotWeapon();
    }

    // ================================================================
    // Weapon Visual (display only — bot combat uses BotWeaponHandler raycasts)
    // ================================================================

    private void EquipBotWeapon()
    {
        if (_decorator == null) return;

        if (_decorator.WeaponAttachPoint == null)
        {
            Debug.LogWarning("[Bot] No WeaponAttachPoint on avatar for " + BotName);
            return;
        }

        _weaponDecorators = new BaseWeaponDecorator[AllWeaponIds.Length];
        LoadoutSlotType[] slots = new LoadoutSlotType[]
        {
            LoadoutSlotType.WeaponPrimary,
            LoadoutSlotType.WeaponSecondary,
            LoadoutSlotType.WeaponTertiary
        };

        // Default fallback weapon IDs per class (guaranteed to have bundled prefabs)
        int[] defaultFallbacks = { 1002, 1003, 1004, 1005, 1006, 1007 };

        for (int i = 0; i < AllWeaponIds.Length && i < slots.Length; i++)
        {
            try
            {
                int weaponId = AllWeaponIds[i];

                // Get the WeaponItem from the shop database — contains particle config,
                // position/rotation, and all metadata needed for proper initialization.
                WeaponItem weaponItem = ItemManager.Instance.GetWeaponItemInShop(weaponId);

                // If weapon not in shop database, fall back to default of same class
                if (weaponItem == null)
                {
                    short tmpDmg; float tmpRate; UberstrikeItemClass tmpClass;
                    BotConfig.GetWeaponStats(weaponId, out tmpDmg, out tmpRate, out tmpClass);

                    int fallbackId = 1002;
                    foreach (int fb in defaultFallbacks)
                    {
                        short fbDmg; float fbRate; UberstrikeItemClass fbClass;
                        BotConfig.GetWeaponStats(fb, out fbDmg, out fbRate, out fbClass);
                        if (fbClass == tmpClass) { fallbackId = fb; break; }
                    }

                    AllWeaponIds[i] = fallbackId;
                    weaponItem = ItemManager.Instance.GetWeaponItemInShop(fallbackId);
                    if (weaponItem == null)
                    {
                        Debug.LogWarning("[Bot] Fallback weapon " + fallbackId + " not in shop!");
                        continue;
                    }
                }

                // Instantiate the weapon prefab
                var weaponGo = ItemManager.Instance.Instantiate(AllWeaponIds[i]);
                if (weaponGo == null)
                {
                    Debug.LogWarning("[Bot] Weapon prefab " + AllWeaponIds[i] + " not bundled!");
                    continue;
                }

                var weaponDeco = weaponGo.GetComponent<BaseWeaponDecorator>();
                if (weaponDeco == null)
                {
                    Object.Destroy(weaponGo);
                    continue;
                }

                // Configure the decorator exactly like WeaponSlot does for remote players:
                // This sets up particle effects, position, and surface impact type.
                weaponDeco.EnableShootAnimation = false; // No first-person animation
                weaponDeco.DefaultPosition = Vector3.zero;

                // CRITICAL: Set the surface/impact particle effect type from the weapon config.
                // Without this, weapons use default/wrong particles.
                if (weaponItem.Configuration != null)
                {
                    weaponDeco.SetSurfaceEffect(weaponItem.Configuration.ParticleEffect);
                }

                _weaponDecorators[i] = weaponDeco;

                // Assign to avatar weapon slot (parents to WeaponAttachPoint)
                _decorator.AssignWeapon(slots[i], weaponDeco);

                // CRITICAL: Refresh the cached parent reference AFTER parenting.
                // BaseWeaponDecorator.Awake() caches transform.parent at instantiation
                // (which is null/world root). The trail renderer is parented to this
                // cached reference — without refresh, trails appear huge at world origin.
                weaponDeco.RefreshParent();

                // Set weapon layer to match remote player rendering
                LayerUtil.SetLayerRecursively(weaponGo.transform, UberstrikeLayer.RemotePlayer);

                // Only show the first weapon initially
                weaponGo.SetActive(i == 0);

                Debug.Log("[Bot] Equipped weapon " + AllWeaponIds[i] + " (" + weaponItem.Name + ") on " + BotName);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Bot] Weapon equip failed slot " + i + " for " + BotName + ": " + e.Message);
            }
        }

        // Show the primary weapon
        if (_weaponDecorators.Length > 0 && _weaponDecorators[0] != null)
        {
            _decorator.ShowWeapon(LoadoutSlotType.WeaponPrimary);
        }
    }

    // ================================================================
    // Quick Switch — weapon switch callback from BotWeaponHandler
    // ================================================================

    /// <summary>
    /// Called by BotWeaponHandler when Quick Switch changes the active weapon.
    /// Updates scoreboard display and swaps visual weapon model.
    /// </summary>
    public void OnWeaponSwitched(int slotIndex, int newWeaponId)
    {
        WeaponId = newWeaponId;

        // Update scoreboard CurrentWeaponSlot (1=Primary, 2=Secondary, 3=Tertiary)
        if (_characterInfo != null)
        {
            _characterInfo.CurrentWeaponSlot = (byte)(slotIndex + 1);
        }

        // Swap visible weapon model on the avatar
        if (_decorator != null && _decorator.WeaponAttachPoint != null)
        {
            ShowVisualWeapon(slotIndex);
        }
    }

    /// <summary>
    /// Get the active weapon decorator for visual effects (muzzle flash, trail, impacts, sound).
    /// Called by BotWeaponHandler after firing to trigger the decorator's ShowShootEffect().
    /// </summary>
    public BaseWeaponDecorator GetActiveWeaponDecorator()
    {
        if (_weaponDecorators == null || _weaponDecorators.Length == 0) return null;
        int idx = _weaponHandler != null ? _weaponHandler.CurrentSlotIndex : 0;
        if (idx < 0 || idx >= _weaponDecorators.Length) return null;
        return _weaponDecorators[idx];
    }

    /// <summary>
    /// Show the weapon model for the given slot index. Hides all others.
    /// Uses direct SetActive — the decorator's ShowWeapon/IsEnabled pipeline
    /// calls SetActiveRecursively which may not exist in Unity 2022.
    /// </summary>
    private void ShowVisualWeapon(int slotIndex)
    {
        if (_weaponDecorators == null) return;

        for (int i = 0; i < _weaponDecorators.Length; i++)
        {
            if (_weaponDecorators[i] != null)
                _weaponDecorators[i].gameObject.SetActive(i == slotIndex);
        }
    }

    // ================================================================
    // Update Loop
    // ================================================================

    void Update()
    {
        // Deferred init: AnimationController is created in AvatarDecorator.Start(),
        // which runs one frame after Instantiate. Check once after first frame.
        if (!_deferredInitDone && _decorator != null)
        {
            _deferredInitDone = true;
            DeferredAvatarInit();
        }

        // Match-end check: freeze bots and auto-ready when match is over
        if (!CheckMatchRunning())
        {
            if (!_matchEndFrozen)
                FreezeForMatchEnd();
            UpdateMatchEndReady();
            return;
        }
        else if (_matchEndFrozen)
        {
            // Match restarted — unfreeze bots
            UnfreezeFromMatchEnd();
        }

        if (_state == BotState.Dead)
        {
            UpdateDead();
            return;
        }

        // Fallback death check: kill bot if it falls below the world floor
        if (transform.position.y < BotConfig.DeathFloorY)
        {
            KillByEnvironment();
            return;
        }

        // Active death zone scan: check for DeathArea/LevelBoundary overlap every 0.5s
        // Fallback for when physics trigger callbacks don't fire due to layer matrix issues
        CheckDeathZoneOverlap();

        UpdateGraceTimer();
        UpdateCliffAvoidance();
        UpdatePerception();
        UpdateFSM();
        UpdateRotation();
        UpdateAnimation();
    }

    private void UpdateGraceTimer()
    {
        if (_graceTimer > 0f)
        {
            _graceTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Active death zone scan using OverlapSphere. Runs every 0.2s.
    /// Catches DeathArea zones even when physics trigger callbacks
    /// fail due to layer collision matrix settings.
    /// NOTE: Only checks DeathArea, NOT LevelBoundary. LevelBoundary is a SAFE zone —
    /// bots die when they EXIT it (handled by OnTriggerExit), not when inside it.
    /// Uses 2.5m radius to catch larger death zones (Gideon's Tower has wide lava pits).
    /// </summary>
    private void CheckDeathZoneOverlap()
    {
        if (Time.time < _nextDeathZoneScan) return;
        _nextDeathZoneScan = Time.time + 0.2f;

        // Check all trigger colliders within 2.5m of the bot's center
        Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, 2.5f,
            Physics.AllLayers, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].GetComponent<DeathArea>() != null)
            {
                Debug.Log("[Bot] " + BotName + " death zone overlap detected: " + hits[i].name);
                KillByEnvironment();
                return;
            }
        }
    }

    /// <summary>
    /// Difficulty-aware cliff avoidance. Hard bots almost always avoid edges,
    /// Easy bots rarely do (natural-looking deaths when fleeing near edges).
    /// </summary>
    private void UpdateCliffAvoidance()
    {
        if (_navigation != null)
            _navigation.CheckCliffAhead(DifficultyStats.CliffAvoidChance);
    }

    // ================================================================
    // Perception — 5 Hz throttled
    // ================================================================

    private void UpdatePerception()
    {
        if (Time.time - _lastPerceptionTime < BotConfig.PerceptionInterval) return;
        _lastPerceptionTime = Time.time;

        // Find the closest visible target (player or other bot)
        Transform bestTarget = null;
        float bestDist = float.MaxValue;

        // Check local player (skip if same team or dead)
        Transform player = GetLocalPlayerTransform();
        bool playerAlive = GameState.LocalCharacter != null && GameState.LocalCharacter.IsAlive;
        if (player != null && playerAlive && !IsSameTeam(player))
        {
            float playerDist = Vector3.Distance(transform.position, player.position);
            if (playerDist <= BotConfig.SightDistance && CanSeeTarget(player, playerDist))
            {
                bestTarget = player;
                bestDist = playerDist;
            }
        }

        // Check other bots (skip teammates)
        for (int i = 0; i < AllBots.Count; i++)
        {
            var otherBot = AllBots[i];
            if (otherBot == null || otherBot == this || otherBot.Health <= 0) continue;
            if (IsSameTeam(otherBot.transform)) continue; // Don't target teammates

            float botDist = Vector3.Distance(transform.position, otherBot.transform.position);
            if (botDist < bestDist && botDist <= BotConfig.SightDistance
                && CanSeeTarget(otherBot.transform, botDist))
            {
                bestTarget = otherBot.transform;
                bestDist = botDist;
            }
        }

        if (bestTarget != null)
        {
            if (_currentTarget != bestTarget)
            {
                _currentTarget = bestTarget;
                if (_weaponHandler != null)
                    _weaponHandler.ResetReaction();
            }
        }
        else
        {
            // No visible targets — lose current if not chasing
            if (_state != BotState.Chase)
            {
                if (_currentTarget != null) LoseTarget();
            }
        }
    }

    /// <summary>
    /// Check if a target is on the same team as this bot (friendly fire prevention).
    /// Returns true if same team (should NOT be targeted).
    /// </summary>
    private bool IsSameTeam(Transform target)
    {
        if (_characterInfo == null || _characterInfo.TeamID == TeamID.NONE)
            return false; // Not in a team mode — everyone is an enemy

        // Check if target is the local player
        if (target == GetLocalPlayerTransform())
        {
            return GameState.LocalCharacter != null
                && GameState.LocalCharacter.TeamID == _characterInfo.TeamID;
        }

        // Check if target is another bot
        var otherBot = target.GetComponent<BotController>();
        if (otherBot != null && otherBot._characterInfo != null)
        {
            return otherBot._characterInfo.TeamID == _characterInfo.TeamID;
        }

        return false;
    }

    /// <summary>
    /// Check if this bot can see the given target (angle + line-of-sight).
    /// </summary>
    private bool CanSeeTarget(Transform target, float dist)
    {
        Vector3 dirToTarget = (target.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToTarget);

        // Skip angle check if already engaged (chasing/combat)
        if (angle > BotConfig.SightAngle && _state != BotState.Chase && _state != BotState.Combat)
            return false;

        // Line of sight raycast (ignore triggers — bot's own capsule trigger would block LOS)
        Vector3 eyePos = transform.position + Vector3.up * 1.4f;
        Vector3 targetPos = target.position + Vector3.up * 0.8f;
        RaycastHit hit;
        if (Physics.Raycast(eyePos, (targetPos - eyePos).normalized, out hit, dist + 1f,
            ~0, QueryTriggerInteraction.Ignore))
        {
            // Hit the target's layer or a CharacterHitArea
            int hitLayer = hit.collider.gameObject.layer;
            if (hitLayer == (int)UberstrikeLayer.LocalPlayer ||
                hitLayer == (int)UberstrikeLayer.RemotePlayer ||
                hit.collider.GetComponent<CharacterHitArea>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private void LoseTarget()
    {
        _currentTarget = null;
        if (_weaponHandler != null)
            _weaponHandler.ResetReaction();
    }

    private Transform GetLocalPlayerTransform()
    {
        if (GameState.LocalPlayer != null)
            return GameState.LocalPlayer.transform;
        return null;
    }

    // ================================================================
    // FSM
    // ================================================================

    private void SetState(BotState newState)
    {
        if (_state == newState) return;
        _state = newState;
        _stateTimer = 0f;

        switch (newState)
        {
            case BotState.Patrol:
                _navigation.Resume();
                _navigation.SetInCombat(false);
                _navigation.SetCrouching(false);
                _navigation.PickRandomPatrolPoint(transform.position);
                break;
            case BotState.Chase:
                _navigation.Resume();
                _navigation.SetInCombat(false);
                _navigation.SetCrouching(false);
                break;
            case BotState.Combat:
                _navigation.Resume();
                _navigation.SetInCombat(true);
                break;
            case BotState.Idle:
                _navigation.Stop();
                break;
        }
    }

    private void UpdateFSM()
    {
        _stateTimer += Time.deltaTime;

        switch (_state)
        {
            case BotState.Idle:
                UpdateIdle();
                break;
            case BotState.Patrol:
                UpdatePatrol();
                break;
            case BotState.Chase:
                UpdateChase();
                break;
            case BotState.Combat:
                UpdateCombat();
                break;
        }
    }

    private void UpdateIdle()
    {
        if (_stateTimer > 1f)
        {
            SetState(BotState.Patrol);
        }

        if (_currentTarget != null)
        {
            float dist = Vector3.Distance(transform.position, _currentTarget.position);
            if (dist <= BotConfig.EngageDistance)
                SetState(BotState.Combat);
            else
                SetState(BotState.Chase);
        }
    }

    private void UpdatePatrol()
    {
        if (_currentTarget != null)
        {
            _patrolIdling = false;
            float dist = Vector3.Distance(transform.position, _currentTarget.position);
            if (dist <= BotConfig.EngageDistance)
            {
                SetState(BotState.Combat);
                return;
            }
            else
            {
                SetState(BotState.Chase);
                return;
            }
        }

        // Idle pause: bot stops and looks around before picking next patrol point
        if (_patrolIdling)
        {
            // Slowly rotate toward look target during idle
            transform.rotation = Quaternion.Slerp(transform.rotation, _patrolLookTarget,
                Time.deltaTime * 2f);

            if (Time.time >= _patrolIdleEndTime)
            {
                _patrolIdling = false;
                _navigation.PickRandomPatrolPoint(transform.position);
                _stateTimer = 0f;
            }
            return;
        }

        if (_navigation.HasReachedDestination)
        {
            if (_stateTimer > BotConfig.PatrolWaitTime)
            {
                // 70% chance to idle-pause and look around, 30% walk immediately
                if (Random.value < 0.7f)
                {
                    _patrolIdling = true;
                    _patrolIdleEndTime = Time.time + Random.Range(1.5f, 3.5f);

                    // Pick a random look direction
                    float angle = Random.Range(0f, 360f);
                    Vector3 lookDir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    _patrolLookTarget = Quaternion.LookRotation(lookDir);
                    _navigation.Stop();
                }
                else
                {
                    _navigation.PickRandomPatrolPoint(transform.position);
                    _stateTimer = 0f;
                }
            }
        }
    }

    private void UpdateChase()
    {
        if (_currentTarget == null)
        {
            SetState(BotState.Patrol);
            return;
        }

        float dist = Vector3.Distance(transform.position, _currentTarget.position);

        if (dist <= BotConfig.EngageDistance)
        {
            SetState(BotState.Combat);
            return;
        }

        if (dist > BotConfig.SightDistance)
        {
            _currentTarget = null;
            SetState(BotState.Patrol);
            return;
        }

        _navigation.SetDestination(_currentTarget.position);
    }

    private void UpdateCombat()
    {
        if (_currentTarget == null)
        {
            SetState(BotState.Patrol);
            return;
        }

        // Drop target if player died — prevents shooting corpses and ghost kills
        if (_currentTarget == GetLocalPlayerTransform())
        {
            bool playerAlive = GameState.LocalCharacter != null && GameState.LocalCharacter.IsAlive;
            if (!playerAlive)
            {
                LoseTarget();
                SetState(BotState.Patrol);
                return;
            }
        }

        float dist = Vector3.Distance(transform.position, _currentTarget.position);

        if (dist > BotConfig.EngageDistance * 1.2f)
        {
            SetState(BotState.Chase);
            return;
        }

        if (dist > BotConfig.SightDistance)
        {
            _currentTarget = null;
            SetState(BotState.Patrol);
            return;
        }

        // Always fire at the target
        _weaponHandler.FireAtTarget(_currentTarget);

        // Hard bots: crouch dodge when player is looking at them
        if (Difficulty == BotDifficulty.Hard && Time.time > _crouchEndTime)
        {
            // End crouch if duration expired
            if (_navigation != null && _navigation.IsCrouching)
                _navigation.SetCrouching(false);

            // Check if player is aiming at us (rough check: player forward vs direction to bot)
            if (Time.time - _lastCrouchTime > BotConfig.CrouchCooldown
                && _currentTarget == GetLocalPlayerTransform())
            {
                Camera playerCam = Camera.main;
                if (playerCam != null)
                {
                    Vector3 toBot = (transform.position + Vector3.up * 1.2f - playerCam.transform.position).normalized;
                    float aimDot = Vector3.Dot(playerCam.transform.forward, toBot);
                    // Player is aiming roughly at us (within ~10 degrees)
                    if (aimDot > 0.985f && Random.value < BotConfig.CrouchDetectChance)
                    {
                        _navigation.SetCrouching(true);
                        _crouchEndTime = Time.time + BotConfig.CrouchDuration;
                        _lastCrouchTime = Time.time;
                    }
                }
            }
        }

        // Direction from bot to target
        Vector3 toTarget = (_currentTarget.position - transform.position).normalized;
        Vector3 strafeDir = Vector3.Cross(toTarget, Vector3.up) * _strafeDirection;
        float healthRatio = (float)_health / BotConfig.MaxHealth;

        // Periodic strafe direction flip (from OrbitStrafeBehavior._nextFlipUtc)
        if (Time.time >= _nextStrafeFlipTime)
        {
            _strafeDirection *= -1;
            _nextStrafeFlipTime = Time.time + Random.Range(BotConfig.StrafeFlipMinTime, BotConfig.StrafeFlipMaxTime);
        }

        // Difficulty-aware strafe distance and dodge chance
        float strafeDist = DifficultyStats.StrafeDistance;
        float dodgeChance = DifficultyStats.DodgeJumpChance;

        // --- Behavior selection based on health and distance ---

        if (healthRatio < BotConfig.DisengageHealthRatio)
        {
            // DISENGAGE: Low HP — retreat AWAY from target while still shooting
            Vector3 retreatDir = -toTarget + strafeDir * 0.5f;
            Vector3 retreatPoint = transform.position + retreatDir.normalized * strafeDist * 2f;
            _navigation.SetDestination(retreatPoint);
        }
        else if (dist < BotConfig.CloseRangeDistance)
        {
            // EVASIVE: Close range — fast lateral strafe + dodge jumps
            Vector3 evadePoint = transform.position + strafeDir * strafeDist * 1.5f;
            _navigation.SetDestination(evadePoint);

            // Dodge jump at close range (chance per second, with cooldown)
            if (dodgeChance > 0f && _dodgeJumpCooldown <= 0f)
            {
                if (Random.value < dodgeChance * Time.deltaTime)
                {
                    _navigation.DodgeJump(strafeDir);
                    _dodgeJumpCooldown = BotConfig.DodgeJumpCooldown;
                }
            }
        }
        else if (dist < BotConfig.EngageDistance)
        {
            // ORBIT STRAFE: Mid range — circle target at ideal distance
            // Tangent movement (perpendicular to target direction)
            Vector3 tangent = strafeDir;

            // Range correction: nudge toward or away from ideal distance
            float rangeDelta = dist - BotConfig.IdealCombatDistance;
            Vector3 rangeCorrection = toTarget * Mathf.Clamp(rangeDelta * 0.3f, -1f, 1f);

            Vector3 orbitPoint = transform.position + (tangent * strafeDist + rangeCorrection).normalized * strafeDist;
            _navigation.SetDestination(orbitPoint);
        }
        else
        {
            // FAR: Advance toward target
            _navigation.SetDestination(_currentTarget.position);
        }

        // Tick dodge jump cooldown
        if (_dodgeJumpCooldown > 0f)
            _dodgeJumpCooldown -= Time.deltaTime;
    }

    // ================================================================
    // Rotation — face target or movement direction
    // ================================================================

    private void UpdateRotation()
    {
        Vector3 lookDir = Vector3.zero;

        if (_currentTarget != null)
        {
            // Always face the target when we have one — regardless of state.
            // Real players always look at their opponent even while moving.
            if (_weaponHandler != null && _weaponHandler.LastAimDirection.sqrMagnitude > 0.01f
                && _state == BotState.Combat)
            {
                lookDir = _weaponHandler.LastAimDirection;
            }
            else
            {
                lookDir = _currentTarget.position - transform.position;
            }
        }
        else if (_navigation != null)
        {
            lookDir = _navigation.DesiredVelocity;
        }

        if (lookDir.sqrMagnitude > 0.01f)
        {
            lookDir.y = 0f; // Keep horizontal rotation only (no tilting)
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            }
        }
    }

    /// <summary>
    /// Get the center of mass of a target using their collider bounds.
    /// Used for both visual pitch aiming and by BotWeaponHandler for shot targeting.
    /// </summary>
    private Vector3 GetTargetCenter(Transform target)
    {
        var cc = target.GetComponent<CharacterController>();
        if (cc != null)
            return target.position + cc.center;

        var capsule = target.GetComponent<CapsuleCollider>();
        if (capsule != null)
            return target.position + capsule.center;

        var col = target.GetComponent<Collider>();
        if (col != null)
            return col.bounds.center;

        return target.position + Vector3.up * 0.8f;
    }

    // ================================================================
    // Animation
    // ================================================================

    private void UpdateAnimation()
    {
        if (_decorator == null || _decorator.AnimationController == null) return;

        var anim = _decorator.AnimationController;
        float speed = _navigation != null ? _navigation.CurrentSpeed : 0f;
        bool jumping = _navigation != null && (_navigation.IsJumping || _navigation.IsLaunched);
        bool crouching = _navigation != null && _navigation.IsCrouching;

        // Base body animation
        if (jumping)
            anim.PlayAnimation(AnimationIndex.jumpUp);
        else if (crouching)
        {
            if (speed > 0.2f)
                anim.PlayAnimation(AnimationIndex.crouch);
            else
                anim.PlayAnimation(AnimationIndex.squat);
        }
        else if (speed > 1f)
            anim.PlayAnimation(AnimationIndex.run);
        else if (speed > 0.2f)
            anim.PlayAnimation(AnimationIndex.walk);
        else
        {
            // Use weapon-holding idle (arms up) instead of weaponless idle
            anim.PlayAnimation(AnimationIndex.heavyGunBreathe);
        }

        // Upper body aim pitch — same system as real remote players
        // heavyGunUpDown is a layer-1 additive animation mixed on the Spine bone
        // SetAnimationTimeNormalized drives the normalized time (0=looking down, 1=looking up)
        if (_currentTarget != null && (_state == BotState.Combat || _state == BotState.Chase))
        {
            Vector3 toTarget = GetTargetCenter(_currentTarget) - transform.position;
            // Convert vertical angle to 0..1 range: 0.5 = level, 0 = down, 1 = up
            float vertAngle = Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude);
            float normalizedAim = Mathf.Clamp01(0.5f + vertAngle / Mathf.PI);
            anim.SetAnimationTimeNormalized(AnimationIndex.heavyGunUpDown, normalizedAim);
        }
        else
        {
            // Default: arms level (0.5)
            anim.SetAnimationTimeNormalized(AnimationIndex.heavyGunUpDown, 0.5f);
        }

        // Trigger shoot animation when firing
        if (_state == BotState.Combat && _weaponHandler != null && _currentTarget != null)
        {
            anim.PlayAnimation(AnimationIndex.shootHeavyGun);
        }

        // CRITICAL: UpdateAnimation() must be called every frame to actually blend
        // Animation.Blend() calls. PlayAnimation() only sets EndTime — the controller's
        // UpdateAnimation() does the actual Animation.Blend() work.
        anim.UpdateAnimation();
    }

    // ================================================================
    // Death & Respawn
    // ================================================================

    private void Die(Vector3 force)
    {
        _state = BotState.Dead;
        _respawnTime = Time.time + BotConfig.RespawnDelay;

        gameObject.layer = (int)UberstrikeLayer.IgnoreRaycast;

        bool isEnvDeath = _isEnvironmentDeath;
        _isEnvironmentDeath = false; // consume flag

        // Stale damage timeout: if last combat damage was >3s ago, treat as environment death.
        if (!isEnvDeath && Time.time - _lastDamageTime > STALE_DAMAGE_TIMEOUT)
            isEnvDeath = true;

        // Kill attribution — airtight, no time-based guessing.
        // _killingBlowFromPlayer is ONLY set when health drops to 0 from a non-bot source
        // in the same ApplyDamage call. This is the single source of truth.
        bool killedByPlayer = _killingBlowFromPlayer;
        _killingBlowFromPlayer = false; // consume flag

        bool killedByBot = !killedByPlayer && !string.IsNullOrEmpty(PendingAttackerName);
        string killerName = killedByBot ? PendingAttackerName : null;
        BodyPart killBodyPart = killedByBot ? PendingAttackBodyPart : _lastHitBodyPart;

        if (isEnvDeath)
        {
            // Environment death (death zone, fall off map) — no one gets kill credit
            // Show suicide in kill feed
            try
            {
                TeamID myTeam = _characterInfo != null ? _characterInfo.TeamID : TeamID.NONE;
                EventStreamHud.Instance.AddEventText(BotName, myTeam,
                    "killed themself", "", myTeam);
            }
            catch (System.Exception) { }
        }
        else if (killedByBot)
        {
            // Killed by another bot — show in kill feed, credit killer bot
            ShowKillFeed(killerName, killBodyPart, _lastHitWeaponClass);
            foreach (var bot in AllBots)
            {
                if (bot != null && bot.BotName == killerName && bot._characterInfo != null)
                {
                    bot._characterInfo.Kills++;
                    break;
                }
            }
        }
        else if (killedByPlayer)
        {
            // Killed by local player — the killing blow came from a non-bot source
            AwardKillXp();
            ShowKillFeed(null, killBodyPart, _lastHitWeaponClass);
        }
        else
        {
            // Unattributed death — treat as suicide (no ghost kills)
            try
            {
                TeamID myTeam = _characterInfo != null ? _characterInfo.TeamID : TeamID.NONE;
                EventStreamHud.Instance.AddEventText(BotName, myTeam,
                    "killed themself", "", myTeam);
            }
            catch (System.Exception) { }
        }

        // Update scoreboard: mark dead + increment deaths
        if (_characterInfo != null)
        {
            _characterInfo.Deaths++;
            _characterInfo.Health = 0; // IsAlive checks Health > 0
            _characterInfo.PlayerState |= (PlayerStates)0x02; // Dead flag — skull icon on scoreboard
        }

        // Clear attacker tracking
        PendingAttackerName = null;
        PendingAttackBodyPart = BodyPart.Body;

        if (_decorator != null)
        {
            // Play death animation using Unity's Animation.Play() directly.
            // The AvatarAnimationController's Blend-based system requires per-frame
            // UpdateAnimation() calls to maintain weight, but dead bots skip Update().
            // Animation.Play() is self-sustaining and holds the final pose with ClampForever.
            PlayDeathAnimation();

            // Play death sound
            try { _decorator.PlayDieSound(); }
            catch (System.Exception) { }

            // Keep body visible for 4s then hide
            Invoke(nameof(HideAvatar), 4f);
        }

        if (_agent != null && _agent.enabled)
            _agent.enabled = false;

        _navigation.Stop();
    }

    private void AwardKillXp()
    {
        int xp = BotConfig.XpPerKill;

        try { PlayerDataManager.Instance.AttributeXp(xp); }
        catch (System.Exception) { }

        try { XpPtsHud.Instance.GainXp(xp); }
        catch (System.Exception) { }

        if (GlobalUIRibbon.Exists)
        {
            GlobalUIRibbon.Instance.AddXPEvent(xp);
        }

        TotalBotKills++;

        // Increment player's scoreboard kill count (Tab overlay reads this)
        // Without this, kills stay at 0 (or go negative from death penalties)
        try
        {
            if (GameState.LocalCharacter != null)
                GameState.LocalCharacter.Kills++;
        }
        catch (System.Exception) { }

        Debug.Log("[Bot] " + BotName + " killed! +" + xp + " XP (Total kills: " + TotalBotKills + ")");
    }

    public static int TotalBotKills { get; private set; }

    /// <summary>
    /// Reset all static match tracking state. Called on scene load to prevent
    /// stale kills/deaths from persisting across map changes.
    /// </summary>
    public static void ResetMatchStats()
    {
        TotalBotKills = 0;
        LastBotAttacker = null;
        LastBotAttackBodyPart = BodyPart.Body;

        try
        {
            if (GameState.LocalCharacter != null)
            {
                GameState.LocalCharacter.Kills = 0;
                GameState.LocalCharacter.Deaths = 0;
            }
        }
        catch (System.Exception) { }
    }

    /// <summary>
    /// Show kill in the event stream HUD. Supports both player-kills-bot and bot-kills-bot.
    /// </summary>
    private void ShowKillFeed(string killerBotName, BodyPart bodyPart, UberstrikeItemClass weaponClass)
    {
        try
        {
            string killer;
            if (killerBotName != null)
            {
                // Bot killed by another bot
                killer = killerBotName;
            }
            else
            {
                // Bot killed by local player
                killer = "Player";
                if (GameState.LocalCharacter != null)
                    killer = GameState.LocalCharacter.PlayerName;
            }

            // Build kill verb based on weapon class and body part.
            // Explosive weapons (cannon, launcher, splattergun) can't headshot/nutshot —
            // their splash hits all body parts so the "last hit" body part is random.
            bool isExplosive = weaponClass == UberstrikeItemClass.WeaponCannon
                || weaponClass == UberstrikeItemClass.WeaponLauncher
                || weaponClass == UberstrikeItemClass.WeaponSplattergun;

            string verb = "killed";
            if (weaponClass == UberstrikeItemClass.WeaponMelee)
                verb = "smacked";
            else if (!isExplosive && (bodyPart & BodyPart.Head) != 0)
                verb = "headshot";
            else if (!isExplosive && (bodyPart & BodyPart.Nuts) != 0)
                verb = "nutshot";

            TeamID killerTeam = TeamID.NONE;
            TeamID myTeam = _characterInfo != null ? _characterInfo.TeamID : TeamID.NONE;
            // If killer is the player, use their team
            if (GameState.LocalCharacter != null)
                killerTeam = GameState.LocalCharacter.TeamID;
            EventStreamHud.Instance.AddEventText(killer, killerTeam, verb, BotName, myTeam);
        }
        catch (System.Exception) { }
    }

    /// <summary>
    /// Called by BotSpawner when the local player dies. Shows the death screen
    /// with the killer bot's name using the game's feedback HUD.
    /// </summary>
    public static void ShowBotKilledPlayerScreen()
    {
        if (LastBotAttacker == null) return;

        string killerName = LastBotAttacker.BotName;
        BodyPart bodyPart = LastBotAttackBodyPart;

        try
        {
            // Show centered death message based on body part hit
            string deathMsg;
            if ((bodyPart & BodyPart.Head) != 0)
                deathMsg = string.Format(LocalizedStrings.HeadshotFromN, killerName);
            else if ((bodyPart & BodyPart.Nuts) != 0)
                deathMsg = string.Format(LocalizedStrings.NutshotFromN, killerName);
            else
                deathMsg = string.Format(LocalizedStrings.KilledByN, killerName);

            EventFeedbackHud.Instance.EnqueueFeedback(
                InGameEventFeedbackType.CustomMessage, deathMsg, 6);
        }
        catch (System.Exception) { }

        try
        {
            // Also add to kill feed (top-right)
            string playerName = "Player";
            if (GameState.LocalCharacter != null)
                playerName = GameState.LocalCharacter.PlayerName;

            string verb = "killed";
            if ((bodyPart & BodyPart.Head) != 0)
                verb = "headshot";
            else if ((bodyPart & BodyPart.Nuts) != 0)
                verb = "nutshot";

            TeamID botTeam = (LastBotAttacker != null && LastBotAttacker._characterInfo != null)
                ? LastBotAttacker._characterInfo.TeamID : TeamID.NONE;
            TeamID playerTeam = GameState.LocalCharacter != null ? GameState.LocalCharacter.TeamID : TeamID.NONE;
            EventStreamHud.Instance.AddEventText(killerName, botTeam, verb, playerName, playerTeam);
        }
        catch (System.Exception) { }

        Debug.Log("[Bot] Death screen: " + killerName + " killed player (bodyPart=" + bodyPart + ")");
        LastBotAttacker = null;
        LastBotAttackBodyPart = BodyPart.Body;
    }

    /// <summary>
    /// Reflection helper to set private/protected fields on MonoBehaviours.
    /// Searches the type and its base type for the field.
    /// </summary>
    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var type = obj.GetType();
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null && type.BaseType != null)
            field = type.BaseType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
            field.SetValue(obj, value);
        else
            Debug.LogWarning("[Bot] Reflection field not found: " + fieldName + " on " + type.Name);
    }

    // ================================================================
    // Environmental Death (DeathArea, fall off map, etc.)
    // ================================================================

    /// <summary>
    /// Kill bot via environmental hazard (death zone, fall off map, etc.).
    /// Similar to Die() but without attacker tracking — shows as self-kill.
    /// </summary>
    public void KillByEnvironment()
    {
        if (_health <= 0) return; // Already dead
        _health = 0;

        // Mark as environment death so Die() doesn't award XP to the player
        _isEnvironmentDeath = true;
        // Clear any pending attacker — no one gets kill credit for environment deaths
        PendingAttackerName = null;
        PendingAttackBodyPart = BodyPart.Body;

        Debug.Log("[Bot] " + BotName + " killed by environment");
        Die(Vector3.zero);
    }

    // ================================================================
    // Freeze / Unfreeze (F3 toggle) — disables ALL bot subsystems
    // ================================================================

    public void Freeze()
    {
        enabled = false; // BotController.Update()
        if (_navigation != null) _navigation.enabled = false;
        if (_weaponHandler != null) _weaponHandler.enabled = false;
        if (_navigation != null) _navigation.Stop();
    }

    public void Unfreeze()
    {
        enabled = true;
        if (_navigation != null) _navigation.enabled = true;
        if (_weaponHandler != null) _weaponHandler.enabled = true;
        if (_navigation != null) _navigation.Resume();
    }

    /// <summary>
    /// Play death animation directly via Unity's Animation component.
    /// Uses Animation.Play() instead of the AvatarAnimationController's Blend system
    /// because dead bots skip UpdateAnimation(), causing Blend-based animations to fade out.
    /// Animation.Play() with WrapMode.ClampForever is self-sustaining — the body holds
    /// the final death pose indefinitely without per-frame calls.
    /// </summary>
    private void PlayDeathAnimation()
    {
        try
        {
            // Find the Animation component on the decorator or its children
            Animation anim = _decorator.GetComponentInChildren<Animation>();
            if (anim == null) return;

            // Stop ALL current animations (walk, run, shoot, breathe, etc.)
            anim.Stop();

            // Try to play die1 directly
            AnimationState dieState = anim["die1"];
            if (dieState != null)
            {
                dieState.wrapMode = WrapMode.ClampForever;
                dieState.layer = 10; // High layer to override everything
                dieState.weight = 1f;
                dieState.speed = 1f;
                dieState.time = 0f;
                dieState.enabled = true;
                anim.Play("die1");
            }
            else
            {
                // Fallback: try through the controller
                if (_decorator.AnimationController != null)
                    _decorator.AnimationController.TriggerAnimation(AnimationIndex.die1, true);
            }
        }
        catch (System.Exception) { }
    }

    private void HideAvatar()
    {
        if (_decorator != null)
            _decorator.gameObject.SetActive(false);
        if (_fallbackAvatar != null)
            _fallbackAvatar.SetActive(false);
    }

    private void UpdateDead()
    {
        // Don't respawn if match has ended (FreezeForMatchEnd handles dead bots)
        if (_matchEndFrozen) return;

        // Apply gravity to dead body so it falls naturally instead of floating mid-air.
        // Uses simple raycast-based fall (no full physics — just downward movement).
        ApplyDeadBodyGravity();

        if (Time.time >= _respawnTime)
        {
            Respawn();
        }
    }

    /// <summary>
    /// Simple gravity for dead bot bodies — makes them fall to the ground
    /// instead of floating in mid-air where they died.
    /// </summary>
    private void ApplyDeadBodyGravity()
    {
        Vector3 pos = transform.position;

        // Check if there's ground below
        RaycastHit hit;
        float checkDist = 50f * Time.deltaTime + 0.5f;
        if (Physics.Raycast(pos + Vector3.up * 0.1f, Vector3.down, out hit, checkDist,
            ~((1 << 18) | (1 << 20)), QueryTriggerInteraction.Ignore))
        {
            // Ground found — snap to it if above
            if (pos.y > hit.point.y + 0.05f)
            {
                pos.y = Mathf.MoveTowards(pos.y, hit.point.y, 50f * Time.deltaTime);
            }
        }
        else
        {
            // No ground — free fall
            pos.y -= 50f * Time.deltaTime;
        }

        transform.position = pos;
    }

    // ================================================================
    // Match End — freeze bots and auto-ready for next round
    // ================================================================

    private bool CheckMatchRunning()
    {
        try
        {
            if (GameState.CurrentGame == null) return true;

            // In team modes, IsMatchRunning may never be set to true if there's
            // no server to fire OnMatchStart. Check if the player is alive and in-game
            // as a fallback — if the player is playing, bots should be active too.
            if (GameState.CurrentGame.IsMatchRunning) return true;

            // Fallback: if we have a local character that's alive, the match is effectively running
            if (GameState.LocalCharacter != null && GameState.LocalCharacter.IsAlive)
                return true;

            return false;
        }
        catch { return true; } // Assume running if can't check
    }

    /// <summary>
    /// Freeze all bot subsystems when match ends. Respawn dead bots so they
    /// appear standing on the end-of-match screen (matches real player behavior).
    /// </summary>
    private void FreezeForMatchEnd()
    {
        _matchEndFrozen = true;
        _matchEndReadyTime = Time.time + Random.Range(1f, 3f); // Staggered ready click

        // Stop movement and combat
        if (_navigation != null) _navigation.Stop();
        if (_weaponHandler != null) _weaponHandler.enabled = false;

        // If dead, respawn so bot appears on end-of-match screen
        if (_state == BotState.Dead || _health <= 0)
        {
            _health = BotConfig.MaxHealth;
            _armor = BotConfig.GetLoadoutArmor(_botIndex);
            gameObject.layer = (int)UberstrikeLayer.RemotePlayer;

            if (_decorator != null)
                _decorator.gameObject.SetActive(true);
            if (_fallbackAvatar != null)
                _fallbackAvatar.SetActive(true);

            if (_characterInfo != null)
            {
                _characterInfo.PlayerState &= ~(PlayerStates)0x02; // Clear Dead flag
                _characterInfo.Health = BotConfig.MaxHealth;
            }
        }

        _state = BotState.Idle;

        // Play idle animation
        if (_decorator != null && _decorator.AnimationController != null)
        {
            _decorator.AnimationController.PlayAnimation(AnimationIndex.idle);
            _decorator.AnimationController.UpdateAnimation();
        }

        Debug.Log("[Bot] " + BotName + " frozen for match end");
    }

    /// <summary>
    /// Auto-click "Ready" for next round after a staggered delay (1-3s).
    /// Sets CharacterInfo.IsReadyForGame and updates the ready counter.
    /// </summary>
    private void UpdateMatchEndReady()
    {
        if (_characterInfo == null) return;
        if (_characterInfo.IsReadyForGame) return;

        if (Time.time >= _matchEndReadyTime)
        {
            _characterInfo.IsReadyForGame = true;

            try
            {
                if (GameState.CurrentGame != null)
                    GameState.CurrentGame.UpdatePlayerReadyForNextRound();
            }
            catch (System.Exception) { }

            Debug.Log("[Bot] " + BotName + " is ready for next match");
        }
    }

    /// <summary>
    /// Restore normal bot behavior when a new match starts.
    /// </summary>
    private void UnfreezeFromMatchEnd()
    {
        _matchEndFrozen = false;

        // Reset match stats for new round (idempotent — called per bot but safe)
        TotalBotKills = 0;
        try
        {
            if (GameState.LocalCharacter != null)
            {
                GameState.LocalCharacter.Kills = 0;
                GameState.LocalCharacter.Deaths = 0;
            }
        }
        catch (System.Exception) { }

        if (_characterInfo != null)
            _characterInfo.IsReadyForGame = false;

        if (_weaponHandler != null) _weaponHandler.enabled = true;

        _health = BotConfig.MaxHealth;
        _armor = BotConfig.GetLoadoutArmor(_botIndex);
        _graceTimer = BotConfig.SpawnGraceTime;

        // Respawn at a fresh position
        Vector3 pos;
        Quaternion rot;
        if (SpawnPointManager.Instance != null)
        {
            SpawnPointManager.Instance.GetRandomSpawnPoint(out pos, out rot);
        }
        else
        {
            pos = transform.position;
            rot = transform.rotation;
        }

        transform.position = pos + Vector3.up;
        transform.rotation = rot;

        if (_navigation != null)
        {
            _navigation.SnapToGround();
            _navigation.Resume();
        }

        if (_agent != null)
        {
            _agent.enabled = true;
            if (_agent.isOnNavMesh)
                _agent.Warp(transform.position);
        }

        if (_characterInfo != null)
        {
            _characterInfo.PlayerState &= ~(PlayerStates)0x02;
            _characterInfo.Health = BotConfig.MaxHealth;
            _characterInfo.Kills = 0;
            _characterInfo.Deaths = 0;
        }

        SetState(BotState.Patrol);
        Debug.Log("[Bot] " + BotName + " unfrozen for new match");
    }

    private void Respawn()
    {
        Vector3 pos;
        Quaternion rot;
        if (SpawnPointManager.Instance != null)
        {
            SpawnPointManager.Instance.GetRandomSpawnPoint(out pos, out rot);
        }
        else
        {
            pos = transform.position + Random.insideUnitSphere * 10f;
            pos.y = transform.position.y;
            rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        transform.position = pos + Vector3.up;
        transform.rotation = rot;

        _health = BotConfig.MaxHealth;
        _armor = BotConfig.GetLoadoutArmor(_botIndex);
        _graceTimer = BotConfig.SpawnGraceTime;
        PendingAttackerName = null;
        PendingAttackBodyPart = BodyPart.Body;
        _lastHitBodyPart = BodyPart.Body;
        _lastDamageTime = 0f;
        _killingBlowFromPlayer = false;

        // Scoreboard: mark alive again
        if (_characterInfo != null)
        {
            _characterInfo.PlayerState &= ~(PlayerStates)0x02; // Clear Dead flag
            _characterInfo.Health = BotConfig.MaxHealth;
        }

        gameObject.layer = (int)UberstrikeLayer.RemotePlayer;

        if (_decorator != null)
        {
            _decorator.gameObject.SetActive(true);
            if (_decorator.HudInformation != null)
                _decorator.HudInformation.SetHealthBarValue(1f);
        }
        if (_fallbackAvatar != null)
            _fallbackAvatar.SetActive(true);

        if (_navigation != null && _navigation.HasNavMesh)
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.enabled = true;
                _agent.Warp(transform.position);
            }
        }

        // Snap to ground so bot doesn't float after respawn
        if (_navigation != null)
            _navigation.SnapToGround();

        _navigation.Resume();
        SetState(BotState.Patrol);
    }
}
