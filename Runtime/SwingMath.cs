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
            if (style == SwingStyle.Thrust)
            {
                float z = Mathf.LerpUnclamped(0.4f, 0.95f, ThrustExtension(s));
                return new WeaponPose(new Vector3(0.14f, -0.17f, z), Quaternion.Euler(88f, 0f, 0f));
            }

            Quaternion sweep = Sweep(style, s, arc, mirrored);
            // Out in front along the sweep, blade (the prefab's +Y) pointing the way the swing points.
            Vector3 position = sweep * new Vector3(0f, -0.12f, 0.6f);
            float roll = style == SwingStyle.Slash ? (mirrored ? 70f : -70f) : 0f;
            return new WeaponPose(position, sweep * Quaternion.Euler(80f, 0f, 0f) * Quaternion.Euler(0f, roll, 0f));
        }

        /// <summary>Smooth 0..1 blend.</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
