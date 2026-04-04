using UnityEngine;
using UberStrike.Core.Types;
using UberStrike.DataCenter.Common.Entities;
using UberStrike.Realtime.Common;

/// <summary>
/// Raycast combat system for bots with Quick Switch support.
/// Fires at targets with configurable aim error, fire rate, and damage.
/// Supports hitting both local player (layer 18) and other bots (layer 20).
///
/// Visual effects: Uses the weapon decorator's ShowShootEffect() pipeline
/// (same as remote players via WeaponSimulator) for muzzle flash, bullet trail,
/// impact particles, and shoot sound — all matched to the equipped weapon.
///
/// Quick Switch (matched to 4.3.8 WeaponController):
///   - Each weapon slot has INDEPENDENT fire rate timers (NextShootTime per slot)
///   - After firing, bot switches to next weapon after 0.2s (4.3.8 _weaponSwitchTimeout)
///   - This allows firing faster overall by alternating weapons
///   - Scoreboard weapon display updates on each switch
/// </summary>
public class BotWeaponHandler : MonoBehaviour
{
    private BotController _bot;
    private float _reactionTimer;
    private bool _hasReacted;

    // Quick Switch: multiple weapon slots with independent fire timers
    private WeaponSlotData[] _weaponSlots;
    private int _currentSlotIndex;
    private float _switchCooldown;
    private float _lastSwitchTime;
    // Full weapon switch takes ~0.5s in the original game (0.2s internal + raise animation).
    // Bots use 0.5s to feel realistic — not the instant quick-switch that bypasses fire rates.
    private const float SWITCH_TIMEOUT = 0.5f;

    // Shot counter offset high to avoid collision with player shot IDs
    private int _shotCounter = 10000;

    // Combined layer mask: hits both LocalPlayer (18) AND RemotePlayer (20) + geometry
    private int _combinedShootMask;

    // Recoil system: accumulates per-shot, recovers over time
    private Vector2 _recoilOffset;      // Current accumulated recoil (pitch, yaw in degrees)
    private float _lastShotTime;        // For recoil recovery timing

    /// <summary>
    /// The last computed aim direction — used by BotController.UpdateRotation()
    /// to align the bot's visual orientation with the actual shot direction.
    /// </summary>
    public Vector3 LastAimDirection { get; private set; }

    /// <summary>Current weapon ID (for scoreboard display).</summary>
    public int CurrentWeaponId => _weaponSlots != null && _weaponSlots.Length > 0
        ? _weaponSlots[_currentSlotIndex].weaponId : 0;

    /// <summary>Current weapon slot index (0-based, for scoreboard CurrentWeaponSlot).</summary>
    public int CurrentSlotIndex => _currentSlotIndex;

    /// <summary>Number of weapons this bot carries.</summary>
    public int WeaponCount => _weaponSlots != null ? _weaponSlots.Length : 0;

    /// <summary>Get weapon ID at a specific slot index.</summary>
    public int GetWeaponIdAtSlot(int slot)
    {
        if (_weaponSlots == null || slot < 0 || slot >= _weaponSlots.Length) return 0;
        return _weaponSlots[slot].weaponId;
    }

    private struct WeaponSlotData
    {
        public int weaponId;
        public short damage;
        public float fireRate;
        public UberstrikeItemClass weaponClass;
        public float nextShootTime;  // Independent per-slot fire timer
        public bool isExplosive;
    }

    public void Initialize(BotController bot)
    {
        _bot = bot;

        // Build combined mask: geometry + LocalPlayer + RemotePlayer
        _combinedShootMask = (1 << 0) // Default (geometry)
            | (1 << (int)UberstrikeLayer.LocalPlayer)
            | (1 << (int)UberstrikeLayer.RemotePlayer);
    }

