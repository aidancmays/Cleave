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
