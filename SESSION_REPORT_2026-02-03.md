# UberStrike Bot Debugging Session Report
**Date:** 2026-02-03
**Duration:** ~2 hours
**Agent:** Claude Code (Opus 4.5)
**Status:** ONGOING - Critical blocking issues remain

---

## Executive Summary

This session focused on fixing bot spawning, ground detection, and movement issues. Multiple iterations were attempted to solve the "bots flying into sky" problem, which was eventually traced to the ground raycast hitting the bot's own colliders. However, fixing this introduced the opposite problem (bots falling through floor). Player tracking was successfully implemented but bots still don't follow/move toward the player.

---

## Issues Addressed

### Issue #1: Bots Flying Into Sky (PARTIALLY FIXED)
**Symptom:** Bots spawn and instantly fly upward into the sky at high speed.

**Root Cause Discovered:** The gravity raycast was hitting the bot's own colliders (SphereCollider and body mesh colliders) and treating them as "ground". Since these colliders are ABOVE the bot's transform origin, the code would "snap up" to this fake ground, creating a feedback loop.

**Technical Details:**
```csharp
// PROBLEM: Raycast starting above bot, going down, hitting bot's own collider
Vector3 rayOrigin = new Vector3(targetPos.x, targetPos.y + 5f, targetPos.z);
// Bot's SphereCollider is at Y+1.0 with radius 0.6, so extends from Y+0.4 to Y+1.6
// Raycast hits this at Y+1.6, code snaps bot UP to Y+1.65
// Next frame, same thing happens, bot rises infinitely
```

**Attempted Fixes:**
1. Added check: if detected "ground" is above current position, ignore it
2. Changed layer mask to exclude bot's layer
3. Moved bot from Layer 0 to Layer 8
4. Set body clone recursively to Layer 8

**Current State:** Bots now fall THROUGH the floor instead of flying up. The ground raycast may now be missing actual ground geometry.

---

### Issue #2: Bots Not Following Player (PARTIALLY FIXED)
**Symptom:** Bots spawn and stare at player, rotating to track them, but don't move toward them.

**Root Cause:** Multiple issues:
1. `IsEnemy()` check was looking for "LocalPlayer" but player object is named "Player"
2. `Physics.OverlapSphere` might not detect player on Layer 25 (Controller)
3. Movement execution might be blocked by wall checks

**Fixes Applied:**
1. Added "Player" to IsEnemy name checks
2. Implemented direct player tracking via `GameObject.Find("Player")`
3. Player is now cached and always tracked within ViewDistance

**Current State:** Bots now correctly TRACK the player (rotate to face them) but still don't MOVE toward them. The state machine enters Combat/Search but ExecuteMovement() may have issues.

---

### Issue #3: Ground Detection / Layer System (CRITICAL BLOCKER)
**The Core Problem:** UberStrike uses a complex layer system, and finding the right configuration for both ground detection AND weapon hit detection is challenging.

**Layer Map (Discovered via DeepProbe):**
| Layer | Name | Usage |
|-------|------|-------|
| 0 | Default | Ground geometry, props |
| 2 | IgnoreRaycast | Non-hittable objects |
| 8 | Player | Player characters (attempted for bots) |
| 12 | Environment | Level geometry |
| 20 | RemotePlayer | Other players in multiplayer |
| 25 | Controller | LocalPlayer |
| 26 | Projectiles | Bullets, rockets |

**Attempted Layer Configurations:**
1. **Layer 0 (Default):** Weapons hit bot, but ground raycast also hits bot's colliders
2. **Layer 25 (Controller):** Weapons DON'T hit bot (not in weapon raycast mask)
3. **Layer 8 (Player):** Current attempt - weapons should hit, ground raycast excludes

**Current Raycast Mask:**
```csharp
int layerMask = ~((1 << 2) | (1 << 8) | (1 << 26));
// Excludes: IgnoreRaycast (2), Player (8), Projectiles (26)
// Includes: Default (0), Environment (12), etc.
```

**Problem:** Ground geometry layer is unknown. If ground is on Layer 0, it should work. If ground is on another layer or uses a different collision setup, bots fall through.

---

### Issue #4: FPS Drop (NOT INVESTIGATED)
**Symptom:** FPS drops from 200 to 20 when bots are active, especially when pressing F2.

**Likely Causes:**
1. Excessive logging (we added many Log() calls for diagnostics)
2. `Physics.OverlapSphere` called every frame for each bot
3. `FindObjectsOfType<BotController>()` called every frame in bot avoidance
4. Reflection-heavy damage system initialization

**Recommended Fix (Not Implemented):**
- Reduce logging frequency
- Cache OverlapSphere results
- Use spatial hashing instead of FindObjectsOfType
- Profile to identify actual bottleneck

---

## Code Changes Made This Session

### BotController.cs

**1. Gravity/Ground Snap Function (Multiple Iterations)**
```csharp
// CURRENT VERSION:
private void ApplyGravityAndGroundSnap(ref Vector3 targetPos)
{
    if (_isJumping) return;

    RaycastHit hit;
    // Exclude: IgnoreRaycast (2), Player/Bot (8), Projectiles (26)
    int layerMask = ~((1 << 2) | (1 << 8) | (1 << 26));

    Vector3 rayOrigin = new Vector3(targetPos.x, targetPos.y + 1.0f, targetPos.z);

    if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 100f, layerMask))
    {
        float groundY = hit.point.y;

        if (groundY > rayOrigin.y)
        {
            targetPos.y -= 15f * Time.deltaTime; // Fall
        }
        else
        {
            float targetY = groundY + 0.05f;
            // Snap/fall logic...
        }
    }
    else
    {
        targetPos.y -= 25f * Time.deltaTime; // No ground, fall fast
    }

    // Safety: respawn near player if fell too far
    if (targetPos.y < -100f)
    {
        GameObject player = GameObject.Find("Player");
        if (player != null)
            targetPos = player.transform.position + Vector3.up * 2f;
    }
}
```

