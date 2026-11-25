using UnityEngine;

namespace Cleave
{
    /// <summary>How a swing moves through the air (and so which way its hits sweep).</summary>
    public enum SwingStyle
    {
        /// <summary>Side to side across the view. Alternates direction swing after swing.</summary>
        Slash,

        /// <summary>From above the head down to the ground in front.</summary>
        Overhead,

        /// <summary>Straight ahead and back: a narrow line that reaches a little further.</summary>
        Thrust,
    }

    /// <summary>
    /// One kind of melee weapon: how hard and how far it hits, how quickly it swings, how its quick and charged
    /// attacks move, and what they cost. Shared asset; put it on the weapon's floating prefab (WeaponView).
    ///
    /// Pressing and letting go within Tap Time is a quick attack. Holding longer winds up a charged attack: the charge
    /// fills over Charge Time, and letting go swings with damage, knockback and stamina cost between the quick
    /// attack's and the full charge's.
    /// </summary>
    [CreateAssetMenu(menuName = "Cleave/Melee Weapon", fileName = "Melee Weapon")]
    public class MeleeWeapon : ScriptableObject
    {
        [Header("Hitting")]
        [Min(0f)] [SerializeField] float damage = 10f;
        [Tooltip("Damage at full charge, as a multiple of Damage.")]
        [Min(1f)] [SerializeField] float chargedDamageMultiplier = 2.5f;
        [Tooltip("Metres from the eyes the swing reaches.")]
        [Min(0.1f)] [SerializeField] float reach = 2f;
        [Tooltip("How thick the swing is (sphere cast radius), so it doesn't have to be pixel perfect.")]
        [Min(0f)] [SerializeField] float hitRadius = 0.15f;
        [Tooltip("Most things one swing can hit. Walls stop it either way.")]
        [Min(1)] [SerializeField] int maxTargets = 1;
        [Tooltip("Impulse (N s) given to what a quick attack hits, along the swing.")]
        [Min(0f)] [SerializeField] float knockback = 2f;
        [Tooltip("Impulse at full charge.")]
        [Min(0f)] [SerializeField] float chargedKnockback = 8f;

        [Header("Timing (seconds)")]
        [Tooltip("Let go sooner than this for a quick attack; hold longer to charge.")]
        [Min(0.01f)] [SerializeField] float tapTime = 0.18f;
        [Tooltip("How long after Tap Time the charge takes to fill.")]
        [Min(0.01f)] [SerializeField] float chargeTime = 0.8f;
        [Tooltip("How long a quick attack's swing takes.")]
        [Min(0.05f)] [SerializeField] float swingTime = 0.28f;
        [Tooltip("How long a charged swing takes.")]
        [Min(0.05f)] [SerializeField] float chargedSwingTime = 0.34f;
        [Tooltip("After a swing, before the next attack can start.")]
        [Min(0f)] [SerializeField] float recoveryTime = 0.22f;
        [Tooltip("Recovery after a fully charged swing.")]
        [Min(0f)] [SerializeField] float chargedRecoveryTime = 0.45f;

        [Header("Swings")]
        [SerializeField] SwingStyle quickStyle = SwingStyle.Slash;
        [SerializeField] SwingStyle chargedStyle = SwingStyle.Overhead;
        [Tooltip("Degrees a slash or overhead swing sweeps through.")]
        [Range(10f, 180f)] [SerializeField] float arc = 110f;

        [Header("Stamina")]
        [Min(0f)] [SerializeField] float staminaCost = 6f;
        [Tooltip("Cost of a fully charged attack.")]
        [Min(0f)] [SerializeField] float chargedStaminaCost = 14f;

        [Header("Holding")]
        [Tooltip("Where the weapon floats while idle, from the eyes (x right, y up, z forward).")]
        [SerializeField] Vector3 holdOffset = new Vector3(0.3f, -0.3f, 0.55f);
        [Tooltip("How it is turned while idle. Weapon prefabs point along their +Y.")]
        [SerializeField] Vector3 holdEuler = new Vector3(35f, 0f, -12f);
        [Tooltip("Walk speed multiplier while winding up a charge.")]
        [Range(0.1f, 1f)] [SerializeField] float chargingSpeed = 0.6f;

        public float Damage => damage;
        public float ChargedDamageMultiplier => chargedDamageMultiplier;
        public float Reach => reach;
        public float HitRadius => hitRadius;
        public int MaxTargets => maxTargets;
        public float Knockback => knockback;
        public float ChargedKnockback => chargedKnockback;
        public float TapTime => tapTime;
        public float ChargeTime => chargeTime;
        public float SwingTime => swingTime;
        public float ChargedSwingTime => chargedSwingTime;
        public float RecoveryTime => recoveryTime;
        public float ChargedRecoveryTime => chargedRecoveryTime;
        public SwingStyle QuickStyle => quickStyle;
        public SwingStyle ChargedStyle => chargedStyle;
        public float Arc => arc;
        public float StaminaCost => staminaCost;
        public float ChargedStaminaCost => chargedStaminaCost;
        public Vector3 HoldOffset => holdOffset;
        public Quaternion HoldRotation => Quaternion.Euler(holdEuler);
        public float ChargingSpeed => chargingSpeed;

        /// <summary>The numbers for a swing let go after holding for <paramref name="held"/> seconds.</summary>
        public SwingSpec SpecFor(float held)
        {
            float charge = SwingMath.Charge(held, tapTime, chargeTime);
            bool charged = held >= tapTime;
            return new SwingSpec
            {
                Charged = charged,
                Charge = charge,
                Style = charged ? chargedStyle : quickStyle,
                Damage = damage * Mathf.Lerp(1f, chargedDamageMultiplier, charge),
                Knockback = Mathf.Lerp(knockback, chargedKnockback, charge),
                StaminaCost = Mathf.Lerp(staminaCost, chargedStaminaCost, charge),
                Duration = charged ? chargedSwingTime : swingTime,
                Recovery = charged ? Mathf.Lerp(recoveryTime, chargedRecoveryTime, charge) : recoveryTime,
            };
        }

        /// <summary>For setting up stand-in weapons from code (editor builders).</summary>
        public void Setup(float damage, float reach, SwingStyle quick, SwingStyle charged)
        {
            this.damage = damage;
            this.reach = reach;
            quickStyle = quick;
            chargedStyle = charged;
        }
    }

    /// <summary>One swing's numbers, worked out when the attack is let go.</summary>
    public struct SwingSpec
    {
        /// <summary>Held past Tap Time (even if barely charged).</summary>
        public bool Charged;
        /// <summary>0..1 how full the charge was.</summary>
        public float Charge;
        public SwingStyle Style;
        public float Damage;
        public float Knockback;
        public float StaminaCost;
        public float Duration;
        public float Recovery;
    }
}
