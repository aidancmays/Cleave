# Changelog

## 0.2.1

- Winding up and charging now has its own pose that stays in view (cocked to the side for a slash, raised for an
  overhead, pulled in for a thrust) instead of starting the swing's path, which went off screen. Swings are unchanged.

## 0.2.0

- Overcharge: holding on past full charge adds up to Overcharge Damage Bonus (default +20%) more damage, reached
  at Auto Swing Time (default 2.5 s from the press), when the charge swings by itself. 0 turns the auto swing off.
- The weapon shakes from the start of a charge, harder and faster the longer it is held (Attacker Max Shake and
  Max Shake Twist), replacing the tremble at full charge. SwingSpec.Overcharge added.

## 0.1.0

- First version: Attacker (quick and charged melee swings, owner-swept hits checked and applied by the server),
  MeleeWeapon profiles with Slash, Overhead and Thrust swings, WeaponView floating weapons posed procedurally,
  Hittable targets and knockback for loose rigidbodies, TrainingDummy.
