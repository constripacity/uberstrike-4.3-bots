using UnityEngine;
using UberStrike.Core.Types;

/// <summary>
/// Tunable bot parameters. Ported from BotRunner/Config/BotConfig.cs.
/// All values are public static so they can be tweaked at runtime via console or inspector.
/// </summary>
public static class BotConfig
{
    // --- Movement (matched to original 4.3.8 CharacterMoveController) ---
    public static float WalkSpeed = 7.0f;          // PlayerWalkSpeed=7 in LevelEnviroment.cs
    public static float CrouchSpeedScale = 0.7f;   // PLAYER_DUCK_SCALE=0.7 (70% of normal)
    public static float PatrolRadius = 40f;
    public static float PatrolWaitTime = 1.5f;

    // --- Jump / Bunny Hop (matched to original 4.3.8) ---
    public static float JumpSpeed = 15f;           // PlayerJumpSpeed=15 in LevelEnviroment.cs
    public static float JumpInterval = 0.35f;       // Must release & re-press; ~0.35s natural cycle
    public static float JumpGravity = 50f;          // EnviromentSettings.Gravity=50
    public static float AirAcceleration = 3f;       // EnviromentSettings.AirAcceleration=3
    public static float GroundAcceleration = 15f;   // EnviromentSettings.GroundAcceleration=15

    // --- Crouch (matched to original 4.3.8 CheckDuck) ---
    public static float CrouchHeight = 0.9f;       // HEIGHT_DUCKED=0.9
    public static float NormalHeight = 1.6f;        // HEIGHT_NORMAL=1.6
    public static float CrouchCenterY = -0.4f;      // CENTER_OFFSET_DUCKED=-0.4
    public static float NormalCenterY = -0.1f;       // CENTER_OFFSET_NORMAL=-0.1

    // --- Quick Switch ---
    public static float QuickSwitchDelay = 0.15f;  // Delay between fire and weapon switch
    public static int WeaponsPerBot = 3;            // Number of weapons each bot carries

    // --- Combat ---
    public static float EngageDistance = 30f;
    public static float AimErrorDegrees = 3.5f;
    public static float ReactionDelay = 0.2f;
    public static float SightDistance = 45f;
    public static float SightAngle = 80f;
    public static float CriticalStrikeBonus = 1.0f; // +100% = 2x damage on headshot/nutshot

    // --- Health & Armor ---
    public static short MaxHealth = 100;
    public static short MaxArmor = 100; // Same as real multiplayer
    public static float ArmorAbsorptionRate = 0.66f; // 66% of damage absorbed by armor
    public static float RespawnDelay = 5f;
    public static float SpawnGraceTime = 2f;

    // --- Perception ---
    public static float PerceptionInterval = 0.2f; // 5 Hz

    // --- Spawner ---
    public static int MaxBots = 8;

    // --- XP Awards ---
    public static int XpPerKill = 100;
    public static int XpHeadshotBonus = 50;
    public static int XpNutshotBonus = 50;
    public static int PointsPerKill = 10;

    // --- Weapon IDs (from ShopEnums.DefaultWeaponId) ---
    public static readonly int[] WeaponIds = new int[]
    {
        1002, // Machinegun
        1003, // Shotgun
        1004, // SniperRifle
        1005, // Cannon
        1006, // Splattergun
        1007, // Launcher
    };

    /// <summary>
    /// Per-weapon stats for bots. Returns damage, fire rate, and weapon class for a given weapon ID.
    /// Damages match the original UberStrike defaults from DefaultItemUtil.
    /// Shotgun uses 35 (simulating ~3-4 pellet hits from single raycast).
    /// </summary>
    public static void GetWeaponStats(int weaponId, out short damage, out float fireRate, out UberstrikeItemClass weaponClass)
    {
        switch (weaponId)
        {
            case 1002: damage = 14;  fireRate = 0.12f; weaponClass = UberstrikeItemClass.WeaponMachinegun; break;
            case 1003: damage = 35;  fireRate = 0.55f; weaponClass = UberstrikeItemClass.WeaponShotgun; break;
            case 1004: damage = 80;  fireRate = 1.20f; weaponClass = UberstrikeItemClass.WeaponSniperRifle; break;
            case 1005: damage = 65;  fireRate = 0.90f; weaponClass = UberstrikeItemClass.WeaponCannon; break;
            case 1006: damage = 15;  fireRate = 0.30f; weaponClass = UberstrikeItemClass.WeaponSplattergun; break;
            case 1007: damage = 70;  fireRate = 1.00f; weaponClass = UberstrikeItemClass.WeaponLauncher; break;
            default:   damage = 20;  fireRate = 0.22f; weaponClass = UberstrikeItemClass.WeaponMachinegun; break;
        }
    }

