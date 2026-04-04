using UnityEngine;
using UberStrike.Realtime.Common;
using UberStrike.DataCenter.Common.Entities;
using UberStrike.Core.Types;

public class CharacterHitArea : BaseGameProp
{
    public override void ApplyDamage(DamageInfo shot)
    {
        shot.BodyPart = _part;

        if (Shootable != null)
        {
            // Debug overrides: block damage to local player if active
            if (Shootable.IsLocal && DebugOverrideRegistry.Current.ShouldBlockDamage)
                return;

            //check if the avatar is shootable (normally disabled for 1 second after respawn)
            if (Shootable.IsVulnerable)
            {
                // award for headshot
                if (_part == BodyPart.Head || _part == BodyPart.Nuts)
                {
                    shot.Damage += (short)(shot.Damage * shot.CriticalStrikeBonus);
                }

                // Mark this damage as coming through CharacterHitArea — ONLY player weapons
                // take this path. Bot weapons call Shootable.ApplyDamage() directly, bypassing
                // this method. This is the definitive way to know the player fired.
                var bot = Shootable as BotController;
                if (bot != null)
                    bot.DamageFromPlayerWeapon = true;

                Shootable.ApplyDamage(shot);

                if (bot != null)
                    bot.DamageFromPlayerWeapon = false;
            }
        }
        else
        {
            Debug.LogError("No character set to the body part!");
        }
    }

    public override bool IsLocal
    {
        get
        {
            return Shootable != null ? Shootable.IsLocal : false;
        }
    }

    #region Properties
    public IShootable Shootable { get; set; }

    public BodyPart CharacterBodyPart
    {
        get { return _part; }
    }
    #endregion

    #region FIELDS
    [SerializeField]
    private BodyPart _part;

    #endregion
}
