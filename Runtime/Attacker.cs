using System;
using System.Collections.Generic;
using GameplayTags;
using Unity.Netcode;
using UnityEngine;

namespace Cleave
{
    /// <summary>Where an Attacker is in an attack.</summary>
    public enum AttackPhase : byte
    {
        Idle,
        /// <summary>The attack is held down: pulling back, and charging once past the weapon's Tap Time.</summary>
        WindingUp,
        Swinging,
        /// <summary>After a swing, before the next one can start.</summary>
        Recovering,
    }

    /// <summary>
    /// Lets a networked player swing a melee weapon: tap the attack for a quick swing, hold it to wind up a charged
    /// one. Put it on the player root (with a NetworkObject) and give it the eyes (Aim). Nothing here reads input:
    /// the owner's input code sets <see cref="TriggerHeld"/> every frame, and whatever equips weapons calls
    /// <see cref="Hold"/> on every machine with the weapon's floating prefab (a WeaponView).
    ///
    /// The owner runs the attack, so it answers at once: it sweeps the swing through the world with sphere casts
    /// from the eyes and tells the server what it hit; the server checks the distance and applies the hit to the
    /// target's Hittable (and knocks loose rigidbodies about). Everyone else is told when a charge starts and when a
    /// swing goes, and moves their copy of the floating weapon the same way.
    ///
    /// Stamina: set <see cref="PayStamina"/> (owner) to pay each swing's cost; returning false cancels the swing.
    /// <see cref="CanStart"/> can refuse a wind-up (exhausted, say).
    /// </summary>
    [DisallowMultipleComponent]
    public class Attacker : NetworkBehaviour
    {
        [Tooltip("The eyes: swings sweep from here, and the weapon floats in front of it. Usually the head or camera.")]
        [SerializeField] Transform aim;
        [Tooltip("Optional. The weapon goes in here (a child of Aim). Empty = one is made under Aim.")]
        [SerializeField] Transform weaponAnchor;
        [Tooltip("What swings can hit (walls included, so they stop swings).")]
        [SerializeField] LayerMask hitMask = Physics.DefaultRaycastLayers;
        [Tooltip("Knock networked rigidbodies that have no Hittable (loose props) about when hit.")]
        [SerializeField] bool pushLooseBodies = true;
        [Tooltip("The server accepts hits this much further than the weapon's reach, for lag.")]
        [Min(0f)] [SerializeField] float reachTolerance = 1.5f;
        [Tooltip("An attack pressed this long before the last one has recovered still starts once it has.")]
        [Min(0f)] [SerializeField] float queueTime = 0.25f;

        [Header("Look")]
        [Tooltip("What a Spin turns round. Empty = this object (the player root).")]
        [SerializeField] Transform spinBody;

        [Tooltip("How far (metres) the idle weapon drifts.")]
        [Min(0f)] [SerializeField] float idleSway = 0.008f;
        [Tooltip("How far (metres) the weapon shakes at its hardest, just before a held charge swings by itself. It builds up from nothing as the charge is held.")]
        [Min(0f)] [SerializeField] float maxShake = 0.02f;
        [Tooltip("How far (degrees) the weapon twists as it shakes at its hardest.")]
        [Min(0f)] [SerializeField] float maxShakeTwist = 4f;
        [Tooltip("How quickly the drawn weapon follows its pose while swinging, and otherwise.")]
        [Min(1f)] [SerializeField] float swingFollow = 45f;
        [Min(1f)] [SerializeField] float follow = 18f;
        [Tooltip("Draw each swing's casts in the Scene view (green: a target, red: something solid).")]
        [SerializeField] bool drawCasts;

        [Header("Tags")]
        [Tooltip("Optional. Held on every machine while winding up or swinging.")]
        [SerializeField] GameplayTag attackingTag;
        [Tooltip("Holding any of these (or their children) stops attacks and cancels a wind-up: knocked out, dead...")]
        [SerializeField] List<GameplayTag> blockedBy = new List<GameplayTag>();

        readonly NetworkVariable<bool> windingUp = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        TagHolder tags;
        bool attackingTagAdded;
        WeaponView view;
        MeleeWeapon weapon;

