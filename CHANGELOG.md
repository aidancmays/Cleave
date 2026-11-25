# Changelog

## 0.4.0

- Spin swing style: a horizontal sweep that turns the player round with it (Attacker Spin Body, default the root),
  between Spin Arc's two angles (default 180 to 360 degrees) by charge. Its swing time is for the smallest turn;
  bigger turns take longer at the same speed. Spin hits take full damage (they are always straight ahead).
- Thrust Reach Bonus (default 0.4 m) on Melee Weapon.

## 0.3.0

- The weapon's edge (its X axis) now leads every slash and overhead swing and wind-up; while idle it is turned
  Hold Twist (default 90) degrees about its length so the edge faces the player.
- Damage falls off with how far off the centre of the view a target is hit: it halves every Half Damage Angle
  (default 15) degrees, so swings can catch several things but the one aimed at takes most of it. Max Targets
  default raised to 4. Knockback is not reduced.

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
