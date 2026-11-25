using GameplayTags;
using NUnit.Framework;
using UnityEngine;

namespace Cleave.Tests
{
    public class DamageTypeTests
    {
        [Test]
        public void QuickAndChargedAttacksHaveTheirOwnDamageType()
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeapon>();
            var slash = ScriptableObject.CreateInstance<GameplayTag>();
            var blunt = ScriptableObject.CreateInstance<GameplayTag>();
            try
            {
                Assert.IsNull(weapon.DamageTypeFor(false), "none by default");
                Assert.IsNull(weapon.DamageTypeFor(true), "none by default");

                weapon.SetDamageTypes(slash, blunt);
                Assert.AreSame(slash, weapon.DamageTypeFor(false));
                Assert.AreSame(blunt, weapon.DamageTypeFor(true));
            }
            finally
            {
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(slash);
                Object.DestroyImmediate(blunt);
            }
        }
    }
}
