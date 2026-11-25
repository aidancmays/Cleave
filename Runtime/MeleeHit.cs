using UnityEngine;

namespace Cleave
{
    /// <summary>One swing landing on something, as the server applies it.</summary>
    public struct MeleeHit
    {
        /// <summary>Who swung (the server's copy of them). Null if they have gone.</summary>
        public Attacker Attacker;
        /// <summary>The weapon they swung.</summary>
        public MeleeWeapon Weapon;
        /// <summary>Where it landed, in world space.</summary>
        public Vector3 Point;
        /// <summary>Which way the swing was moving through it (world space, unit length).</summary>
        public Vector3 Direction;
        public float Damage;
        /// <summary>Impulse along Direction, in N s.</summary>
        public float Knockback;
        /// <summary>A charged attack (held past the weapon's Tap Time).</summary>
        public bool Charged;
        /// <summary>0..1 how full the charge was.</summary>
        public float Charge;
    }
}