        AttackPhase phase;
        float phaseStart;
        float windStart;
        SwingSpec spec;
        bool mirrored;
        bool nextMirrored;
        float startProgress;
        float progress;
        float lastProgress;
        float spun;
        int landed;
        bool sweepDone;
        bool lastTrigger;
        float queuedAt = float.NegativeInfinity;
        WeaponPose shown;
        bool hasShown;
        readonly HashSet<Component> struck = new HashSet<Component>();
        static readonly RaycastHit[] casts = new RaycastHit[16];
        static readonly IComparer<RaycastHit> byDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        /// <summary>Owner: is the attack button down this frame? Set it every frame from input.</summary>
        public bool TriggerHeld { get; set; }

        /// <summary>Owner: while true no attack starts and a wind-up is dropped without swinging (carrying something,
        /// a menu open...).</summary>
        public bool Suppressed { get; set; }

        /// <summary>Owner: pays a swing's stamina cost when it is let go. False = it can't be paid, no swing.</summary>
        public Func<float, bool> PayStamina { get; set; }

        /// <summary>Owner: asked before a wind-up starts. False = no attack.</summary>
        public Func<bool> CanStart { get; set; }

        /// <summary>Owner: multiplies every swing's damage (a strength bonus, a buff).</summary>
        public float DamageMultiplier { get; set; } = 1f;

        /// <summary>A swing went (every machine), for sounds and effects.</summary>
        public event Action<SwingSpec> Swung;

        /// <summary>Owner: a swing touched something. True = a target (Hittable or loose body), false = something solid
        /// that stopped it there (a wall). For hit sounds, sparks and screen shake.</summary>
        public event Action<Collider, Vector3, bool> Contact;

        public Transform Aim => aim != null ? aim : transform;
        public AttackPhase Phase => phase;
        public MeleeWeapon Weapon => weapon;
        public WeaponView Held => view;

        /// <summary>Seconds the current wind-up has been held (0 when not winding up).</summary>
        public float HeldFor => phase == AttackPhase.WindingUp ? Time.time - windStart : 0f;

        /// <summary>0..1 how full the charge is while winding up (0 otherwise).</summary>
        public float Charge => phase == AttackPhase.WindingUp && weapon != null ? SwingMath.Charge(HeldFor, weapon.TapTime, weapon.ChargeTime) : 0f;

        /// <summary>Winding up past Tap Time: the swing will be a charged one.</summary>
        public bool IsCharging => phase == AttackPhase.WindingUp && weapon != null && HeldFor >= weapon.TapTime;

        /// <summary>Walk speed multiplier for the attack (slower while charging).</summary>
        public float SpeedMultiplier => IsCharging ? weapon.ChargingSpeed : 1f;

        /// <summary>False while winding up or swinging.</summary>
        public bool CanSprint => phase == AttackPhase.Idle || phase == AttackPhase.Recovering;

        /// <summary>False while the player has a Blocked By tag.</summary>
        public bool CanAttack
        {
            get
            {
                if (tags == null) return true;
                foreach (GameplayTag tag in blockedBy)
                    if (tags.Has(tag)) return false;
                return true;
            }
        }

        void Awake()
        {
            tags = GetComponent<TagHolder>();
            if (weaponAnchor == null)
            {
                weaponAnchor = new GameObject("Weapon Anchor").transform;
                weaponAnchor.SetParent(Aim, false);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) windingUp.OnValueChanged += OnWindingUpChanged;
        }

        public override void OnNetworkDespawn()
        {
            windingUp.OnValueChanged -= OnWindingUpChanged;
        }