    // --- Skin Colors (per-bot variety) ---
    public static readonly Color[] SkinColors = new Color[]
    {
        new Color(0.87f, 0.75f, 0.62f), // Light (default)
        new Color(0.55f, 0.40f, 0.30f), // Tan
        new Color(0.45f, 0.35f, 0.25f), // Dark
        new Color(0.95f, 0.80f, 0.65f), // Pale
        new Color(0.35f, 0.25f, 0.18f), // Deep
        new Color(0.75f, 0.60f, 0.45f), // Olive
        new Color(0.85f, 0.55f, 0.35f), // Auburn
        new Color(0.70f, 0.55f, 0.40f), // Bronze
    };

    // --- Gear Loadouts (per-bot visual variety) ---
    // Each array is: Head, Face, Gloves, UpperBody, LowerBody, Boots, Holo
    // 0 = no gear in slot (shows base body)
    // All 252 gear prefabs are bundled locally — no shop server needed.
    // Item IDs from BackendData.cs, AP values from the commented header data.
    public static readonly int[][] GearLoadouts = new int[][]
    {
        //                  Head   Face   Gloves Upper  Lower  Boots  Holo
        new int[] { 1103,  1104,  1106,  1101,  1102,  1107,  0 },    // Ninja (SForce)
        new int[] { 1108,  1109,  1110,  1111,  1112,  1113,  0 },    // Pirate (Cap'n Bradford)
        new int[] { 1171,  0,     1169,  1167,  1168,  1170,  0 },    // Knight Golden (Sir Magnus)
        new int[] { 1280,  0,     1279,  1282,  1281,  1278,  0 },    // Juggernaut
        new int[] { 1275,  1273,  1274,  1277,  1276,  1272,  0 },    // Black Corps
        new int[] { 1224,  0,     1221,  1230,  1227,  1218,  0 },    // Tron Blue (T500)
        new int[] { 1339,  1340,  1341,  1342,  1343,  1344,  0 },    // Vampire (Lucius the Cruel)
        new int[] { 1138,  1139,  1143,  1140,  1141,  1144,  0 },    // Skeleton
    };

    // --- Per-loadout armor points ---
    // Real AP values from BackendData.cs header comments: // ID, ArmorPoints, ArmorWeight, "Name"
    // Only UpperBody + LowerBody + Holo slots contribute AP (confirmed in LoadoutManager.GetArmorValues).
    private static readonly short[] LoadoutArmorValues = new short[]
    {
         60, // Loadout 0: Ninja — Upper 30 + Lower 30
         40, // Loadout 1: Pirate — Upper 20 + Lower 20
         60, // Loadout 2: Knight Golden — Upper 30 + Lower 30
         80, // Loadout 3: Juggernaut — Upper 40 + Lower 40
         80, // Loadout 4: Black Corps — Upper 40 + Lower 40
         55, // Loadout 5: Tron Blue — Upper 25 + Lower 30
        100, // Loadout 6: Vampire — Upper 60 + Lower 60 (capped at MaxArmor)
         30, // Loadout 7: Skeleton — Upper 15 + Lower 15
    };

    public static short GetLoadoutArmor(int loadoutIndex)
    {
        int idx = loadoutIndex % LoadoutArmorValues.Length;
        short val = LoadoutArmorValues[idx];
        return val > MaxArmor ? MaxArmor : val;
    }

    // --- Water Movement (from original CharacterMoveController) ---
    public static float WadeSpeedScale = 0.8f;       // PLAYER_WADE_SCALE — WaterLevel 1-2
    public static float SwimSpeedScale = 0.6f;        // PLAYER_SWIM_SCALE — WaterLevel 3 (fully submerged)
    public static float WaterGravityScale = 0.1f;     // Gravity * 0.1 in water (50 * 0.1 = 5)
    public static float WaterTerminalVelocity = -3f;  // Max sink speed in water
    public static float WaterAcceleration = 6f;       // EnviromentSettings.WaterAcceleration
    public static float WaterSurfaceForce = 8f;       // Upward force to help bots surface

    // --- Death Zone ---
    public static float DeathFloorY = -200f;          // If bot falls below this, instant death

    // --- Launch / JumpPad ---
    public static float LaunchTimeout = 10f;          // Max flight time before force-landing
    public static float LandingMomentumKeep = 0.3f;   // % of horizontal launch velocity kept on landing

    // --- Bot Names (themed to match loadouts) ---
    public static readonly string[] BotNames = new string[]
    {
        "ShadowBlade",    // Ninja
        "Cap'nBradford",  // Pirate
        "SirMagnus",      // Knight
        "IronFist",       // Juggernaut
        "GhostReaper",    // Black Corps
        "NeonStrike",     // Tron
        "LuciusCruel",    // Vampire
        "BoneCollector"   // Skeleton
    };
}