    /// <summary>
    /// Set up multiple weapons for Quick Switch. Called by BotController after weapon IDs are assigned.
    /// Uses REAL weapon data from the shop database for fire rate, damage, and class —
    /// same stats that real players get. Falls back to BotConfig.GetWeaponStats if shop data unavailable.
    /// </summary>
    public void SetWeapons(int[] weaponIds)
    {
        _weaponSlots = new WeaponSlotData[weaponIds.Length];
        for (int i = 0; i < weaponIds.Length; i++)
        {
            short dmg; float rate; UberstrikeItemClass wc;

            // Try to get REAL weapon stats from shop database (loaded from server/bundles)
            WeaponItem shopWeapon = ItemManager.Instance.GetWeaponItemInShop(weaponIds[i]);
            if (shopWeapon != null && shopWeapon.Configuration != null)
            {
                // Use actual game stats — RateOfFire is in milliseconds, convert to seconds
                dmg = (short)shopWeapon.Configuration.DamagePerProjectile;
                rate = shopWeapon.Configuration.RateOfFire / 1000f;
                wc = shopWeapon.Configuration.ItemClass;

                // Shotgun: multiply damage by pellets for single-raycast simulation
                if (shopWeapon.Configuration.ProjectilesPerShot > 1)
                    dmg = (short)(dmg * Mathf.Min(shopWeapon.Configuration.ProjectilesPerShot, 4));

                // Sanity clamp: prevent zero or negative fire rates
                if (rate < 0.05f) rate = 0.05f;

                Debug.Log("[BotWeapon] " + shopWeapon.Name + " (ID " + weaponIds[i]
                    + "): dmg=" + dmg + " rate=" + rate.ToString("F3") + "s class=" + wc);
            }
            else
            {
                // Fallback to hardcoded class-based stats
                BotConfig.GetWeaponStats(weaponIds[i], out dmg, out rate, out wc);
                Debug.LogWarning("[BotWeapon] ID " + weaponIds[i] + " not in shop — using fallback stats");
            }

            _weaponSlots[i] = new WeaponSlotData
            {
                weaponId = weaponIds[i],
                damage = dmg,
                fireRate = rate,
                weaponClass = wc,
                nextShootTime = 0f,
                isExplosive = wc == UberstrikeItemClass.WeaponCannon
                    || wc == UberstrikeItemClass.WeaponLauncher
                    || wc == UberstrikeItemClass.WeaponSplattergun
            };
        }
        _currentSlotIndex = 0;
    }

    /// <summary>
    /// Legacy single-weapon setup (backwards compatibility).
    /// </summary>
    public void SetWeapon(int weaponId)
    {
        SetWeapons(new int[] { weaponId });
    }

