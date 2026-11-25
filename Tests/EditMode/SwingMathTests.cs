using NUnit.Framework;
using UnityEngine;

namespace Cleave.Tests
{
    public class SwingMathTests
    {
        [Test]
        public void ChargeIsZeroUntilTapTimeThenFillsOverChargeTime()
        {
            Assert.AreEqual(0f, SwingMath.Charge(0.1f, 0.2f, 1f), 1e-5f);
            Assert.AreEqual(0f, SwingMath.Charge(0.2f, 0.2f, 1f), 1e-5f);
            Assert.AreEqual(0.5f, SwingMath.Charge(0.7f, 0.2f, 1f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.Charge(5f, 0.2f, 1f), 1e-5f);
        }

        [Test]
        public void OverchargeBuildsFromFullChargeToAutoSwing()
        {
            Assert.AreEqual(0f, SwingMath.Overcharge(1f, 0.2f, 0.8f, 2.5f), 1e-5f);
            Assert.AreEqual(0.5f, SwingMath.Overcharge(1.75f, 0.2f, 0.8f, 2.5f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.Overcharge(9f, 0.2f, 0.8f, 2.5f), 1e-5f);
            Assert.AreEqual(0f, SwingMath.Overcharge(9f, 0.2f, 0.8f, 0f), 1e-5f, "no auto swing, no overcharge");
        }

        [Test]
        public void ShakeGrowsTheLongerItIsHeld()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeapon>();
            try
            {
                Assert.AreEqual(0f, weapon.ShakeFor(weapon.TapTime * 0.5f), 1e-5f);
                float early = weapon.ShakeFor(weapon.TapTime + weapon.ChargeTime);
                float late = weapon.ShakeFor(weapon.AutoSwingTime - 0.1f);
                Assert.Greater(early, 0f);
                Assert.Greater(late, early);
                Assert.AreEqual(1f, weapon.ShakeFor(weapon.AutoSwingTime), 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void WindUpStaysInView([Values] SwingStyle style, [Values(0f, 0.5f, 1f)] float charge, [Values] bool mirrored)
        {
            // The grip, as seen from the eyes, within a 60 degree tall 16:9 view (with some margin).
            Vector3 grip = SwingMath.WindUp(style, charge, mirrored).Position;
            Assert.Greater(grip.z, 0.3f, "in front of the near plane");
            Assert.Less(Mathf.Abs(grip.y) / grip.z, Mathf.Tan(30f * Mathf.Deg2Rad) * 0.9f);
            Assert.Less(Mathf.Abs(grip.x) / grip.z, Mathf.Tan(30f * Mathf.Deg2Rad) * 16f / 9f * 0.9f);
        }

        [Test]
        public void HitFalloffShrinksEachFurtherHit()
        {
            Assert.AreEqual(1f, SwingMath.HitFalloff(0, 0.6f), 1e-5f);
            Assert.AreEqual(0.6f, SwingMath.HitFalloff(1, 0.6f), 1e-5f);
            Assert.AreEqual(0.36f, SwingMath.HitFalloff(2, 0.6f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.HitFalloff(5, 1f), 1e-5f, "1 = no falloff");
            Assert.AreEqual(0f, SwingMath.HitFalloff(1, 0f), 1e-5f, "0 = only the first takes damage");
        }

        [Test]
        public void CentreFalloffHalvesEveryHalfAngle()
        {
            Assert.AreEqual(1f, SwingMath.CentreFalloff(0f, 15f), 1e-5f);
            Assert.AreEqual(0.5f, SwingMath.CentreFalloff(15f, 15f), 1e-5f);
            Assert.AreEqual(0.25f, SwingMath.CentreFalloff(30f, 15f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.CentreFalloff(50f, 0f), 1e-5f, "0 = no falloff");
        }

        [Test]
        public void EdgeLeadsTheSwing([Values(SwingStyle.Slash, SwingStyle.Overhead)] SwingStyle style, [Values] bool mirrored, [Values(0.1f, 0.5f, 0.9f)] float s)
        {
            const float arc = 110f;
            Quaternion turn = SwingMath.Swing(style, s, arc, mirrored).Rotation;
            Vector3 motion = SwingMath.Direction(style, s + 0.01f, arc, mirrored) - SwingMath.Direction(style, s - 0.01f, arc, mirrored);
            Vector3 edge = turn * Vector3.right;
            Vector3 flat = turn * Vector3.forward;
            Assert.Greater(Mathf.Abs(Vector3.Dot(edge, motion.normalized)), 0.9f, "the edge faces the way it moves");
            Assert.Less(Mathf.Abs(Vector3.Dot(flat, motion.normalized)), 0.3f, "not the flat");
        }

        [Test]
        public void IdleShowsTheEdge()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeapon>();
            try
            {
                // The edge (+X) faces back toward the eyes more than the flat (+Z) does.
                Quaternion turn = SwingMath.Idle(weapon).Rotation;
                Assert.Greater(Mathf.Abs((turn * Vector3.right).z), Mathf.Abs((turn * Vector3.forward).z));
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void SpinTurnsFurtherAndLongerWithCharge()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeapon>();
            try
            {
                weapon.Setup(10f, 2f, SwingStyle.Slash, SwingStyle.Spin);
                SwingSpec quick = weapon.SpecFor(0f);
                SwingSpec low = weapon.SpecFor(weapon.TapTime);
                SwingSpec full = weapon.SpecFor(weapon.TapTime + weapon.ChargeTime);
                Assert.AreEqual(0f, quick.SpinArc, "only the spin turns");
                Assert.AreEqual(SwingStyle.Spin, low.Style);
                SwingSpec half = weapon.SpecFor(weapon.TapTime + weapon.ChargeTime * 0.5f);
                Assert.AreEqual(weapon.SpinArc.x, low.SpinArc, 1e-3f);
                Assert.AreEqual(weapon.SpinArc.y, full.SpinArc, 1e-3f);
                Assert.Less(half.SpinArc, (weapon.SpinArc.x + weapon.SpinArc.y) * 0.5f, "exponential, not linear");
                Assert.Greater(half.SpinArc, low.SpinArc);
                Assert.Greater(full.Duration, low.Duration);
                Assert.AreEqual(half.Duration / half.SpinArc, full.Duration / full.SpinArc, 1e-4f, "same turning speed");
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void SwingProgressRunsFromStartToOne()
        {
            Assert.AreEqual(-0.3f, SwingMath.SwingProgress(-0.3f, 0f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.SwingProgress(-0.3f, 1f), 1e-5f);
            Assert.AreEqual(1f, SwingMath.SwingProgress(-0.3f, 3f), 1e-5f);
            Assert.Greater(SwingMath.SwingProgress(0f, 0.5f), 0.5f, "eases out: past halfway at half time");
        }

        [Test]
        public void ChargedWindUpPullsFurtherBack()
        {
            Assert.Less(SwingMath.WindUpProgress(1f), SwingMath.WindUpProgress(0f));
            Assert.Less(SwingMath.WindUpProgress(0f), 0f);
        }

        [Test]
        public void SlashSweepsRightToLeftAndMirrorsLeftToRight()
        {
            Vector3 start = SwingMath.Direction(SwingStyle.Slash, 0f, 90f, false);
            Vector3 end = SwingMath.Direction(SwingStyle.Slash, 1f, 90f, false);
            Assert.Greater(start.x, 0.5f);
            Assert.Less(end.x, -0.5f);
            Assert.Less(SwingMath.Direction(SwingStyle.Slash, 0f, 90f, true).x, -0.5f);
            Assert.AreEqual(1f, SwingMath.Direction(SwingStyle.Slash, 0.5f, 90f, false).z, 1e-4f);
        }

        [Test]
        public void OverheadSweepsFromAboveToBelow()
        {
            Assert.Greater(SwingMath.Direction(SwingStyle.Overhead, 0f, 100f, false).y, 0.5f);
            Assert.Less(SwingMath.Direction(SwingStyle.Overhead, 1f, 100f, false).y, -0.5f);
        }

        [Test]
        public void ThrustGoesStraightAndOnlyHitsWhenOut()
        {
            Assert.AreEqual(Vector3.forward, SwingMath.Direction(SwingStyle.Thrust, 0.7f, 90f, false));
            Assert.IsFalse(SwingMath.CanHit(SwingStyle.Thrust, 0.1f));
            Assert.IsTrue(SwingMath.CanHit(SwingStyle.Thrust, 0.6f));
            Assert.IsFalse(SwingMath.CanHit(SwingStyle.Slash, -0.1f));
        }

        [Test]
        public void SpecScalesWithCharge()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeapon>();
            try
            {
                SwingSpec tap = weapon.SpecFor(0f);
                Assert.IsFalse(tap.Charged);
                Assert.AreEqual(weapon.Damage, tap.Damage, 1e-4f);
                Assert.AreEqual(weapon.StaminaCost, tap.StaminaCost, 1e-4f);
                Assert.AreEqual(weapon.QuickStyle, tap.Style);

                SwingSpec full = weapon.SpecFor(weapon.TapTime + weapon.ChargeTime);
                Assert.IsTrue(full.Charged);
                Assert.AreEqual(1f, full.Charge, 1e-4f);
                Assert.AreEqual(0f, full.Overcharge, 1e-4f);
                Assert.AreEqual(weapon.Damage * weapon.ChargedDamageMultiplier, full.Damage, 1e-3f);
                Assert.AreEqual(weapon.ChargedStaminaCost, full.StaminaCost, 1e-4f);
                Assert.AreEqual(weapon.ChargedStyle, full.Style);

                SwingSpec held = weapon.SpecFor(weapon.AutoSwingTime);
                Assert.AreEqual(1f, held.Overcharge, 1e-4f);
                Assert.AreEqual(full.Damage * (1f + weapon.OverchargeDamageBonus), held.Damage, 1e-3f);

                SwingSpec half = weapon.SpecFor(weapon.TapTime + weapon.ChargeTime * 0.5f);
                Assert.Greater(half.Damage, tap.Damage);
                Assert.Less(half.Damage, full.Damage);
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }
    }
}