**2. Direct Player Tracking (NEW)**
```csharp
private Transform _cachedPlayerTransform;
private float _lastPlayerSearchTime;

void UpdatePerception()
{
    // Direct player tracking - more reliable than OverlapSphere
    if (_cachedPlayerTransform == null || Time.time - _lastPlayerSearchTime > 2.0f)
    {
        _lastPlayerSearchTime = Time.time;
        GameObject playerObj = GameObject.Find("Player");
        if (playerObj == null) playerObj = GameObject.Find("LocalPlayer");
        if (playerObj != null)
            _cachedPlayerTransform = playerObj.transform;
    }

    if (_cachedPlayerTransform != null)
    {
        float dist = Vector3.Distance(transform.position, _cachedPlayerTransform.position);
        if (dist < ViewDistance)
            UpdateMemory(_cachedPlayerTransform, _cachedPlayerTransform.position, true);
    }
    // ... rest of perception
}
```

**3. IsEnemy Fix**
```csharp
bool IsEnemy(Collider col)
{
    if (col.transform == transform) return false;
    if (col.GetComponent<BotController>() != null) return true;

    string objName = col.name;
    if (objName == "LocalPlayer" || objName == "GamePlayer" || objName == "Player") return true;

    string rootName = col.transform.root.name;
    if (rootName == "LocalPlayer" || rootName == "GamePlayer" || rootName == "Player") return true;

    try { if (col.CompareTag("Player")) return true; } catch {}
    return false;
}
```

**4. Layer Setup**
```csharp
void SetupDamageSystem()
{
    // Use Layer 8 (Player) - weapons should raycast against this
    // Ground detection excludes layer 8 so we don't hit our own collider
    gameObject.layer = 8;
    // ...
}
```

### InjectionTester.cs

**1. Bot Layer Changed**
```csharp
// OLD: botObj.layer = 0;
// NEW:
botObj.layer = 8; // Put bot on Layer 8 (Player)
```

**2. Body Clone Layer (CRITICAL FIX)**
```csharp
// OLD: SetLayerRecursively(bodyClone, 0);
// NEW:
SetLayerRecursively(bodyClone, 8); // Ground raycast excludes Layer 8
```

**3. All Placeholder Layers Updated to 8**

---

## Key Discoveries

### UberStrike Damage System Architecture
```
Weapon.Shoot()
  -> Raycast hits Collider
  -> Get CharacterHitArea component
  -> CharacterHitArea.ApplyDamage(DamageInfo shot)
  -> Forwards to IShootable.ApplyDamage(DamageInfo shot)
```

**DamageInfo Class:**
```csharp
class DamageInfo {
    Int16 Damage;
    Vector3 Force;
    Vector3 Hitpoint;
    BodyPart BodyPart;
    Int32 ShotID;
    Int32 WeaponID;
    UberstrikeItemClass WeaponClass;
    Single CriticalStrikeBonus;
    DamageEffectType DamageEffectFlag;
    Single DamageEffectValue;
}
```

**IShootable Interface:**
```csharp
interface IShootable {
    bool IsVulnerable { get; }
    bool IsLocal { get; }
    void ApplyDamage(DamageInfo shot);
    void ApplyForce(Vector3 position, Vector3 force);
}
```

### Ground Detection Failure Modes
1. **Flying Up:** Raycast hits bot's own colliders (above transform)
2. **Falling Through:** Raycast misses ground (wrong layer mask or no geometry)
3. **Stuck in Air:** No raycast hit + no gravity applied

---

## Remaining Critical Issues

| Priority | Issue | Status | Blocker? |
|----------|-------|--------|----------|
| P0 | Bots fall through floor | BROKEN | YES |
| P0 | Bots don't move/follow | BROKEN | YES |
| P1 | FPS drops when bots active | NOT FIXED | Partial |
| P2 | Bots don't take damage | NOT FIXED | No |
| P2 | Weapon visibility | FIXED | No |

---

## Recommended Next Steps

### 1. Debug Ground Layer (Highest Priority)
```csharp
// Add diagnostic to find what layer ground actually is:
void DiagnoseGroundLayers()
{
    RaycastHit hit;
    if (Physics.Raycast(transform.position + Vector3.up * 10f, Vector3.down, out hit, 100f))
    {
        Debug.Log("GROUND HIT: " + hit.collider.name +
                  " Layer: " + hit.collider.gameObject.layer +
                  " Tag: " + hit.collider.tag);
    }
}
```

### 2. Fix Movement Execution
- Check why ExecuteMovement() calculates moveDir but bot doesn't move
- Verify Rigidbody.MovePosition is being called
- Check if wall raycast is blocking all movement

### 3. Performance Optimization
- Add frame skip to expensive operations
- Cache Physics.OverlapSphere results
- Remove or reduce diagnostic logging

### 4. Alternative Ground Detection
Consider using CharacterController instead of manual raycast:
```csharp
CharacterController cc = gameObject.AddComponent<CharacterController>();
cc.Move(velocity * Time.deltaTime); // Handles ground collision automatically
```

---

## Session Files

- `BotController.cs` - Main bot AI controller
- `InjectionTester.cs` - Spawn and injection logic
- `CharacterHitAreaProbe.cs` - Type analysis tool (F7)
- `DeepProbe.txt` - Runtime type dumps

---

## Training Data Tags
`#ground-detection` `#layer-system` `#physics-raycast` `#unity-3.5` `#bot-spawning` `#player-tracking` `#gravity` `#noclip-bug` `#uberstrike`

---

*Report generated: 2026-02-03*
*Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>*