        /// <summary>
        /// Every machine: puts a weapon's floating prefab in view (an instance of it, without its colliders and
        /// rigidbodies), or takes the current one away with null. Drops any attack in progress. Returns the instance.
        /// </summary>
        public WeaponView Hold(WeaponView prefab)
        {
            DropAttack();
            if (view != null) Destroy(view.gameObject);
            view = null;
            weapon = null;
            hasShown = false;
            if (prefab == null) return null;

            view = Instantiate(prefab, weaponAnchor, false);
            view.name = prefab.name;
            foreach (Joint joint in view.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint);
            foreach (Collider collider in view.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
            weapon = view.Weapon;
            if (weapon == null) Debug.LogWarning($"Cleave: '{prefab.name}' has no Melee Weapon on its Weapon View, so it can't swing.", prefab);
            else
            {
                shown = SwingMath.Idle(weapon);
                hasShown = true;
                Apply(shown);
            }
            return view;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) UpdateOwner();
            else
            {
                UpdateProgress();
                if (phase == AttackPhase.Recovering && Time.time - phaseStart >= spec.Recovery) phase = AttackPhase.Idle;
            }
            UpdateTag();
        }

        void LateUpdate()
        {
            if (view == null || weapon == null) return;
            WeaponPose target = TargetPose();
            float rate = phase == AttackPhase.Swinging ? swingFollow : follow;
            shown = hasShown ? WeaponPose.Lerp(shown, target, 1f - Mathf.Exp(-rate * Time.deltaTime)) : target;
            hasShown = true;
            Apply(shown);
        }

        void Apply(WeaponPose pose) => view.transform.SetLocalPositionAndRotation(pose.Position, pose.Rotation);

        // ---- Owner ----

        void UpdateOwner()
        {
            bool trigger = TriggerHeld;
            bool pressed = trigger && !lastTrigger;
            lastTrigger = trigger;
            bool blocked = Suppressed || !CanAttack || weapon == null;

            if (blocked && phase == AttackPhase.WindingUp) DropAttack();
            if (pressed && !blocked)
            {
                if (phase == AttackPhase.Idle) BeginWindUp();
                else queuedAt = Time.time;
            }

            switch (phase)
            {
                case AttackPhase.WindingUp:
                    // Let go, or held so long it swings by itself.
                    if (!trigger || (weapon.AutoSwingTime > 0f && Time.time - windStart >= weapon.AutoSwingTime)) LetGo();
                    break;
                case AttackPhase.Swinging:
                    UpdateProgress();
                    break;
                case AttackPhase.Recovering:
                    if (Time.time - phaseStart >= spec.Recovery)
                    {
                        phase = AttackPhase.Idle;
                        if (!blocked && Time.time - queuedAt <= queueTime + spec.Recovery)
                        {
                            queuedAt = float.NegativeInfinity;
                            BeginWindUp();
                            if (!trigger) LetGo();
                        }
                    }
                    break;
            }
        }

        void BeginWindUp()
        {
            if (CanStart != null && !CanStart()) return;
            phase = AttackPhase.WindingUp;
            windStart = Time.time;
            windingUp.Value = true;
        }

        void LetGo()
        {
            float held = Time.time - windStart;
            windingUp.Value = false;
            SwingSpec next = weapon.SpecFor(held);
            next.Damage *= DamageMultiplier;
            if (PayStamina != null && !PayStamina(next.StaminaCost))
            {
                phase = AttackPhase.Idle;
                return;
            }

            // Quick slashes and spins alternate direction.
            bool alternates = Alternates(next.Style, next.Charged);
            bool flip = alternates && nextMirrored;
            if (alternates) nextMirrored = !nextMirrored;
            float start = SwingMath.WindUpProgress(next.Charge);
            BeginSwing(next, flip, start);
            SwingRpc((byte)next.Style, next.Charged, next.Charge, flip, start, next.Duration, next.Recovery, next.SpinArc);
        }

        static bool Alternates(SwingStyle style, bool charged) => style == SwingStyle.Spin || (style == SwingStyle.Slash && !charged);

        /// <summary>Owner: drops a wind-up without swinging (a swing already going finishes).</summary>
        public void CancelWindUp()
        {
            if (phase == AttackPhase.WindingUp) DropAttack();
        }

        void DropAttack()
        {
            if (phase == AttackPhase.WindingUp && IsSpawned && IsOwner) windingUp.Value = false;
            phase = AttackPhase.Idle;
            queuedAt = float.NegativeInfinity;
        }

