using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns and manages bots in Training mode.
/// F1 = spawn a bot, F2 = remove all bots, F3 = toggle AI on/off, G = toggle ESP.
/// Auto-initializes via [RuntimeInitializeOnLoadMethod] + sceneLoaded.
/// Listens for player death to show "killed by [BotName]" death screen.
/// </summary>
public class BotSpawner : MonoBehaviour
{
    private static BotSpawner _instance;
    private bool _aiEnabled = true;
    private bool _espEnabled = false;
    private bool _crouchEnabled = false;
    private int _spawnCounter;
    private Texture2D _espTex;

    // Player death detection — poll-based (CmuneEventHandler may not fire in Training mode)
    private bool _playerWasAlive = true;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Only activate on map scenes, not the lobby
        if (!scene.name.StartsWith("Level") || scene.name == "LevelSpaceship")
            return;

        if (_instance == null)
        {
            var go = new GameObject("BotSpawner");
            _instance = go.AddComponent<BotSpawner>();
            Object.DontDestroyOnLoad(go);
        }
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void Update()
    {
        // Hotkey controls
        if (Input.GetKeyDown(KeyCode.F1))
            SpawnBot();

        if (Input.GetKeyDown(KeyCode.F2))
            ClearAllBots();

        if (Input.GetKeyDown(KeyCode.F3))
            ToggleAI();

        if (Input.GetKeyDown(KeyCode.G))
            ToggleESP();

        if (Input.GetKeyDown(KeyCode.J))
            SendBotsToJumpPads();

        if (Input.GetKeyDown(KeyCode.K))
            ToggleCrouch();

        // Detect player death for "killed by" screen
        CheckPlayerDeath();

        // Enforce bot kill count as authoritative scoreboard value.
        // Training mode applies a "knockback" suicide penalty (-1 kill per death)
        // every time the player dies, which overrides our AwardKillXp increments.
        // By continuously writing TotalBotKills, the scoreboard always shows the
        // correct number of bot kills regardless of death penalties.
        if (BotController.AllBots.Count > 0)
        {
            try
            {
                if (GameState.LocalCharacter != null)
                    GameState.LocalCharacter.Kills = (short)BotController.TotalBotKills;
            }
            catch (System.Exception) { }
        }
    }

    // ================================================================
    // Player Death Detection
    // ================================================================

    /// <summary>
    /// Poll-based death detection. Tracks alive→dead transitions.
    /// The bot kill message is now handled by GameModeUtil.OnPlayerSuicide() intercept,
    /// so this only serves as a backup if the suicide event doesn't fire.
    /// </summary>
    private void CheckPlayerDeath()
    {
        if (BotController.AllBots.Count == 0) return;

        bool playerAlive = true;
        try
        {
            var localPlayer = GameState.LocalPlayer;
            if (localPlayer != null)
                playerAlive = !localPlayer.IsDead;
            else
                playerAlive = true; // No player = not dead
        }
        catch { return; }

        // Detect alive → dead transition — backup only
        // Primary path is GameModeUtil.OnPlayerSuicide() which intercepts first
        if (_playerWasAlive && !playerAlive)
        {
            // Only fire if LastBotAttacker still set (not yet consumed by OnPlayerSuicide)
            if (BotController.LastBotAttacker != null)
                BotController.ShowBotKilledPlayerScreen();
        }

        _playerWasAlive = playerAlive;
    }

