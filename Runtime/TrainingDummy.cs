using System;
using Unity.Netcode;
using UnityEngine;

namespace Cleave
{
    /// <summary>
    /// A target to practise on: takes any number of hits and rocks on its base, harder the harder it was hit.
    /// Put it on a networked object with a Hittable and colliders; Wobble is the part that rocks (pivoting at its
    /// origin, so put that at the foot). Keeps a tally of the hits it has taken, on every machine.
    /// </summary>
    [RequireComponent(typeof(Hittable))]
    [DisallowMultipleComponent]
    public class TrainingDummy : NetworkBehaviour
    {
        [Tooltip("The part that rocks when hit. Empty = this object.")]
        [SerializeField] Transform wobble;
        [Tooltip("Degrees per second of rocking per unit of knockback.")]
        [Min(0f)] [SerializeField] float kick = 60f;
        [Tooltip("Extra rocking per point of damage.")]
        [Min(0f)] [SerializeField] float kickPerDamage = 2f;
        [Min(0f)] [SerializeField] float maxLean = 35f;
        [Min(0f)] [SerializeField] float stiffness = 90f;
        [Min(0f)] [SerializeField] float damping = 7f;

        Hittable hittable;
        Quaternion rest;
        Vector3 lean;         // axis * degrees
        Vector3 leanVelocity; // degrees per second

        public int Hits { get; private set; }
        public float TotalDamage { get; private set; }
        public float LastDamage { get; private set; }
        public bool LastCharged { get; private set; }

        /// <summary>Raised on every machine when it is hit: damage, charged.</summary>
        public event Action<float, bool> Struck;

        void Awake()
        {
            hittable = GetComponent<Hittable>();
            if (wobble == null) wobble = transform;
            rest = wobble.localRotation;
            hittable.Struck += OnStruck;
        }

        public override void OnDestroy()
        {
            if (hittable != null) hittable.Struck -= OnStruck;
            base.OnDestroy();
        }

        void OnStruck(MeleeHit hit)
        {
            if (IsSpawned) ReactRpc(hit.Direction, hit.Damage, hit.Knockback, hit.Charged);
            else React(hit.Direction, hit.Damage, hit.Knockback, hit.Charged);
        }

        [Rpc(SendTo.Everyone)]
        void ReactRpc(Vector3 direction, float damage, float knockback, bool charged) => React(direction, damage, knockback, charged);

        void React(Vector3 direction, float damage, float knockback, bool charged)
        {
            Hits++;
            TotalDamage += damage;
            LastDamage = damage;
            LastCharged = charged;

            // Tip away from the swing: about the horizontal axis across it.
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, flat.normalized);
            Vector3 localAxis = wobble.parent != null ? wobble.parent.InverseTransformDirection(axis) : axis;
            leanVelocity += localAxis * (kick * knockback + kickPerDamage * damage);
            Struck?.Invoke(damage, charged);
        }

        void Update()
        {
            if (lean.sqrMagnitude < 1e-6f && leanVelocity.sqrMagnitude < 1e-6f) return;
            float dt = Time.deltaTime;
            leanVelocity += (-stiffness * lean - damping * leanVelocity) * dt;
            lean += leanVelocity * dt;
            if (lean.magnitude > maxLean)
            {
                lean = lean.normalized * maxLean;
                leanVelocity *= 0.5f;
            }
            float angle = lean.magnitude;
            wobble.localRotation = angle > 1e-4f ? Quaternion.AngleAxis(angle, lean / angle) * rest : rest;
        }
    }
}
