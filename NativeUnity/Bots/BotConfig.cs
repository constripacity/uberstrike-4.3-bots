using UnityEngine;
using UberStrike.Core.Types;

// ================================================================
// Difficulty System
// ================================================================

public enum BotDifficulty { Easy, Medium, Hard }

public struct DifficultyPreset
{
    public float AimError;         // Degrees of aim scatter
    public float ReactionDelay;    // Seconds before first shot on sight
    public float StrafeDistance;    // Lateral movement amplitude
    public float DodgeJumpChance;  // Chance per second to dodge jump in close range
    public float DamageMultiplier; // Damage scalar (0.7 = 70% damage)
    public float CliffAvoidChance; // Chance to avoid walking off cliff edges (0=never, 1=always)
    public string Tag;             // Shown in bot name like "[E] BotName"
}

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
    public static float QuickSwitchDelay = 0.6f;   // Delay after firing before considering a weapon switch (realistic timing)
    public static int WeaponsPerBot = 3;            // Number of weapons each bot carries

    // --- Combat (base values — overridden per-bot by difficulty) ---
    public static float EngageDistance = 30f;
    public static float AimErrorDegrees = 3.5f;
    public static float ReactionDelay = 0.2f;
    public static float SightDistance = 45f;
    public static float SightAngle = 80f;
    public static float CriticalStrikeBonus = 0.3f; // +30% = 1.3x damage on headshot/nutshot (reduced from 1.5x to prevent one-shots)

    // --- Combat Behaviors (from BotRunner OrbitStrafeBehavior/DisengageBehavior) ---
    public static float DisengageHealthRatio = 0.3f;   // Retreat below 30% HP
    public static float CloseRangeDistance = 8f;        // Close range threshold
    public static float IdealCombatDistance = 15f;      // Preferred orbit distance
    public static float StrafeDistance = 3f;            // Lateral movement distance (base)
    public static float StrafeFlipMinTime = 2f;         // Min time before direction flip
    public static float StrafeFlipMaxTime = 4f;         // Max time before direction flip
    public static float DodgeJumpChance = 0.3f;         // Chance per second to dodge jump (base)
    public static float DodgeJumpCooldown = 2f;         // Min time between dodge jumps
    public static float DodgeJumpSpeed = 5f;            // Lateral speed added on dodge

    // --- Health & Armor ---
    public static short MaxHealth = 100;
    public static short MaxArmor = 100; // Same as real multiplayer
    public static float ArmorAbsorptionRate = 0.66f; // 66% of damage absorbed by armor
    public static float RespawnDelay = 5f;
    public static float SpawnGraceTime = 2f;

    // --- Perception ---
    public static float PerceptionInterval = 0.2f; // 5 Hz

    // --- Spawner ---
    public static int MaxBots = 15; // 16-player match = 15 bots + 1 player

    // --- XP Awards ---
    public static int XpPerKill = 100;
    public static int XpHeadshotBonus = 50;
    public static int XpNutshotBonus = 50;
    public static int PointsPerKill = 10;

    // --- Weapon pools by class (all bundled weapons from BackendData) ---
    // Each bot gets 3 random weapons from different classes for variety.
    public static readonly int[] MachinegunIds = {
        1002, // Machine Gun
        1146, // UMG
        1303, // UMG Dragon Edition
        1370, // Assault Rifle
        1371, // Assault Rifle Camo
        1372, // Assault Rifle Tiger
        1373, // Assault Rifle Black
    };
    public static readonly int[] ShotgunIds = {
        1003, // Shotgun
        1013, // Snotgun
        1374, // SPAS-12
        1375, // Auto Shotgun Camo
        1376, // Auto Shotgun Tiger
        1377, // Auto Shotgun Black
    };
    public static readonly int[] SniperIds = {
        1004, // Sniper Rifle
        1016, // Ordinator Rifle
        1023, // Magma Rifle
        1245, // Arctic Rifle
        1302, // Magma Rifle Dragon
        1355, // AWP Normal
        1356, // AWP Black
        1357, // AWP Camo
        1358, // AWP Tiger
    };
    public static readonly int[] CannonIds = {
        1005, // Cannon
        1020, // Force Cannon
        1145, // Enigma Cannon
        1243, // Force Cannon Plus
        1299, // Ultima Cannon
        1315, // Enigma Cannon Dragon
    };
    public static readonly int[] SplattergunIds = {
        1006, // Splatter Gun
        1022, // Mad Splatter
        1237, // Spiteful Stinger
        1239, // Nefarious Needler
    };
    public static readonly int[] LauncherIds = {
        1007, // Grenade Launcher
        1025, // Mortar Exporter
        1262, // iLauncher
        1297, // Demolisher
    };
    public static readonly int[] HandgunIds = {
        1001, // Handgun
        1008, // Judge
        1009, // Jury
        1010, // Executioner
        1164, // Godfather's Hand Cannon
        1300, // Snap Shot
        1363, // USP Normal
        1365, // USP Black
        1359, // UZI Normal
        1362, // UZI Gold
    };
    public static readonly int[] SpecialIds = {
        1011, // Shadow Gun
        1012, // Battlesnake
        1015, // Thunderbuss
        1017, // Deliverator
        1018, // Vanquisher
        1026, // The Final Word
        1147, // Particle Lance
        1242, // Firewave
        1244, // Dark Vanquisher
        1246, // Fusion Lance
        1301, // Vanquisher Dragon
        1313, // Battlesnake Dragon
        1314, // Thunderbuss Dragon
        1316, // Particle Lance Dragon
        1331, // Vanquisher Kongregate
    };

    // All weapon pools combined for random selection
    public static readonly int[][] AllWeaponPools = {
        MachinegunIds, ShotgunIds, SniperIds, CannonIds,
        SplattergunIds, LauncherIds, HandgunIds, SpecialIds,
    };

    // Legacy WeaponIds kept for backwards compat (initialization uses random selection now)
    public static readonly int[] WeaponIds = new int[]
    {
        1002, 1003, 1004, 1005, 1006, 1007,
    };

    /// <summary>
    /// Pick 3 random weapons from different weapon classes for a bot.
    /// Ensures each bot has a varied loadout.
    /// </summary>
    public static int[] GetRandomWeaponLoadout()
    {
        // Shuffle pool indices to pick 3 different classes
        int[] indices = new int[AllWeaponPools.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }

        int[] loadout = new int[WeaponsPerBot];
        for (int i = 0; i < WeaponsPerBot && i < indices.Length; i++)
        {
            var pool = AllWeaponPools[indices[i]];
            loadout[i] = pool[Random.Range(0, pool.Length)];
        }
        return loadout;
    }

    /// <summary>
    /// Per-weapon stats for bots. Returns damage, fire rate, and weapon class.
    /// Stats are based on weapon CLASS (not individual weapon ID) since variants
    /// within a class share the same base stats.
    /// </summary>
    public static void GetWeaponStats(int weaponId, out short damage, out float fireRate, out UberstrikeItemClass weaponClass)
    {
        // Identify class by checking which pool the weapon belongs to
        if (System.Array.IndexOf(MachinegunIds, weaponId) >= 0)
            { damage = 14;  fireRate = 0.12f; weaponClass = UberstrikeItemClass.WeaponMachinegun; return; }
        if (System.Array.IndexOf(ShotgunIds, weaponId) >= 0)
            { damage = 35;  fireRate = 0.55f; weaponClass = UberstrikeItemClass.WeaponShotgun; return; }
        if (System.Array.IndexOf(SniperIds, weaponId) >= 0)
            { damage = 80;  fireRate = 1.20f; weaponClass = UberstrikeItemClass.WeaponSniperRifle; return; }
        if (System.Array.IndexOf(CannonIds, weaponId) >= 0)
            { damage = 65;  fireRate = 0.90f; weaponClass = UberstrikeItemClass.WeaponCannon; return; }
        if (System.Array.IndexOf(SplattergunIds, weaponId) >= 0)
            { damage = 15;  fireRate = 0.30f; weaponClass = UberstrikeItemClass.WeaponSplattergun; return; }
        if (System.Array.IndexOf(LauncherIds, weaponId) >= 0)
            { damage = 70;  fireRate = 1.00f; weaponClass = UberstrikeItemClass.WeaponLauncher; return; }
        if (System.Array.IndexOf(HandgunIds, weaponId) >= 0)
            { damage = 25;  fireRate = 0.25f; weaponClass = UberstrikeItemClass.WeaponHandgun; return; }
        if (System.Array.IndexOf(SpecialIds, weaponId) >= 0)
            { damage = 20;  fireRate = 0.18f; weaponClass = UberstrikeItemClass.WeaponMachinegun; return; }

        // Fallback
        damage = 20; fireRate = 0.22f; weaponClass = UberstrikeItemClass.WeaponMachinegun;
    }

    // --- Difficulty Presets ---
    public static readonly DifficultyPreset[] Difficulties = new DifficultyPreset[]
    {
        // Easy: target practice — wide scatter, slow reactions, low damage, sometimes falls off edges
        new DifficultyPreset { AimError = 8f,   ReactionDelay = 0.8f,  StrafeDistance = 2f, DodgeJumpChance = 0f,   DamageMultiplier = 0.25f, CliffAvoidChance = 0.4f, Tag = "E" },
        // Medium: fair fight — moderate scatter, reasonable reactions, ALWAYS avoids edges
        new DifficultyPreset { AimError = 5f,   ReactionDelay = 0.4f,  StrafeDistance = 3f, DodgeJumpChance = 0.3f, DamageMultiplier = 0.65f, CliffAvoidChance = 1.0f, Tag = "M" },
        // Hard: challenging — tight aim, quick reactions, full damage, ALWAYS avoids edges
        new DifficultyPreset { AimError = 2.5f, ReactionDelay = 0.15f, StrafeDistance = 4f, DodgeJumpChance = 0.6f, DamageMultiplier = 1.0f,  CliffAvoidChance = 1.0f, Tag = "H" },
    };

    /// <summary>
    /// Current spawn difficulty mix. L key cycles this in BotSpawner.
    /// Determines the distribution of Easy/Medium/Hard bots when spawning.
    /// </summary>
    public static BotDifficulty SpawnDifficultyMix = BotDifficulty.Medium;

    /// <summary>
    /// Get difficulty for a bot based on its index and the current mix setting.
    /// Mixed: distributes Easy(40%), Medium(40%), Hard(20%) roughly.
    /// </summary>
    public static BotDifficulty GetDifficultyForBot(int botIndex)
    {
        switch (SpawnDifficultyMix)
        {
            case BotDifficulty.Easy:
                // All easy
                return BotDifficulty.Easy;
            case BotDifficulty.Hard:
                // All hard
                return BotDifficulty.Hard;
            case BotDifficulty.Medium:
            default:
                // Mixed distribution: Easy, Medium, Hard cycling
                int mod = botIndex % 5;
                if (mod < 2) return BotDifficulty.Easy;     // 0,1 = Easy (40%)
                if (mod < 4) return BotDifficulty.Medium;   // 2,3 = Medium (40%)
                return BotDifficulty.Hard;                   // 4 = Hard (20%)
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
        new int[] { 1172,  0,     1169,  1167,  1168,  1170,  0 },    // Knight Thorny (Sir Magnus alt)
        new int[] { 1178,  0,     1176,  1174,  1175,  1177,  0 },    // Iron Viktor
        new int[] { 1120,  0,     1116,  1121,  1122,  1123,  0 },    // Croc Hunter
        new int[] { 1187,  1190,  1188,  1185,  1186,  1189,  0 },    // UberSanta
        new int[] { 1225,  0,     1222,  1231,  1228,  1219,  0 },    // Tron Red (T500)
        new int[] { 1286,  0,     1285,  1288,  1287,  1283,  0 },    // Sentinel
        new int[] { 1321,  1322,  1323,  1319,  1320,  1324,  0 },    // Ninja Black Dragon
        new int[] { 1327,  1328,  1329,  1325,  1326,  1330,  0 },    // Ninja White Dragon
        new int[] { 1259,  1257,  1258,  1261,  1260,  1256,  0 },    // ArcticDreads
        new int[] { 1309,  0,     1307,  1304,  1305,  1308,  0 },    // Juggernaut Dragon
        new int[] { 1383,  1385,  1387,  1389,  1395,  1388,  0 },    // CT Germany
        new int[] { 1396,  0,     1397,  1399,  1400,  1398,  0 },    // Terrorist Urban
        new int[] { 1334,  0,     1335,  1332,  1333,  1336,  0 },    // Kongregate
        new int[] { 0,     1031,  1086,  1153,  1151,  1157,  0 },    // SuperSpy
        new int[] { 1115,  0,     1116,  1117,  1118,  1119,  0 },    // Commando Goon
        new int[] { 1075,  0,     1086,  1098,  1045,  1089,  0 },    // Admin / Green Beret
        new int[] { 1226,  0,     1223,  1232,  1229,  1220,  0 },    // Tron Yellow (T500)
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
        100, // Loadout 6: Vampire — Upper 60 + Lower 60 (capped)
         30, // Loadout 7: Skeleton — Upper 15 + Lower 15
         60, // Loadout 8: Knight Thorny
         80, // Loadout 9: Iron Viktor — Upper 40 + Lower 40
         40, // Loadout 10: Croc Hunter — Upper 20 + Lower 20
         40, // Loadout 11: UberSanta — Upper 20 + Lower 20
         60, // Loadout 12: Tron Red — Upper 30 + Lower 30
         80, // Loadout 13: Sentinel — Upper 40 + Lower 40
         80, // Loadout 14: Ninja Black Dragon — Upper 40 + Lower 40
         80, // Loadout 15: Ninja White Dragon — Upper 40 + Lower 40
         60, // Loadout 16: ArcticDreads — Upper 30 + Lower 30
         80, // Loadout 17: Juggernaut Dragon — Upper 40 + Lower 40
         60, // Loadout 18: CT Germany
         60, // Loadout 19: Terrorist Urban — Upper 60 + Lower 60 (capped)
         80, // Loadout 20: Kongregate — Upper 40 + Lower 40
         60, // Loadout 21: SuperSpy — Upper 30 + Lower 30
         20, // Loadout 22: Commando Goon — Upper 10 + Lower 10
         20, // Loadout 23: Admin — Upper 10 + Lower 10 (estimate)
         40, // Loadout 24: Tron Yellow — Upper 10 + Lower 10
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

    // --- Movement Naturalness (per difficulty) ---
    // Jump chance per frame when moving on ground. Lower = less bunny hopping.
    // Easy/Medium bots should mostly walk, only jumping occasionally.
    // Hard bots jump more aggressively but still with per-bot randomization.
    public static float JumpChanceEasy = 0.003f;      // ~0.3% per frame ≈ jump every 5-6s
    public static float JumpChanceMedium = 0.008f;    // ~0.8% per frame ≈ jump every 2-3s
    public static float JumpChanceHard = 0.025f;      // ~2.5% per frame ≈ bunny hop often
    public static float JumpChanceCombat = 0.04f;     // During combat, all difficulties jump more

    // --- Crouch AI (Hard difficulty only) ---
    public static float CrouchDetectChance = 0.15f;   // 15% chance per perception tick to crouch-dodge
    public static float CrouchDuration = 0.8f;        // How long to stay crouched
    public static float CrouchCooldown = 3f;           // Min time between crouch dodges

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
        "NeonStrike",     // Tron Blue
        "LuciusCruel",    // Vampire
        "BoneCollector",  // Skeleton
        "SilentStorm",    // Knight Thorny
        "ViktorIron",     // Iron Viktor
        "CrocMaster",     // Croc Hunter
        "JingleDeath",    // UberSanta
        "RedCircuit",     // Tron Red
        "SentinelX",      // Sentinel
        "DarkDragon",     // Ninja Black Dragon
        "WhiteFang",      // Ninja White Dragon
        "ArcticWolf",     // ArcticDreads
        "DragonClaw",     // Juggernaut Dragon
        "BravoSix",       // CT Germany
        "Insurgent",      // Terrorist
        "KongSlayer",     // Kongregate
        "AgentZero",      // SuperSpy
        "Warlord",        // Commando
        "AdminPrime",     // Admin
        "GoldWire",       // Tron Yellow
    };
}
