using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DeathArea : MonoBehaviour
{
    private void Awake()
    {
        if (GetComponent<Collider>()) GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider c)
    {
        if (c.tag == "Player" && GameState.HasCurrentPlayer)
        {
            // LevelBoundary.KillPlayer() clears LastBotAttacker internally
            // so environment suicides are never credited to bots
            LevelBoundary.KillPlayer();
        }
        else
        {
            // Check if a bot entered the death zone
            var bot = c.GetComponentInParent<BotController>();
            if (bot != null && bot.Health > 0)
            {
                bot.KillByEnvironment();
            }
        }
    }
}
