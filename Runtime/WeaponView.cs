using UnityEngine;

namespace Cleave
{
    /// <summary>
    /// The root of a weapon's floating prefab: what is drawn in front of the player while they hold it, and which
    /// MeleeWeapon it swings as. Model it pointing along +Y with the grip at the origin; the Attacker moves it about.
    /// Colliders and rigidbodies on it are removed when it is put in view (Attacker.Hold), so it never bumps into
    /// the player or blocks its own swings.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponView : MonoBehaviour
    {
        [SerializeField] MeleeWeapon weapon;

        public MeleeWeapon Weapon
        {
            get => weapon;
            set => weapon = value;
        }
    }
}
