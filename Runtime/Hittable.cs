using System;
using UnityEngine;

namespace Cleave
{
    /// <summary>
    /// Something melee swings can land on: a monster, a training dummy, a breakable urn. Put it on the root of a
    /// networked object (with a NetworkObject); its colliders, children included, take the hits. The server raises
    /// Struck for each hit, and your code decides what it does (spend health, flinch, break). With Push Rigidbody on,
    /// a non-kinematic Rigidbody here is knocked back too.
    ///
    /// Networked rigidbodies without a Hittable (loose props) are still knocked about by swings, if the Attacker
    /// allows it.
    /// </summary>
    [DisallowMultipleComponent]
    public class Hittable : MonoBehaviour
    {
        [Tooltip("Knock this object's Rigidbody back when hit (server).")]
        [SerializeField] bool pushRigidbody = true;
        [Tooltip("Multiplies the knockback impulse it takes.")]
        [Min(0f)] [SerializeField] float knockbackScale = 1f;
        [Tooltip("Off: swings pass through it as if it weren't there (dead, say).")]
        [SerializeField] bool canBeHit = true;

        Rigidbody body;

        /// <summary>Server: a swing landed on it.</summary>
        public event Action<MeleeHit> Struck;

        /// <summary>Server: a swing landed on any Hittable (for debug panels, hit sounds and the like).</summary>
        public static event Action<Hittable, MeleeHit> AnyStruck;

        public bool CanBeHit
        {
            get => canBeHit && isActiveAndEnabled;
            set => canBeHit = value;
        }

        void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>Server: applies a hit (the Attacker calls this once it has checked the owner's report).</summary>
        public void ServerStrike(MeleeHit hit)
        {
            if (!CanBeHit) return;
            if (pushRigidbody && body != null && !body.isKinematic && hit.Knockback > 0f)
                body.AddForceAtPosition(hit.Direction * hit.Knockback * knockbackScale, hit.Point, ForceMode.Impulse);
            Struck?.Invoke(hit);
            AnyStruck?.Invoke(this, hit);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AnyStruck = null;
    }
}
