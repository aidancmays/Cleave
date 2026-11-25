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

        /// <summary>A horizontal sweep that turns the whole player round with it, further the more it is charged
        /// (Spin Arc, on an exponential curve). Alternates direction like slashes. Hits whatever passes in front.</summary>
        Spin,
    }

    /// <summary>
    /// One kind of melee weapon: how hard and how far it hits, how quickly it swings, how its quick and charged
    /// attacks move, and what they cost. Shared asset; put it on the weapon's floating prefab (WeaponView).
    ///
    /// Pressing and letting go within Tap Time is a quick attack. Holding longer winds up a charged attack: the charge
    /// fills over Charge Time, and letting go swings with damage, knockback and stamina cost between the quick
    /// attack's and the full charge's. Holding on past full charge adds a little more damage (Overcharge Damage Bonus)
    /// while the weapon shakes harder and harder, until it swings by itself at Auto Swing Time.
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
        [Tooltip("Each further thing one swing hits takes this fraction of the damage of the one before (0.6 = 100%, 60%, 36%...). No cap on how many; walls stop the swing.")]
        [Range(0f, 1f)] [SerializeField] float hitFalloff = 0.6f;
        [Tooltip("Damage halves for every this many degrees a target is off the centre of the view, so what you aim at takes most of it. 0 = no falloff.")]
        [Min(0f)] [SerializeField] float halfDamageAngle = 15f;
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
        [Tooltip("Seconds from the press after which a held charge swings by itself. 0 = never.")]
        [Min(0f)] [SerializeField] float autoSwingTime = 2.5f;
        [Tooltip("Holding on past full charge adds up to this much more damage (0.2 = +20%), reached at Auto Swing Time.")]
        [Min(0f)] [SerializeField] float overchargeDamageBonus = 0.2f;

        [Header("Swings")]
        [SerializeField] SwingStyle quickStyle = SwingStyle.Slash;
        [SerializeField] SwingStyle chargedStyle = SwingStyle.Overhead;
        [Tooltip("Degrees a slash or overhead swing sweeps through.")]
        [Range(10f, 180f)] [SerializeField] float arc = 110f;
        [Tooltip("Extra metres of reach a thrust has.")]
        [Min(0f)] [SerializeField] float thrustReachBonus = 0.4f;
        [Tooltip("Degrees a Spin turns the player round, barely charged and fully charged. In between it grows exponentially, so a little charge barely turns at all.")]
        [SerializeField] Vector2 spinArc = new Vector2(15f, 360f);

        [Header("Stamina")]
        [Min(0f)] [SerializeField] float staminaCost = 6f;
        [Tooltip("Cost of a fully charged attack.")]
        [Min(0f)] [SerializeField] float chargedStaminaCost = 14f;

        [Header("Holding")]
        [Tooltip("Where the weapon floats while idle, from the eyes (x right, y up, z forward).")]
        [SerializeField] Vector3 holdOffset = new Vector3(0.3f, -0.3f, 0.55f);
        [Tooltip("How it is turned while idle. Weapon prefabs point along their +Y, edges along X.")]
        [SerializeField] Vector3 holdEuler = new Vector3(35f, 0f, -12f);
        [Tooltip("Degrees it is then turned about its own length while idle. 90 = edge toward you.")]
        [SerializeField] float holdTwist = 90f;
        [Tooltip("Walk speed multiplier while winding up a charge.")]
        [Range(0.1f, 1f)] [SerializeField] float chargingSpeed = 0.6f;

        public float Damage => damage;
        public float ChargedDamageMultiplier => chargedDamageMultiplier;
        public float Reach => reach;
        public float HitRadius => hitRadius;
        public float HitFalloff => hitFalloff;
        public float HalfDamageAngle => halfDamageAngle;
        public float Knockback => knockback;
        public float ChargedKnockback => chargedKnockback;
        public float TapTime => tapTime;
        public float ChargeTime => chargeTime;
        public float SwingTime => swingTime;
        public float ChargedSwingTime => chargedSwingTime;
        public float RecoveryTime => recoveryTime;
        public float ChargedRecoveryTime => chargedRecoveryTime;
        public float AutoSwingTime => autoSwingTime;
        public float OverchargeDamageBonus => overchargeDamageBonus;

        /// <summary>0..1 how far a hold has gone past full charge toward Auto Swing Time.</summary>
        public float OverchargeFor(float held) => SwingMath.Overcharge(held, tapTime, chargeTime, autoSwingTime);

        /// <summary>0..1 how hard the weapon shakes after holding for <paramref name="held"/> seconds: from nothing at
        /// Tap Time, building up to full at Auto Swing Time (or at full charge with no auto swing).</summary>
        public float ShakeFor(float held)
        {
            float end = autoSwingTime > tapTime ? autoSwingTime : tapTime + chargeTime;
            float t = Mathf.Clamp01((held - tapTime) / Mathf.Max(0.0001f, end - tapTime));
            return t * t;
        }
        public SwingStyle QuickStyle => quickStyle;
        public SwingStyle ChargedStyle => chargedStyle;
        public float Arc => arc;
        public float ThrustReachBonus => thrustReachBonus;
        public Vector2 SpinArc => spinArc;
        public float StaminaCost => staminaCost;
        public float ChargedStaminaCost => chargedStaminaCost;
        public Vector3 HoldOffset => holdOffset;
        public Quaternion HoldRotation => Quaternion.Euler(holdEuler) * Quaternion.AngleAxis(holdTwist, Vector3.up);
        public float ChargingSpeed => chargingSpeed;

        /// <summary>The numbers for a swing let go after holding for <paramref name="held"/> seconds.</summary>
        public SwingSpec SpecFor(float held)
        {
            float charge = SwingMath.Charge(held, tapTime, chargeTime);
            float overcharge = OverchargeFor(held);
            bool charged = held >= tapTime;
            SwingStyle style = charged ? chargedStyle : quickStyle;
            float spin = style == SwingStyle.Spin ? SwingMath.SpinArc(spinArc.x, spinArc.y, charge) : 0f;
            float duration = charged ? chargedSwingTime : swingTime;
            // A spin's swing time is for a half turn; further turns take longer at the same speed (small ones no less
            // than a third of it).
            if (spin > 0f) duration *= Mathf.Max(SwingMath.MinSpinTime, spin / 180f);
            return new SwingSpec
            {
                Charged = charged,
                Charge = charge,
                Overcharge = overcharge,
                Style = style,
                SpinArc = spin,
                Damage = damage * Mathf.Lerp(1f, chargedDamageMultiplier, charge) * (1f + overchargeDamageBonus * overcharge),
                Knockback = Mathf.Lerp(knockback, chargedKnockback, charge),
                StaminaCost = Mathf.Lerp(staminaCost, chargedStaminaCost, charge),
                Duration = duration,
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
        /// <summary>0..1 how long it was held on past full charge, toward the auto swing.</summary>
        public float Overcharge;
        public SwingStyle Style;
        /// <summary>Degrees a Spin turns the player (0 for other styles).</summary>
        public float SpinArc;
        public float Damage;
        public float Knockback;
        public float StaminaCost;
        public float Duration;
        public float Recovery;
    }
}
