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

    /// <summary>
    /// CharacterInfo registered in the game's Players dict for scoreboard display.
    /// </summary>
    private UberStrike.Realtime.Common.CharacterInfo _characterInfo;

    // ================================================================
    // IShootable Implementation
    // ================================================================

    public void ApplyDamage(DamageInfo shot)
    {
        if (_health <= 0 || _graceTimer > 0f) return;

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
        BotName = botName;
        _botIndex = botIndex;
        _health = BotConfig.MaxHealth;
        _armor = BotConfig.GetLoadoutArmor(_botIndex);
        _graceTimer = BotConfig.SpawnGraceTime;

        // Assign multiple weapons for Quick Switch (3 weapons per bot, rotated by index)
        int weaponCount = Mathf.Min(BotConfig.WeaponsPerBot, BotConfig.WeaponIds.Length);
        AllWeaponIds = new int[weaponCount];
        for (int i = 0; i < weaponCount; i++)
        {
            AllWeaponIds[i] = BotConfig.WeaponIds[(_botIndex + i) % BotConfig.WeaponIds.Length];
        }
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
            EventStreamHud.Instance.AddEventText(BotName, TeamID.NONE, "joined the game");
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
            _characterInfo.TeamID = TeamID.NONE;
            _characterInfo.Cmid = -(900 + _botIndex); // negative = bot
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

        // Set up HUD name tag
        if (_decorator.HudInformation != null)
        {
            _decorator.HudInformation.SetAvatarLabel(BotName);
            _decorator.HudInformation.SetHealthBarValue(1f);
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

        // Check if avatar has a weapon attach point (serialized on prefab)
        if (_decorator.WeaponAttachPoint == null)
        {
            Debug.LogWarning("[Bot] No WeaponAttachPoint on avatar for " + BotName);
            return;
        }

        // Create visual weapon models for ALL Quick Switch weapons
        _weaponDecorators = new BaseWeaponDecorator[AllWeaponIds.Length];
        LoadoutSlotType[] slots = new LoadoutSlotType[]
        {
            LoadoutSlotType.WeaponPrimary,
            LoadoutSlotType.WeaponSecondary,
            LoadoutSlotType.WeaponTertiary
        };

        for (int i = 0; i < AllWeaponIds.Length && i < slots.Length; i++)
        {
            try
            {
                var weaponGo = ItemManager.Instance.Instantiate(AllWeaponIds[i]);
                if (weaponGo == null)
                {
                    Debug.LogWarning("[Bot] Failed to instantiate weapon " + AllWeaponIds[i]);
                    continue;
                }

                var weaponDeco = weaponGo.GetComponent<BaseWeaponDecorator>();
                if (weaponDeco == null)
                {
                    Object.Destroy(weaponGo);
                    continue;
                }

                _weaponDecorators[i] = weaponDeco;

                // Assign to weapon slot — parents to WeaponAttachPoint automatically
                _decorator.AssignWeapon(slots[i], weaponDeco);

                // Ensure weapon is on the same layer as the bot
                LayerUtil.SetLayerRecursively(weaponGo.transform, UberstrikeLayer.RemotePlayer);

                // Only show the first weapon initially, hide the rest
                weaponGo.SetActive(i == 0);

                Debug.Log("[Bot] Equipped weapon slot " + i + ": " + AllWeaponIds[i] + " on " + BotName);
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
    /// Show the weapon model for the given slot index. Hides all others.
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

        UpdateGraceTimer();
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

        // Check local player
        Transform player = GetLocalPlayerTransform();
        if (player != null)
        {
            float playerDist = Vector3.Distance(transform.position, player.position);
            if (playerDist <= BotConfig.SightDistance && CanSeeTarget(player, playerDist))
            {
                bestTarget = player;
                bestDist = playerDist;
            }
        }

        // Check other bots
        for (int i = 0; i < AllBots.Count; i++)
        {
            var otherBot = AllBots[i];
            if (otherBot == null || otherBot == this || otherBot.Health <= 0) continue;

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
                _navigation.PickRandomPatrolPoint(transform.position);
                break;
            case BotState.Chase:
                _navigation.Resume();
                break;
            case BotState.Combat:
                _navigation.Resume(); // Keep moving — bots strafe during combat
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

        if (_navigation.HasReachedDestination)
        {
            if (_stateTimer > BotConfig.PatrolWaitTime)
            {
                _navigation.PickRandomPatrolPoint(transform.position);
                _stateTimer = 0f;
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

        _weaponHandler.FireAtTarget(_currentTarget);

        // Strafe during combat (all modes — NavMesh and fallback)
        Vector3 toTarget = (_currentTarget.position - transform.position).normalized;
        Vector3 strafeDir = Vector3.Cross(toTarget, Vector3.up);
        float strafeSign = Mathf.Sin(Time.time * 1.5f);
        Vector3 strafeTarget = transform.position + strafeDir * strafeSign * 3f;
        _navigation.SetDestination(strafeTarget);
    }

    // ================================================================
    // Rotation — face target or movement direction
    // ================================================================

    private void UpdateRotation()
    {
        Vector3 lookDir = Vector3.zero;

        if (_currentTarget != null && (_state == BotState.Chase || _state == BotState.Combat))
        {
            // Use the weapon handler's actual aim direction for precise visual alignment
            // This prevents the "aiming at ceiling" bug where body faces different direction than shots
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
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }

    // ================================================================
    // Animation
    // ================================================================

    private void UpdateAnimation()
    {
        if (_decorator == null || _decorator.AnimationController == null) return;

        float speed = _navigation != null ? _navigation.CurrentSpeed : 0f;
        bool jumping = _navigation != null && (_navigation.IsJumping || _navigation.IsLaunched);
        bool crouching = _navigation != null && _navigation.IsCrouching;

        if (jumping)
            _decorator.AnimationController.PlayAnimation(AnimationIndex.jumpUp);
        else if (crouching)
        {
            // Crouch animation: "crouch" when moving, "squat" when idle
            if (speed > 0.2f)
                _decorator.AnimationController.PlayAnimation(AnimationIndex.crouch);
            else
                _decorator.AnimationController.PlayAnimation(AnimationIndex.squat);
        }
        else if (speed > 1f)
            _decorator.AnimationController.PlayAnimation(AnimationIndex.run);
        else if (speed > 0.2f)
            _decorator.AnimationController.PlayAnimation(AnimationIndex.walk);
        else
            _decorator.AnimationController.PlayAnimation(AnimationIndex.idle);

        // CRITICAL: UpdateAnimation() must be called every frame to actually blend
        // Animation.Blend() calls. PlayAnimation() only sets EndTime — the controller's
        // UpdateAnimation() does the actual Animation.Blend() work.
        _decorator.AnimationController.UpdateAnimation();
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

        // Determine if killed by player or another bot
        bool killedByBot = !string.IsNullOrEmpty(PendingAttackerName);
        string killerName = killedByBot ? PendingAttackerName : null;
        BodyPart killBodyPart = killedByBot ? PendingAttackBodyPart : _lastHitBodyPart;

        if (isEnvDeath)
        {
            // Environment death (death zone, fall off map) — no one gets kill credit
            // Show suicide in kill feed
            try
            {
                EventStreamHud.Instance.AddEventText(BotName, TeamID.NONE,
                    "killed themself", "", TeamID.NONE);
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
        else
        {
            // Killed by local player — award XP and show in kill feed
            AwardKillXp();
            ShowKillFeed(null, killBodyPart, _lastHitWeaponClass);
        }

        // Update scoreboard: mark dead + increment deaths
        if (_characterInfo != null)
        {
            _characterInfo.Deaths++;
            _characterInfo.PlayerState |= (PlayerStates)0x02; // Dead flag
        }

        // Clear attacker tracking
        PendingAttackerName = null;
        PendingAttackBodyPart = BodyPart.Body;

        if (_decorator != null)
        {
            if (_decorator.AnimationController != null)
            {
                _decorator.AnimationController.TriggerAnimation(AnimationIndex.die1, true);
            }
            Invoke(nameof(HideAvatar), 1.5f);
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

            // Build kill verb based on weapon class and body part
            string verb = "killed";
            if (weaponClass == UberstrikeItemClass.WeaponMelee)
                verb = "smacked";
            else if ((bodyPart & BodyPart.Head) != 0)
                verb = "headshot";
            else if ((bodyPart & BodyPart.Nuts) != 0)
                verb = "nutshot";

            EventStreamHud.Instance.AddEventText(killer, TeamID.NONE, verb, BotName, TeamID.NONE);
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

            EventStreamHud.Instance.AddEventText(killerName, TeamID.NONE, verb, playerName, TeamID.NONE);
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

        if (Time.time >= _respawnTime)
        {
            Respawn();
        }
    }

    // ================================================================
    // Match End — freeze bots and auto-ready for next round
    // ================================================================

    private bool CheckMatchRunning()
    {
        try
        {
            return GameState.CurrentGame == null || GameState.CurrentGame.IsMatchRunning;
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