    public void FireAtTarget(Transform target)
    {
        if (target == null || _weaponSlots == null || _weaponSlots.Length == 0) return;

        // Always compute aim direction (even before firing) so visual rotation stays correct
        Vector3 origin = GetShootOrigin();
        Vector3 aimPoint = GetTargetAimPoint(target);
        Vector3 toTarget = (aimPoint - origin).normalized;

        // Recover recoil over time (bots "pull down" like real players)
        float timeSinceShot = Time.time - _lastShotTime;
        float recoveryRate = GetRecoilRecovery();
        if (timeSinceShot > 0f)
        {
            float recovery = recoveryRate * Time.deltaTime;
            _recoilOffset.x = Mathf.MoveTowards(_recoilOffset.x, 0f, recovery);
            _recoilOffset.y = Mathf.MoveTowards(_recoilOffset.y, 0f, recovery);
        }

        // Apply current recoil offset to the aim direction (visual + actual)
        Vector3 recoiledDir = ApplyRecoilOffset(toTarget);
        LastAimDirection = recoiledDir;

        // Reaction delay — simulate human perception lag on first sight
        if (!_hasReacted)
        {
            _reactionTimer += Time.deltaTime;
            if (_reactionTimer < GetReactionDelay())
                return;
            _hasReacted = true;
        }

        // Switch cooldown — 0.2s lockout after weapon switch (matches 4.3.8 _weaponSwitchTimeout)
        if (Time.time - _lastSwitchTime < SWITCH_TIMEOUT)
            return;

        ref WeaponSlotData slot = ref _weaponSlots[_currentSlotIndex];

        // Check if current weapon can fire (per-slot independent timer)
        if (Time.time < slot.nextShootTime)
        {
            // Only quick-switch for slow weapons (fire rate > 0.5s = sniper, cannon, launcher).
            // Fast weapons (MG, shotgun, handgun) just wait for their cooldown — switching
            // would be slower than waiting for the next shot.
            float remainingCooldown = slot.nextShootTime - Time.time;
            bool worthSwitching = remainingCooldown > SWITCH_TIMEOUT
                && Time.time - _lastShotTime > BotConfig.QuickSwitchDelay;

            if (worthSwitching && _weaponSlots.Length > 1)
            {
                // Find next weapon that's ready to fire
                for (int i = 1; i < _weaponSlots.Length; i++)
                {
                    int nextIdx = (_currentSlotIndex + i) % _weaponSlots.Length;
                    if (Time.time >= _weaponSlots[nextIdx].nextShootTime)
                    {
                        SwitchToSlot(nextIdx);
                        return; // Will fire after switch timeout
                    }
                }
            }
            return; // Wait for current weapon cooldown
        }

        // Fire!
        slot.nextShootTime = Time.time + slot.fireRate;
        _lastShotTime = Time.time;

        // Use the recoil-affected direction for the actual shot
        Vector3 direction = recoiledDir;

        // Add recoil kick from this shot (accumulates — sustained fire gets worse)
        float recoilKick = GetRecoilPerShot();
        _recoilOffset.x += recoilKick * Random.Range(0.6f, 1.0f);  // Mostly upward kick
        _recoilOffset.y += recoilKick * Random.Range(-0.4f, 0.4f); // Small random horizontal
        // Clamp max recoil so bots don't shoot at the sky
        float maxRecoil = recoilKick * 5f; // Max ~5 shots worth of accumulated offset
        _recoilOffset.x = Mathf.Clamp(_recoilOffset.x, -maxRecoil, maxRecoil);
        _recoilOffset.y = Mathf.Clamp(_recoilOffset.y, -maxRecoil, maxRecoil);

        // Raycast using combined mask, IGNORE triggers (bot root has trigger capsule)
        RaycastHit hit;
        bool didHit = Physics.Raycast(origin, direction, out hit, 1000f, _combinedShootMask,
            QueryTriggerInteraction.Ignore);

        if (didHit)
        {
            // Check for CharacterHitArea (players and bots) — same path as player weapons
            var hitArea = hit.collider.GetComponent<CharacterHitArea>();
            if (hitArea != null && hitArea.Shootable != null)
            {
                // Friendly fire check: skip damage if hit target is on the same team
                bool isFriendly = false;
                int hitLayer = hit.collider.gameObject.layer;
                if (_bot != null && _bot._characterInfo != null && _bot._characterInfo.TeamID != TeamID.NONE)
                {
                    if (hitLayer == (int)UberstrikeLayer.LocalPlayer)
                    {
                        isFriendly = GameState.LocalCharacter != null
                            && GameState.LocalCharacter.TeamID == _bot._characterInfo.TeamID;
                    }
                    else if (hitLayer == (int)UberstrikeLayer.RemotePlayer)
                    {
                        var hitBot = hit.collider.GetComponentInParent<BotController>();
                        if (hitBot != null && hitBot._characterInfo != null)
                            isFriendly = hitBot._characterInfo.TeamID == _bot._characterInfo.TeamID;
                    }
                }

                if (!isFriendly)
                {
                    // Debug overrides: skip damage to local player if active
                    if (hitLayer == (int)UberstrikeLayer.LocalPlayer
                        && DebugOverrideRegistry.Current.ShouldBlockBotDamageToPlayer)
                    {
                        goto SkipDamage;
                    }

                    short effectiveDamage = (short)(slot.damage * GetDamageMultiplier());
                    DamageInfo dmg = new DamageInfo(effectiveDamage);
                    dmg.Force = direction * 5f;
                    dmg.Hitpoint = hit.point;
                    dmg.ShotID = _shotCounter++;
                    dmg.WeaponID = slot.weaponId;
                    dmg.WeaponClass = slot.weaponClass;
                    dmg.BodyPart = hitArea.CharacterBodyPart;

                    // No critical strike bonus for explosive weapons (cannon, launcher, splattergun)
                    // Explosions can't headshot — only direct hitscan weapons get crit bonus
                    if (slot.weaponClass == UberstrikeItemClass.WeaponCannon
                        || slot.weaponClass == UberstrikeItemClass.WeaponLauncher
                        || slot.weaponClass == UberstrikeItemClass.WeaponSplattergun)
                    {
                        dmg.CriticalStrikeBonus = 0f;
                    }
                    else
                    {
                        dmg.CriticalStrikeBonus = BotConfig.CriticalStrikeBonus;
                    }

                    dmg.DamageEffectFlag = DamageEffectType.None;
                    dmg.DamageEffectValue = 0f;

                    // Track attacker BEFORE applying damage (flags consumed in same call stack)
                    if (hitLayer == (int)UberstrikeLayer.LocalPlayer)
                    {
                        // Bot hit the local player — track for player death screen
                        BotController.LastBotAttacker = _bot;
                        BotController.LastBotAttackBodyPart = hitArea.CharacterBodyPart;
                    }
                    else if (hitLayer == (int)UberstrikeLayer.RemotePlayer)
                    {
                        // Bot hit another bot — track attacker for bot-vs-bot kill feed
                        var victimBot = hit.collider.GetComponentInParent<BotController>();
                        if (victimBot != null && victimBot != _bot)
                        {
                            victimBot.PendingAttackerName = _bot.BotName;
                            victimBot.PendingAttackerIsCurrentHit = true;
                            victimBot.PendingAttackBodyPart = hitArea.CharacterBodyPart;
                        }
                    }

                    // Apply damage through IShootable (same as player weapon path)
                    hitArea.Shootable.ApplyDamage(dmg);

                    SkipDamage:;
                }
            }
        }

        // ================================================================
        // Weapon Decorator Visual Effects (muzzle flash, trail, impacts, sound)
        // Uses the same ShowShootEffect() pipeline as remote players via WeaponSimulator.
        // ================================================================
        var decorator = _bot.GetActiveWeaponDecorator();
        if (decorator != null)
        {
            if (!slot.isExplosive)
            {
                // Hitscan: ShowShootEffect triggers muzzle flash, bullet trail,
                // impact particles (surface-dependent), and weapon shoot sound
                RaycastHit[] effectHits = didHit ? new RaycastHit[] { hit } : new RaycastHit[0];
                decorator.ShowShootEffect(effectHits);
            }
            else
            {
                // Explosive weapons: play shoot sound only (explosion effect is
                // handled by ProjectileDetonator on impact, not at fire time)
                decorator.PlayShootSound();
            }
        }
        else
        {
            Debug.LogWarning("[Bot] " + _bot.BotName + " weapon decorator is NULL for slot "
                + _currentSlotIndex + " (weaponId=" + slot.weaponId + ") — VFX skipped");
        }

        // Fallback debug tracer: always visible in editor Scene view (persists 0.5s)
        // Useful for diagnosing aim direction even when VFX pipeline fails
        Color traceColor = didHit ? Color.red : Color.yellow;
        Vector3 traceEnd = didHit ? hit.point : origin + direction * 200f;
        Debug.DrawLine(origin, traceEnd, traceColor, 0.5f);

        // Quick Switch: only switch AFTER current weapon goes on cooldown
        // (handled at the top of this method on the next call when nextShootTime hasn't elapsed).
        // Don't blindly switch after every shot — it causes constant weapon swap animation.
    }

