using UnityEngine;
using UberStrike.Core.Types;
using UberStrike.DataCenter.Common.Entities;
using UberStrike.Realtime.Common;

/// <summary>
/// Raycast combat system for bots with Quick Switch support.
/// Fires at targets with configurable aim error, fire rate, and damage.
/// Supports hitting both local player (layer 18) and other bots (layer 20).
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
    private float _switchCooldown;           // 0.2s lockout after switch (matches 4.3.8)
    private float _lastSwitchTime;
    private const float SWITCH_TIMEOUT = 0.2f; // _weaponSwitchTimeout from original

    // Shot counter offset high to avoid collision with player shot IDs
    private int _shotCounter = 10000;

    // Combined layer mask: hits both LocalPlayer (18) AND RemotePlayer (20) + geometry
    private int _combinedShootMask;

    // Tracer line rendering (hitscan weapons only)
    private LineRenderer _tracer;
    private float _tracerEndTime;
    private static readonly float TracerDuration = 0.08f;

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

        SetupTracer();
    }

    /// <summary>
    /// Set up multiple weapons for Quick Switch. Called by BotController after weapon IDs are assigned.
    /// Each weapon gets its own fire rate timer for independent cooldowns.
    /// </summary>
    public void SetWeapons(int[] weaponIds)
    {
        _weaponSlots = new WeaponSlotData[weaponIds.Length];
        for (int i = 0; i < weaponIds.Length; i++)
        {
            short dmg; float rate; UberstrikeItemClass wc;
            BotConfig.GetWeaponStats(weaponIds[i], out dmg, out rate, out wc);

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

    void Update()
    {
        // Fade out tracer after duration
        if (_tracer != null && _tracer.enabled && Time.time > _tracerEndTime)
        {
            _tracer.enabled = false;
        }
    }

    public void FireAtTarget(Transform target)
    {
        if (target == null || _weaponSlots == null || _weaponSlots.Length == 0) return;

        // Always compute aim direction (even before firing) so visual rotation stays correct
        Vector3 origin = GetShootOrigin();
        Vector3 aimPoint = target.position + Vector3.up * 0.8f;
        Vector3 toTarget = (aimPoint - origin).normalized;
        LastAimDirection = toTarget;

        // Reaction delay — simulate human perception lag on first sight
        if (!_hasReacted)
        {
            _reactionTimer += Time.deltaTime;
            if (_reactionTimer < BotConfig.ReactionDelay)
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
            // Current weapon on cooldown — Quick Switch to next weapon!
            if (_weaponSlots.Length > 1)
            {
                // Find next weapon that's ready to fire
                for (int i = 1; i < _weaponSlots.Length; i++)
                {
                    int nextIdx = (_currentSlotIndex + i) % _weaponSlots.Length;
                    if (Time.time >= _weaponSlots[nextIdx].nextShootTime)
                    {
                        SwitchToSlot(nextIdx);
                        return; // Will fire on next frame after switch timeout
                    }
                }
            }
            return; // All weapons on cooldown
        }

        // Fire!
        slot.nextShootTime = Time.time + slot.fireRate;

        // Apply aim error to the direction
        Vector3 direction = ApplyAimError(toTarget);

        // Raycast using combined mask, IGNORE triggers (bot root has trigger capsule)
        RaycastHit hit;
        Vector3 endPoint = origin + direction * 200f;

        if (Physics.Raycast(origin, direction, out hit, 1000f, _combinedShootMask,
            QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;

            // Check for CharacterHitArea (players and bots) — same path as player weapons
            var hitArea = hit.collider.GetComponent<CharacterHitArea>();
            if (hitArea != null && hitArea.Shootable != null)
            {
                DamageInfo dmg = new DamageInfo(slot.damage);
                dmg.Force = direction * 5f;
                dmg.Hitpoint = hit.point;
                dmg.ShotID = _shotCounter++;
                dmg.WeaponID = slot.weaponId;
                dmg.WeaponClass = slot.weaponClass;
                dmg.BodyPart = hitArea.CharacterBodyPart;
                dmg.CriticalStrikeBonus = BotConfig.CriticalStrikeBonus;
                dmg.DamageEffectFlag = DamageEffectType.None;
                dmg.DamageEffectValue = 0f;

                // Track attacker BEFORE applying damage (flags consumed in same call stack)
                int hitLayer = hit.collider.gameObject.layer;

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
            }
        }

        // Show tracer line (hitscan weapons only — not cannon/launcher/splattergun)
        if (!slot.isExplosive)
            ShowTracer(origin, endPoint);

        // Quick Switch: after firing, switch to next weapon
        if (_weaponSlots.Length > 1)
        {
            int nextIdx = (_currentSlotIndex + 1) % _weaponSlots.Length;
            SwitchToSlot(nextIdx);
        }
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
    }

    private Vector3 GetShootOrigin()
    {
        return transform.position + Vector3.up * 1.4f; // eye height
    }

    private Vector3 ApplyAimError(Vector3 dir)
    {
        float error = BotConfig.AimErrorDegrees * Mathf.Deg2Rad;
        dir.x += Random.Range(-error, error);
        dir.y += Random.Range(-error, error);
        dir.z += Random.Range(-error, error);
        return dir.normalized;
    }

    // ================================================================
    // Tracer Line
    // ================================================================

    private void SetupTracer()
    {
        _tracer = gameObject.AddComponent<LineRenderer>();
        _tracer.positionCount = 2;
        _tracer.startWidth = 0.03f;
        _tracer.endWidth = 0.01f;
        _tracer.material = new Material(Shader.Find("Sprites/Default"));
        _tracer.startColor = new Color(1f, 0.9f, 0.3f, 0.8f); // yellow-orange
        _tracer.endColor = new Color(1f, 0.6f, 0.1f, 0.3f);
        _tracer.enabled = false;
    }

    private void ShowTracer(Vector3 from, Vector3 to)
    {
        if (_tracer == null) return;
        _tracer.SetPosition(0, from);
        _tracer.SetPosition(1, to);
        _tracer.enabled = true;
        _tracerEndTime = Time.time + TracerDuration;
    }
}