        void BeginSwing(SwingSpec next, bool flip, float start)
        {
            spec = next;
            mirrored = flip;
            startProgress = start;
            progress = lastProgress = start;
            spun = 0f;
            phase = AttackPhase.Swinging;
            phaseStart = Time.time;
            landed = 0;
            sweepDone = false;
            struck.Clear();
            Swung?.Invoke(spec);
        }

        // Every machine: moves the swing on; the owner also sweeps it for hits.
        void UpdateProgress()
        {
            if (phase != AttackPhase.Swinging) return;
            float t = (Time.time - phaseStart) / Mathf.Max(0.01f, spec.Duration);
            progress = SwingMath.SwingProgress(startProgress, t);
            if (IsOwner && spec.Style == SwingStyle.Spin) Spin();
            else if (IsOwner && !sweepDone && weapon != null) SweepBetween(lastProgress, progress);
            lastProgress = progress;
            if (t >= 1f)
            {
                phase = AttackPhase.Recovering;
                phaseStart = Time.time;
            }
        }

        // Owner: turns the player on round the spin (right to left, or left to right when mirrored), sweeping
        // level ahead as it goes. Something solid only blocks the angles it is in, not the rest of the turn.
        void Spin()
        {
            float target = Mathf.Clamp01(progress) * spec.SpinArc;
            float delta = target - spun;
            if (delta <= 0f) return;
            bool first = spun <= 0f;
            spun = target;
            float turn = mirrored ? 1f : -1f;
            Transform body = spinBody != null ? spinBody : transform;
            body.Rotate(0f, turn * delta, 0f, Space.World);
            if (weapon == null) return;

            // Fill in the angles turned through this frame (the first frame also takes where it started from).
            Transform eyes = Aim;
            Vector3 ahead = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
            if (ahead.sqrMagnitude < 1e-6f) ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            ahead.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, ahead) * turn; // the way the turn moves
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta / 6f));
            for (int i = first ? 0 : 1; i <= steps; i++)
            {
                Quaternion back = Quaternion.AngleAxis(-turn * delta * (1f - (float)i / steps), Vector3.up);
                Vector3 direction = back * ahead;
                Vector3 knock = (direction + back * side).normalized;
                sweepDone = false;
                CastAlong(direction, weapon.Reach, knock, 1f);
            }
            sweepDone = false;
        }

        void SweepBetween(float from, float to)
        {
            if (spec.Style == SwingStyle.Thrust)
            {
                if (SwingMath.CanHit(spec.Style, to))
                    Cast(to, (weapon.Reach + weapon.ThrustReachBonus) * Mathf.Lerp(0.7f, 1f, SwingMath.ThrustExtension(to)));
                return;
            }

            // Cast every few degrees so a fast swing doesn't skip over a thin target.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * weapon.Arc / 6f));
            for (int i = 1; i <= steps && !sweepDone; i++)
            {
                float s = Mathf.Lerp(from, to, (float)i / steps);
                if (SwingMath.CanHit(spec.Style, s)) Cast(s, weapon.Reach);
            }
        }

        void Cast(float s, float distance)
        {
            Transform eyes = Aim;
            Vector3 direction = eyes.rotation * SwingMath.Direction(spec.Style, s, weapon.Arc, mirrored);
            // What is aimed at takes most of it; things caught off to the side take less.
            float share = SwingMath.CentreFalloff(Vector3.Angle(eyes.forward, direction), weapon.HalfDamageAngle);
            CastAlong(direction, distance, KnockDirection(s, direction), share);
        }

        void CastAlong(Vector3 direction, float distance, Vector3 knock, float share)
        {
            Vector3 origin = Aim.position;
            int count = Physics.SphereCastNonAlloc(origin, weapon.HitRadius, direction, casts, distance, hitMask, QueryTriggerInteraction.Ignore);
            Array.Sort(casts, 0, count, byDistance);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = casts[i];
                Collider collider = hit.collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                // A cast that starts inside a collider reports no point.
                Vector3 point = hit.distance <= 0f && hit.point == Vector3.zero ? origin : hit.point;

                Component target = TargetOf(collider, out NetworkObject network);
                if (target == null)
                {
                    if (drawCasts) Debug.DrawLine(origin, point, Color.red, 1f);
                    Contact?.Invoke(collider, point, false);
                    sweepDone = true;
                    return;
                }

                if (drawCasts) Debug.DrawLine(origin, point, Color.green, 1f);
                if (struck.Contains(target)) return;
                struck.Add(target);
                Contact?.Invoke(collider, point, true);
                Report(target, network, point, knock, share * SwingMath.HitFalloff(landed++, weapon.HitFalloff));
                return;
            }
            if (drawCasts) Debug.DrawRay(origin, direction * distance, new Color(1f, 1f, 1f, 0.3f), 1f);
        }

        // What a collider counts as: a Hittable, a loose networked body, or nothing (solid, stops the swing).
        Component TargetOf(Collider collider, out NetworkObject network)
        {
            network = collider.GetComponentInParent<NetworkObject>();
            Hittable hittable = collider.GetComponentInParent<Hittable>();
            if (hittable != null) return hittable.CanBeHit ? hittable : null;
            Rigidbody body = collider.attachedRigidbody;
            if (pushLooseBodies && network != null && body != null && !body.isKinematic) return body;
            return null;
        }

        // Along the swing's motion and outward, so a slash knocks things sideways and a chop knocks them down.
        Vector3 KnockDirection(float s, Vector3 direction)
        {
            if (spec.Style == SwingStyle.Thrust) return direction;
            Vector3 ahead = Aim.rotation * SwingMath.Direction(spec.Style, s + 0.05f, weapon.Arc, mirrored);
            Vector3 motion = ahead - direction;
            return motion.sqrMagnitude < 1e-8f ? direction : (direction + motion.normalized).normalized;
        }

        void Report(Component target, NetworkObject network, Vector3 point, Vector3 direction, float share)
        {
            float damage = spec.Damage * share;
            if (network != null)
                HitRpc(network, point, direction, damage, spec.Knockback, spec.Charged, spec.Charge);
            else if (IsServer && target is Hittable local)
                local.ServerStrike(MakeHit(point, direction, damage, spec.Knockback, spec.Charged, spec.Charge));
        }

        MeleeHit MakeHit(Vector3 point, Vector3 direction, float damage, float knockback, bool charged, float charge) => new MeleeHit
        {
            Attacker = this,
            Weapon = weapon,
            Point = point,
            Direction = direction.sqrMagnitude > 0f ? direction.normalized : Aim.forward,
            Damage = Mathf.Max(0f, damage),
            Knockback = Mathf.Max(0f, knockback),
            Charged = charged,
            Charge = Mathf.Clamp01(charge),
            DamageType = weapon != null ? weapon.DamageTypeFor(charged) : null,
        };

        // ---- Network ----

        [Rpc(SendTo.Server)]
        void HitRpc(NetworkObjectReference target, Vector3 point, Vector3 direction, float damage, float knockback, bool charged, float charge)
        {
            if (!target.TryGet(out NetworkObject network)) return;
            float reach = (weapon != null ? weapon.Reach + weapon.HitRadius : 3f) + reachTolerance;
            if ((point - Aim.position).sqrMagnitude > reach * reach) return;
            if (weapon != null)
            {
                // Trust the owner's sums (co-op), but not wildly.
                damage = Mathf.Min(damage, weapon.Damage * weapon.ChargedDamageMultiplier * 10f);
                knockback = Mathf.Min(knockback, Mathf.Max(weapon.Knockback, weapon.ChargedKnockback) * 2f);
            }

            MeleeHit hit = MakeHit(point, direction, damage, knockback, charged, charge);
            Hittable hittable = network.GetComponentInChildren<Hittable>();
            if (hittable != null)
            {
                hittable.ServerStrike(hit);
                return;
            }
            if (pushLooseBodies && network.TryGetComponent(out Rigidbody body) && !body.isKinematic)
                body.AddForceAtPosition(hit.Direction * hit.Knockback, point, ForceMode.Impulse);
        }

        [Rpc(SendTo.NotMe)]
        void SwingRpc(byte style, bool charged, float charge, bool flip, float start, float duration, float recovery, float spinArc)
        {
            if (IsOwner) return;
            BeginSwing(new SwingSpec
            {
                Style = (SwingStyle)style,
                SpinArc = spinArc,
                Charged = charged,
                Charge = charge,
                Duration = duration,
                Recovery = recovery,
            }, flip, start);
        }

        void OnWindingUpChanged(bool previous, bool current)
        {
            if (current)
            {
                phase = AttackPhase.WindingUp;
                windStart = Time.time;
            }
            else if (phase == AttackPhase.WindingUp)
            {
                // Dropped, or the swing is on its way (SwingRpc).
                phase = AttackPhase.Idle;
            }
        }

        void UpdateTag()
        {
            if (attackingTag == null || tags == null) return;
            bool attacking = phase == AttackPhase.WindingUp || phase == AttackPhase.Swinging;
            if (attacking == attackingTagAdded) return;
            attackingTagAdded = attacking;
            if (attacking) tags.Add(attackingTag);
            else tags.Remove(attackingTag);
        }

        // ---- Look ----

        // A thrust keeps the idle turn about the weapon's length, plus Thrust Twist.
        WeaponPose Twisted(WeaponPose pose, SwingStyle style)
        {
            if (style == SwingStyle.Thrust)
                pose.Rotation *= Quaternion.AngleAxis(weapon.HoldTwist + weapon.ThrustTwist, Vector3.up);
            return pose;
        }

        WeaponPose TargetPose()
        {
            WeaponPose idle = SwingMath.Idle(weapon);
            switch (phase)
            {
                case AttackPhase.WindingUp:
                {
                    float held = Time.time - windStart;
                    bool charging = held >= weapon.TapTime;
                    float charge = SwingMath.Charge(held, weapon.TapTime, weapon.ChargeTime);
                    SwingStyle style = charging ? weapon.ChargedStyle : weapon.QuickStyle;
                    bool flip = IsOwner && Alternates(style, charging) && nextMirrored;
                    WeaponPose back = Twisted(SwingMath.WindUp(style, charge, flip), style);
                    WeaponPose pose = charging ? back : WeaponPose.Lerp(idle, back, SwingMath.Smooth(held / weapon.TapTime));
                    float shake = weapon.ShakeFor(held);
                    if (shake > 0f) Shake(ref pose, shake);
                    return pose;
                }
                case AttackPhase.Swinging:
                    return Twisted(SwingMath.Swing(spec.Style, progress, weapon.Arc, mirrored), spec.Style);
                case AttackPhase.Recovering:
                {
                    WeaponPose end = Twisted(SwingMath.Swing(spec.Style, 1f, weapon.Arc, mirrored), spec.Style);
                    return WeaponPose.Lerp(end, idle, SwingMath.Smooth((Time.time - phaseStart) / Mathf.Max(0.01f, spec.Recovery)));
                }
                default:
                    float t = Time.time;
                    idle.Position += new Vector3(Mathf.Sin(t * 1.3f), Mathf.Sin(t * 2.1f) * 0.7f, 0f) * idleSway;
                    return idle;
            }
        }

        // Shaking gets faster as well as wider the harder it is.
        void Shake(ref WeaponPose pose, float amount)
        {
            float t = Time.time * Mathf.Lerp(20f, 45f, amount);
            Vector3 noise = new Vector3(Mathf.PerlinNoise(t, 0.3f), Mathf.PerlinNoise(0.7f, t), Mathf.PerlinNoise(t, 5.1f)) - Vector3.one * 0.5f;
            pose.Position += new Vector3(noise.x, noise.y, 0f) * (2f * maxShake * amount);
            pose.Rotation *= Quaternion.Euler(noise.y * 2f * maxShakeTwist * amount, 0f, noise.z * 2f * maxShakeTwist * amount);
        }

        public override void OnDestroy()
        {
            if (attackingTagAdded && tags != null) tags.Remove(attackingTag);
            base.OnDestroy();
        }
    }
}