    /// <summary>
    /// Switch to a different weapon slot. Applies 0.2s fire lockout (matching 4.3.8).
    /// Notifies BotController to update scoreboard weapon display.
    /// </summary>
    private void SwitchToSlot(int slotIndex)
    {
        if (slotIndex == _currentSlotIndex) return;
        _currentSlotIndex = slotIndex;
        _lastSwitchTime = Time.time;

        // Notify bot controller to update scoreboard + visual weapon
        if (_bot != null)
            _bot.OnWeaponSwitched(_currentSlotIndex, _weaponSlots[_currentSlotIndex].weaponId);
    }

    /// <summary>
    /// Reset reaction timer when losing sight of target.
    /// Called by BotController on target change.
    /// </summary>
    public void ResetReaction()
    {
        _hasReacted = false;
        _reactionTimer = 0f;
        _recoilOffset = Vector2.zero; // Fresh aim on new target
    }

    private Vector3 GetShootOrigin()
    {
        return transform.position + Vector3.up * 1.4f; // eye height
    }

    /// <summary>
    /// Compute aim point on target based on their actual collider bounds.
    /// Accounts for crouching (smaller collider), jumping (elevated position),
    /// and any other stance changes. Falls back to +0.8f if no collider found.
    /// </summary>
    private Vector3 GetTargetAimPoint(Transform target)
    {
        // Try CharacterController first (local player uses one)
        var cc = target.GetComponent<CharacterController>();
        if (cc != null)
        {
            return target.position + cc.center;
        }

        // Try CapsuleCollider (other bots use one)
        var capsule = target.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            return target.position + capsule.center;
        }

