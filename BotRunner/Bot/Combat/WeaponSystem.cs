using System;
using System.Collections.Generic;

namespace BotRunner.Bot.Combat
{
    public enum WeaponType
    {
        AssaultRifle,
        SniperRifle,
        Shotgun,
        SMG,
        Pistol,
        RocketLauncher
    }

    public class WeaponProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public WeaponType Type { get; set; }
        public float OptimalRange { get; set; }  // 5m for shotgun, 30m for rifle
        public float MaxEffectiveRange { get; set; }
        public float FireRate { get; set; }      // Rounds per second
        public float ReloadTime { get; set; }    // Seconds
        public int MagazineSize { get; set; }
        public float Spread { get; set; }        // Accuracy cone
        public float ProjectileSpeed { get; set; } // 0 = hitscan
        public int Damage { get; set; }

        // Behavior modifiers
        public float StrafeModifier { get; set; } = 1.0f; // 0.8 = slows strafe while using
        public float MovementPenalty { get; set; } = 0f;  // 0.3 = 30% slower when aiming
    }

    public static class WeaponSystem
    {
        private static readonly Dictionary<int, WeaponProfile> _profiles = new()
        {
            [1] = new WeaponProfile
            {
                Id = 1,
                Name = "Assault Rifle",
                Type = WeaponType.AssaultRifle,
                OptimalRange = 20f,
                MaxEffectiveRange = 40f,
                FireRate = 8f,
                ReloadTime = 2.0f,
                MagazineSize = 30,
                Spread = 0.05f,
                ProjectileSpeed = 0f, // Hitscan
                Damage = 20
            },
            [2] = new WeaponProfile
            {
                Id = 2,
                Name = "Sniper Rifle",
                Type = WeaponType.SniperRifle,
                OptimalRange = 40f,
                MaxEffectiveRange = 60f,
                FireRate = 1f,
                ReloadTime = 3.5f,
                MagazineSize = 10,
                Spread = 0.01f,
                ProjectileSpeed = 0f, // Hitscan
                Damage = 80
            },
            [3] = new WeaponProfile
            {
                Id = 3,
                Name = "Shotgun",
                Type = WeaponType.Shotgun,
                OptimalRange = 5f,
                MaxEffectiveRange = 12f,
                FireRate = 1.5f,
                ReloadTime = 2.5f,
                MagazineSize = 8,
                Spread = 0.25f,
                ProjectileSpeed = 0f, // Hitscan
                Damage = 15 // Per pellet, usually multiple pellets
            },
            [4] = new WeaponProfile
            {
                Id = 4,
                Name = "SMG",
                Type = WeaponType.SMG,
                OptimalRange = 10f,
                MaxEffectiveRange = 25f,
                FireRate = 12f,
                ReloadTime = 1.8f,
                MagazineSize = 40,
                Spread = 0.12f,
                ProjectileSpeed = 0f, // Hitscan
                Damage = 12
            },
            [5] = new WeaponProfile
            {
                Id = 5,
                Name = "Pistol",
                Type = WeaponType.Pistol,
                OptimalRange = 8f,
                MaxEffectiveRange = 20f,
                FireRate = 4f,
                ReloadTime = 1.2f,
                MagazineSize = 15,
                Spread = 0.08f,
                ProjectileSpeed = 0f, // Hitscan
                Damage = 15
            }
        };

        public static WeaponProfile? GetProfile(int weaponId)
        {
            return _profiles.TryGetValue(weaponId, out var profile) ? profile : null;
        }

        public static IEnumerable<WeaponProfile> GetAllProfiles() => _profiles.Values;
    }
}