    void OnGUI()
    {
        // Minimal HUD overlay
        int botCount = BotController.AllBots.Count;
        string info = string.Format("Bots: {0}/{1}  AI: {2}  Crouch: {3}  Kills: {4}  ESP: {5}  [F1=Spawn F2=Clear F3=AI G=ESP J=JumpPad K=Crouch]",
            botCount, BotConfig.MaxBots, _aiEnabled ? "ON" : "OFF",
            _crouchEnabled ? "ON" : "OFF",
            BotController.TotalBotKills, _espEnabled ? "ON" : "OFF");

        GUI.color = Color.black;
        GUI.Label(new Rect(11, Screen.height - 29, 600, 25), info);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, Screen.height - 30, 600, 25), info);

        // ESP overlay
        if (_espEnabled)
            DrawESP();
    }

    // ================================================================
    // ESP Debug Overlay
    // ================================================================

    private void ToggleESP()
    {
        _espEnabled = !_espEnabled;
        Debug.Log("[BotSpawner] ESP " + (_espEnabled ? "ENABLED" : "DISABLED"));
    }

    private void DrawESP()
    {
        Camera cam = GetActiveCamera();
        if (cam == null) return;

        if (_espTex == null)
        {
            _espTex = new Texture2D(1, 1);
            _espTex.SetPixel(0, 0, Color.white);
            _espTex.Apply();
        }

        foreach (var bot in BotController.AllBots)
        {
            if (bot == null) continue;

            Vector3 worldPos = bot.transform.position;
            Vector3 headPos = worldPos + Vector3.up * 1.8f;
            Vector3 feetPos = worldPos;

            Vector3 screenHead = cam.WorldToScreenPoint(headPos);
            Vector3 screenFeet = cam.WorldToScreenPoint(feetPos);

            if (screenHead.z < 0 || screenFeet.z < 0) continue;

            float headY = Screen.height - screenHead.y;
            float feetY = Screen.height - screenFeet.y;

            float boxHeight = feetY - headY;
            if (boxHeight < 10f) boxHeight = 10f;
            float boxWidth = boxHeight * 0.45f;
            float centerX = screenHead.x;

            Rect boxRect = new Rect(centerX - boxWidth / 2f, headY, boxWidth, boxHeight);

            Color espColor;
            switch (bot.State)
            {
                case BotState.Dead:
                    espColor = Color.red;
                    break;
                case BotState.Combat:
                    espColor = new Color(1f, 0.5f, 0f);
                    break;
                case BotState.Chase:
                    espColor = Color.yellow;
                    break;
                default:
                    espColor = Color.green;
                    break;
            }

            DrawBoxOutline(boxRect, espColor, 2f);

            // Health bar
            float healthRatio = Mathf.Clamp01((float)bot.Health / BotConfig.MaxHealth);
            float barW = 4f;
            Rect barBg = new Rect(boxRect.x - barW - 2f, boxRect.y, barW, boxRect.height);
            DrawFilledRect(barBg, new Color(0f, 0f, 0f, 0.6f));
            Color hpColor = Color.Lerp(Color.red, Color.green, healthRatio);
            float barFillH = boxRect.height * healthRatio;
            Rect barFill = new Rect(barBg.x, barBg.y + boxRect.height - barFillH, barW, barFillH);
            DrawFilledRect(barFill, hpColor);

            // Info label
            float dist = Vector3.Distance(cam.transform.position, worldPos);
            string label = string.Format("{0}\n{1} HP {2} AP | {3} | {4:F1}m",
                bot.BotName, bot.Health, bot.Armor, bot.State, dist);

            var labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.LowerCenter;

            float labelW = 200f;
            float labelH = 40f;
            Rect labelRect = new Rect(centerX - labelW / 2f, headY - labelH - 4f, labelW, labelH);

            labelStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height), label, labelStyle);
            labelStyle.normal.textColor = espColor;
            GUI.Label(labelRect, label, labelStyle);

            // Snap line
            DrawLine(
                new Vector2(Screen.width / 2f, Screen.height),
                new Vector2(centerX, feetY),
                new Color(espColor.r, espColor.g, espColor.b, 0.4f));

            // World position
            var posStyle = new GUIStyle(GUI.skin.label);
            posStyle.fontSize = 10;
            posStyle.alignment = TextAnchor.UpperCenter;
            posStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            string posText = string.Format("({0:F1}, {1:F1}, {2:F1})", worldPos.x, worldPos.y, worldPos.z);
            GUI.Label(new Rect(centerX - 80f, feetY + 2f, 160f, 20f), posText, posStyle);
        }
    }

    private void DrawBoxOutline(Rect rect, Color color, float t)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, t), _espTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - t, rect.width, t), _espTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, t, rect.height), _espTex);
        GUI.DrawTexture(new Rect(rect.x + rect.width - t, rect.y, t, rect.height), _espTex);
    }

    private void DrawFilledRect(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, _espTex);
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color)
    {
        GUI.color = color;
        float dx = b.x - a.x;
        float dy = b.y - a.y;
        float length = Mathf.Sqrt(dx * dx + dy * dy);
        if (length < 1f) return;
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

        var prevMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - 0.5f, length, 1.5f), _espTex);
        GUI.matrix = prevMatrix;
    }

    private Camera GetActiveCamera()
    {
        if (LevelCamera.Exists && LevelCamera.Instance.MainCamera != null)
            return LevelCamera.Instance.MainCamera;
        return Camera.main;
    }

    // ================================================================
    // Spawn
    // ================================================================

    public void SpawnBot()
    {
        if (BotController.AllBots.Count >= BotConfig.MaxBots)
        {
            Debug.Log("[BotSpawner] Max bots reached (" + BotConfig.MaxBots + ")");
            return;
        }

        // Get spawn position
        Vector3 pos;
        Quaternion rot;
        if (SpawnPointManager.Instance != null)
        {
            SpawnPointManager.Instance.GetRandomSpawnPoint(out pos, out rot);
        }
        else
        {
            pos = new Vector3(Random.Range(-5f, 5f), 1f, Random.Range(-5f, 5f));
            rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        // Create bot GameObject
        string botName = BotConfig.BotNames[_spawnCounter % BotConfig.BotNames.Length];
        var botGo = new GameObject("Bot_" + botName);
        botGo.transform.position = pos + Vector3.up;
        botGo.transform.rotation = rot;
        botGo.layer = (int)UberstrikeLayer.RemotePlayer;

        // Add a trigger collider on a CHILD object (IgnoreRaycast layer) for JumpPad detection.
        // IMPORTANT: Must NOT be on root — trigger colliders on RemotePlayer layer intercept
        // player weapon raycasts before they reach the avatar's CharacterHitArea bone colliders.
        var triggerGo = new GameObject("BotJumpPadTrigger");
        triggerGo.transform.SetParent(botGo.transform, false);
        triggerGo.layer = (int)UberstrikeLayer.IgnoreRaycast;
        var capsule = triggerGo.AddComponent<CapsuleCollider>();
        capsule.height = 1.8f;
        capsule.radius = 0.3f;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.isTrigger = true;
        var rb = triggerGo.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Add NavMeshAgent — used ONLY for pathfinding, not position control.
        // CRITICAL: Set updatePosition=false IMMEDIATELY after AddComponent, before the
        // agent's first frame. Default is true, which snaps the bot to the NavMesh surface
        // (often below floor geometry) causing bots to fall through the ground on spawn.
        var agent = botGo.AddComponent<NavMeshAgent>();
        agent.updatePosition = false; // We control position via BotNavigation
        agent.updateRotation = false; // We control rotation in BotController
        agent.speed = BotConfig.WalkSpeed;
        agent.angularSpeed = 360f;
        agent.acceleration = BotConfig.GroundAcceleration;
        agent.height = 1.8f;
        agent.radius = 0.3f;
        agent.baseOffset = 0f;
        agent.stoppingDistance = 1f;
        agent.autoBraking = false; // Don't brake — we handle deceleration via friction

        // Add BotController — pass index for variety (skin color, weapon)
        var bot = botGo.AddComponent<BotController>();
        bot.Initialize(botName, _spawnCounter);

        if (!_aiEnabled)
            bot.Freeze();

        _spawnCounter++;
        Debug.Log("[BotSpawner] Spawned " + botName + " at " + pos);
    }

    // ================================================================
    // Management
    // ================================================================

    public void ClearAllBots()
    {
        for (int i = BotController.AllBots.Count - 1; i >= 0; i--)
        {
            if (BotController.AllBots[i] != null)
                Object.Destroy(BotController.AllBots[i].gameObject);
        }
        BotController.AllBots.Clear();
        _spawnCounter = 0;
        Debug.Log("[BotSpawner] All bots cleared");
    }

    public void ToggleAI()
    {
        _aiEnabled = !_aiEnabled;
        foreach (var bot in BotController.AllBots)
        {
            if (bot != null)
            {
                if (_aiEnabled)
                    bot.Unfreeze();
                else
                    bot.Freeze();
            }
        }
        Debug.Log("[BotSpawner] AI " + (_aiEnabled ? "ENABLED" : "DISABLED"));
    }

    /// <summary>
    /// Toggle crouch for all bots (K key). When crouching:
    /// - Movement speed reduced to 70% (original PLAYER_DUCK_SCALE)
    /// - Bunny hop disabled (can't jump while crouching)
    /// - Shooting remains enabled
    /// - Crouch animation plays
    /// </summary>
    private void ToggleCrouch()
    {
        _crouchEnabled = !_crouchEnabled;
        foreach (var bot in BotController.AllBots)
        {
            if (bot == null || bot.Health <= 0) continue;
            var nav = bot.GetComponent<BotNavigation>();
            if (nav != null)
            {
                nav.SetCrouching(_crouchEnabled);
            }
        }
        Debug.Log("[BotSpawner] Crouch " + (_crouchEnabled ? "ENABLED" : "DISABLED"));
    }

    /// <summary>
    /// Send all bots to the nearest JumpPad (J key). For testing JumpPad integration.
    /// </summary>
    private void SendBotsToJumpPads()
    {
        foreach (var bot in BotController.AllBots)
        {
            if (bot == null || bot.Health <= 0) continue;
            var nav = bot.GetComponent<BotNavigation>();
            if (nav != null)
            {
                nav.GoToNearestJumpPad();
            }
        }
        Debug.Log("[BotSpawner] Sending all bots to nearest JumpPad");
    }
}