        // Try generic collider bounds center
        var col = target.GetComponent<Collider>();
        if (col != null)
        {
            return col.bounds.center;
        }

        // Fallback: approximate chest height
        return target.position + Vector3.up * 0.8f;
    }

    /// <summary>
    /// Apply current accumulated recoil offset to the aim direction.
    /// Recoil is pitch (up) + yaw (left/right) in degrees, applied via quaternion rotation.
    /// </summary>
    private Vector3 ApplyRecoilOffset(Vector3 dir)
    {
        if (_recoilOffset.sqrMagnitude < 0.001f) return dir;

        // Pitch axis = perpendicular horizontal (rotates up/down)
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
        if (right.sqrMagnitude < 0.01f)
            right = Vector3.right;

        // Apply pitch (up kick) then yaw (horizontal drift)
        dir = Quaternion.AngleAxis(_recoilOffset.x, right) * dir;
        dir = Quaternion.AngleAxis(_recoilOffset.y, Vector3.up) * dir;
        return dir.normalized;
    }

    // ================================================================
    // Difficulty-aware getters
    // ================================================================

    /// <summary>
    /// Recoil per shot in degrees. Easy bots kick hard, hard bots barely kick.
    /// </summary>
    private float GetRecoilPerShot()
    {
        if (_bot == null) return 2.0f;
        switch (_bot.Difficulty)
        {
            case BotDifficulty.Easy:   return 4.0f;  // Heavy recoil, shots climb fast
            case BotDifficulty.Hard:   return 1.0f;  // Tight control
            default:                   return 2.0f;  // Medium
        }
    }

    /// <summary>
    /// Recoil recovery rate in degrees/second. Easy bots recover slowly (bad recoil control),
    /// hard bots snap back fast (like a skilled player pulling down).
    /// </summary>
    private float GetRecoilRecovery()
    {
        if (_bot == null) return 8f;
        switch (_bot.Difficulty)
        {
            case BotDifficulty.Easy:   return 4f;   // Slow recovery — stays inaccurate longer
            case BotDifficulty.Hard:   return 15f;  // Fast recovery — near-instant correction
            default:                   return 8f;   // Medium
        }
    }

    private float GetReactionDelay()
    {
        if (_bot != null && _bot.Difficulty != BotDifficulty.Medium)
            return _bot.DifficultyStats.ReactionDelay;
        return BotConfig.ReactionDelay;
    }

    private float GetDamageMultiplier()
    {
        if (_bot != null && _bot.Difficulty != BotDifficulty.Medium)
            return _bot.DifficultyStats.DamageMultiplier;
        return 1.0f;
    }
}
