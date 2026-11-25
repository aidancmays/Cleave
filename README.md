# Cleave

First-person melee for co-op games on Netcode for GameObjects. Tap the attack for a quick swing; hold it to wind up
a charged swing that hits harder, knocks further and costs more stamina. There are no hands or animations: the
weapon floats in front of the view and is swung procedurally.

## Requirements

- Unity 6.3 (6000.3) or newer
- Netcode for GameObjects 2.7+
- **Gameplay Tags** (`com.aidancmays.gameplay-tags`)

## Pieces

| Piece | What it does |
|---|---|
| **Melee Weapon** (Create > Cleave > Melee Weapon) | A kind of weapon: damage, charged damage multiplier, reach, hit radius, most targets per swing, knockback, timings (tap time, charge time, swing and recovery times), the quick and charged swing styles (Slash, Overhead, Thrust, or Spin, which turns the player round further the more it is charged) and arc, stamina costs, where it floats, walk speed while charging. |
| **Weapon View** | The root of a weapon's floating prefab, pointing to its Melee Weapon. Model it along +Y with the grip at the origin. Its colliders and rigidbodies are removed when it is put in view. |
| **Attacker** | On the player root (with a NetworkObject). Holds a Weapon View in front of Aim and runs attacks. |
| **Hittable** | On anything swings can land on (with a NetworkObject). The server raises `Struck(MeleeHit)`; your code spends health, flinches, breaks. Optionally knocks its Rigidbody back. |
| **Training Dummy** | Next to a Hittable: rocks when hit and keeps a tally. |

## How an attack goes

1. Press: the weapon pulls back (wind-up). Let go within the weapon's **Tap Time** for a quick swing.
2. Hold longer and it charges over **Charge Time**, pulling further back. Walk speed drops to **Charging Speed** and
   sprinting stops. The weapon shakes, harder the longer it is held. Holding on past full charge adds up to
   **Overcharge Damage Bonus** more damage, and at **Auto Swing Time** (from the press) it swings by itself.
3. Let go: the stamina cost is paid (`PayStamina`; false = no swing) and the swing sweeps its arc. Damage,
   knockback and cost go from the quick attack's to the full charge's with the charge.
4. After the swing comes **Recovery**. A press during the swing or recovery is queued and starts once it ends.

Quick slashes alternate direction; charged swings use the charged style.

## Wiring it up

Nothing in Cleave reads input or knows your inventory.

- Every frame on the owner: `attacker.TriggerHeld = attackButton.IsPressed()`. Set `Suppressed` while attacks
  shouldn't start (carrying, a menu open): a wind-up in progress is then dropped without swinging.
- On every machine, when the held weapon changes: `attacker.Hold(weaponViewPrefab)` (null to put it away). It
  returns the instance, for adding your own components (a print style, say).
- Optional: `PayStamina = cost => ...`, `CanStart = () => ...`, `DamageMultiplier`, `SpeedMultiplier` and
  `CanSprint` for your movement, the `Swung` and `Contact` events for sounds and effects, Blocked By tags.

## Networking

The owner runs its attack locally, so it feels immediate. It sweeps the swing with sphere casts from Aim (walls stop
it), and sends each hit on a networked object to the server, which checks the distance against the weapon's reach
and applies it to the target's Hittable (or knocks a loose networked rigidbody about). Wind-ups are synced, and
swings are sent to everyone, so other players see the weapon move the same way.
