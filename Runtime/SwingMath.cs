using UnityEngine;

namespace Cleave
{
    /// <summary>A place and turn for the floating weapon, relative to the eyes.</summary>
    public struct WeaponPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public WeaponPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static WeaponPose Lerp(WeaponPose a, WeaponPose b, float t) =>
            new WeaponPose(Vector3.LerpUnclamped(a.Position, b.Position, t), Quaternion.SlerpUnclamped(a.Rotation, b.Rotation, t));
    }

    /// <summary>
    /// The numbers behind a swing: how charged a hold is, which way the swing points as it goes, and where the floating
    /// weapon is. Progress <c>s</c> runs 0..1 across the part of the swing that can hit; below 0 is wound back
    /// behind its start (winding up a charge), so a charged swing begins further back than a quick one.
    /// </summary>
    public static class SwingMath
    {
        /// <summary>How far back (in progress) the wind-up starts a swing, at no charge and at full charge.</summary>
        public const float WindUpBack = 0.15f;
        public const float ChargedWindUpBack = 0.35f;

        /// <summary>0..1 how full the charge is after holding for <paramref name="held"/> seconds.</summary>
        public static float Charge(float held, float tapTime, float chargeTime) =>
            Mathf.Clamp01((held - tapTime) / Mathf.Max(0.0001f, chargeTime));

        /// <summary>0..1 how far a hold has gone past full charge toward <paramref name="autoSwingTime"/> (0 when there
        /// is no auto swing, or it comes before full charge).</summary>
        public static float Overcharge(float held, float tapTime, float chargeTime, float autoSwingTime)
        {
            float full = tapTime + chargeTime;
            if (autoSwingTime <= full) return 0f;
            return Mathf.Clamp01((held - full) / (autoSwingTime - full));
        }

        /// <summary>Where (in progress) a swing starts after winding up to <paramref name="charge"/>.</summary>
        public static float WindUpProgress(float charge) => -Mathf.Lerp(WindUpBack, ChargedWindUpBack, Mathf.Clamp01(charge));

        /// <summary>Progress through a swing that started at <paramref name="start"/>, <paramref name="t"/> 0..1 of its time.
        /// Eases out: fast off the mark, slowing into the follow-through.</summary>
        public static float SwingProgress(float start, float t)
        {
            t = Mathf.Clamp01(t);
            float eased = 1f - (1f - t) * (1f - t);
            return Mathf.LerpUnclamped(start, 1f, eased);
        }

        /// <summary>The turn from straight ahead to where the swing points at progress <paramref name="s"/>.
        /// Mirrored slashes go left to right instead of right to left.</summary>
        public static Quaternion Sweep(SwingStyle style, float s, float arc, bool mirrored)
        {
            switch (style)
            {
                case SwingStyle.Slash:
                    float yaw = Mathf.LerpUnclamped(arc * 0.5f, -arc * 0.5f, s);
                    return Quaternion.AngleAxis(mirrored ? -yaw : yaw, Vector3.up);
                case SwingStyle.Overhead:
                    // Negative pitch is up: from over the head down past straight ahead.
                    float pitch = Mathf.LerpUnclamped(-arc * 0.55f, arc * 0.45f, s);
                    return Quaternion.AngleAxis(pitch, Vector3.right);
                default:
                    return Quaternion.identity;
            }
        }

        /// <summary>The direction a swing points at progress <paramref name="s"/>, in the eyes' space.</summary>
        public static Vector3 Direction(SwingStyle style, float s, float arc, bool mirrored) =>
            Sweep(style, s, arc, mirrored) * Vector3.forward;

        /// <summary>How far a thrust is pushed out, 0..1, at progress <paramref name="s"/>.</summary>
        public static float ThrustExtension(float s) => Mathf.Clamp(s, -0.5f, 1f);

        /// <summary>Can the swing hit anything at progress <paramref name="s"/>? (Not while still wound back.)</summary>
        public static bool CanHit(SwingStyle style, float s) => style == SwingStyle.Thrust ? s >= 0.3f && s <= 1f : s >= 0f && s <= 1f;

        /// <summary>Where the weapon floats while held still.</summary>
        public static WeaponPose Idle(MeleeWeapon weapon) => new WeaponPose(weapon.HoldOffset, weapon.HoldRotation);

        /// <summary>Where the weapon is at progress <paramref name="s"/> of a swing (also below 0, wound back).</summary>
        public static WeaponPose Swing(SwingStyle style, float s, float arc, bool mirrored)
        {
            // A spin turns the player instead: the weapon is held out across the view, leading the turn.
            if (style == SwingStyle.Spin) return Swing(SwingStyle.Slash, 0.62f, 110f, mirrored);
            if (style == SwingStyle.Thrust)
            {
                float z = Mathf.LerpUnclamped(0.4f, 0.95f, ThrustExtension(s));
                return new WeaponPose(new Vector3(0.14f, -0.17f, z), Quaternion.Euler(88f, 0f, 0f));
            }

            Quaternion sweep = Sweep(style, s, arc, mirrored);
            // Out in front along the sweep, blade (the prefab's +Y) pointing the way the swing points...
            Vector3 position = sweep * new Vector3(0f, -0.12f, 0.6f);
            Vector3 blade = sweep * Quaternion.Euler(80f, 0f, 0f) * Vector3.up;
            // ...with its edge (the prefab's +X) leading the way it moves.
            return new WeaponPose(position, EdgeLeading(blade, sweep * Motion(style, mirrored)));
        }

        /// <summary>Which way (before the sweep's turn) a swing moves: right to left, left to right, or down.</summary>
        static Vector3 Motion(SwingStyle style, bool mirrored) =>
            style == SwingStyle.Overhead ? Vector3.down : mirrored ? Vector3.right : Vector3.left;

        /// <summary>A turn that points a weapon's +Y along <paramref name="blade"/> and its edge (+X) as near
        /// <paramref name="motion"/> as it can be.</summary>
        public static Quaternion EdgeLeading(Vector3 blade, Vector3 motion)
        {
            blade.Normalize();
            Vector3 edge = Vector3.ProjectOnPlane(motion, blade);
            if (edge.sqrMagnitude < 1e-8f) edge = Vector3.ProjectOnPlane(Vector3.right, blade);
            Vector3 flat = Vector3.Cross(edge.normalized, blade); // +Z, the way the flat faces
            return Quaternion.LookRotation(flat, blade);
        }

        /// <summary>
        /// How much of a swing's damage lands on something hit <paramref name="degrees"/> off the centre of the view:
        /// all of it dead centre, halving every <paramref name="halfDamageAngle"/> degrees further out. 0 = no falloff.
        /// </summary>
        public static float CentreFalloff(float degrees, float halfDamageAngle) =>
            halfDamageAngle <= 0f ? 1f : Mathf.Pow(0.5f, Mathf.Max(0f, degrees) / halfDamageAngle);

        /// <summary>Shortest a spin takes, as a fraction of its swing time (which is for a half turn).</summary>
        public const float MinSpinTime = 1f / 3f;

        /// <summary>Degrees a spin turns at <paramref name="charge"/>: from <paramref name="low"/> to
        /// <paramref name="high"/> exponentially, so it stays small until the charge is nearly full.</summary>
        public static float SpinArc(float low, float high, float charge)
        {
            charge = Mathf.Clamp01(charge);
            low = Mathf.Max(1f, low);
            high = Mathf.Max(low, high);
            return low * Mathf.Pow(high / low, charge);
        }

        /// <summary>Damage share for the <paramref name="index"/>th thing (0 = first) one swing hits: Hit Falloff
        /// to that power.</summary>
        public static float HitFalloff(int index, float falloff) =>
            index <= 0 ? 1f : Mathf.Pow(Mathf.Clamp01(falloff), index);

        /// <summary>
        /// Where the weapon is drawn back while winding up to <paramref name="charge"/>: cocked to the side it will
        /// swing from (raised for an overhead, pulled in for a thrust), but kept in view so the charge can be seen.
        /// The swing itself then sweeps out from its start, which can be well off screen.
        /// </summary>
        public static WeaponPose WindUp(SwingStyle style, float charge, bool mirrored)
        {
            charge = Mathf.Clamp01(charge);
            if (style == SwingStyle.Spin) style = SwingStyle.Slash;
            switch (style)
            {
                case SwingStyle.Slash:
                {
                    float side = mirrored ? -1f : 1f;
                    var position = new Vector3(side * Mathf.Lerp(0.24f, 0.3f, charge), Mathf.Lerp(-0.22f, -0.18f, charge), Mathf.Lerp(0.55f, 0.5f, charge));
                    // Blade up, leaning back and out toward the side it swings from, edge toward the slash.
                    var rotation = Quaternion.Euler(Mathf.Lerp(-15f, -30f, charge), 0f, -side * Mathf.Lerp(40f, 60f, charge));
                    Vector3 blade = rotation * Vector3.up;
                    return new WeaponPose(position, EdgeLeading(blade, new Vector3(-side, 0f, 0f)));
                }
                case SwingStyle.Overhead:
                {
                    var position = new Vector3(Mathf.Lerp(0.14f, 0.08f, charge), Mathf.Lerp(-0.24f, -0.2f, charge), Mathf.Lerp(0.55f, 0.5f, charge));
                    // Raised high, tipping back over the head, edge toward the chop (forward).
                    var rotation = Quaternion.Euler(Mathf.Lerp(-10f, -30f, charge), 0f, Mathf.Lerp(-12f, -6f, charge));
                    Vector3 blade = rotation * Vector3.up;
                    return new WeaponPose(position, EdgeLeading(blade, Vector3.forward));
                }
                default:
                    return new WeaponPose(new Vector3(0.14f, -0.19f, Mathf.Lerp(0.45f, 0.38f, charge)), Quaternion.Euler(88f, 0f, 0f));
            }
        }

        /// <summary>Smooth 0..1 blend.</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
