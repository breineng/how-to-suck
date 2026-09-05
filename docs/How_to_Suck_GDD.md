# HOW TO SUCK
## Final Game Design Document

### Document purpose

**How to Suck** is a small co-op first-person physics game inspired by the progression philosophy and simple stylized presentation of **How to Fish**, but with a completely different core mechanic.

The project is intentionally scoped so that an AI coding agent can reasonably attempt to create the game almost independently from a single detailed prompt. The game should avoid systems that require large amounts of bespoke logic, content scripting, complex AI, advanced networking or handcrafted interactions.

The main source of depth should come from:

- real-time physics;
- simple numerical progression;
- co-op interaction;
- level layout;
- object mass, size and value;
- player greed under a time limit.

The game should be easy to understand immediately, but should naturally produce chaotic and funny situations in multiplayer.

---

# 1. High concept

Players are a crew of absurd industrial cleaners equipped with powerful vacuum devices.

They arrive at cluttered locations and must collect enough valuable junk before time runs out.

Small objects can be physically sucked directly into a player's vacuum.

Large objects may be too heavy to move effectively or too large to fit into the player's intake. In that case, players can use vacuum force like a reverse gravity gun to drag, lift, rotate and manipulate the object.

Outside the level waits the team's large collection vehicle, the **Suck Truck**.

The truck has a huge industrial intake capable of swallowing objects that personal vacuums cannot.

Players therefore constantly choose between:

- quickly sucking small items directly;
- spending time moving large, valuable objects to the truck;
- leaving as soon as the quota is complete;
- risking the run by staying longer for expensive loot.

The central fantasy is:

> Something that used to be almost impossible to move eventually becomes something you casually suck into your own vacuum.

---

# 2. Core design pillars

## 2.1 Physical interaction is the main feature

Objects must not disappear through a progress bar.

They should physically react to vacuum force.

A chair should:

- slide;
- rotate;
- collide with walls;
- get stuck in doorways;
- tumble down stairs;
- be pulled through windows;
- hit other objects;
- be manipulated by multiple players simultaneously.

The game should deliberately allow players to solve problems in unintended physical ways.

If a solo player manages to push a piano out of a second-floor window and somehow get it into the truck, the game should accept that solution.

There should be as few artificial restrictions as possible.

---

## 2.2 Progression should be immediately visible

Progression should not mainly consist of invisible percentage bonuses.

The player should feel stronger because interactions that were previously difficult become trivial.

Example:

Early game:

> A refrigerator barely moves.

Later:

> The refrigerator slides rapidly toward the player.

Even later:

> The player's intake is large enough to swallow the refrigerator directly.

The game should repeatedly create this feeling of:

> "I remember when this thing was a problem."

---

## 2.3 Co-op should emerge from physics

The game should not use arbitrary rules such as:

> Requires 3 players.

Instead, all players simply apply real physical force to the same Rigidbody.

If one vacuum is too weak, another player can help.

If two players pull from different directions, the resulting forces naturally rotate or reposition the object.

This should create spontaneous coordination without dedicated co-op minigames.

---

## 2.4 Greed creates the main strategic tension

Each contract has:

- a required money quota;
- a time limit.

Once the quota is reached, players may leave immediately.

However, valuable objects remain inside the level.

The decision becomes:

> Do we leave safely, or try to drag that $1,500 safe out before the timer ends?

The player should be able to make a bad decision.

That is the main source of failure and tension.

---

## 2.5 Simplicity over feature count

Every new content variation should preferably reuse existing systems.

A new object should ideally require only:

- model;
- collider;
- Rigidbody;
- mass;
- monetary value;
- intake size requirement.

A new map should reuse the same gameplay systems.

Avoid designing mechanics that require unique code for individual items.

---

# 3. Player count

Target:

**1 to 4 players**

The game must be fully playable solo.

Co-op should make difficult physical tasks easier, but no object should be permanently locked behind a player-count requirement.

A sufficiently creative solo player should theoretically be able to move objects above their intended power level through physics and environmental manipulation.

---

# 4. Perspective and controls

First-person.

Basic controls:

| Input | Action |
|---|---|
| WASD | Move |
| Mouse | Look |
| Shift | Sprint |
| Space | Jump |
| LMB | Activate vacuum |
| E | Interact |
| Esc | Menu |

No stamina system.

No crouching unless required by level design.

No complex movement mechanics.

---

# 5. The vacuum

The vacuum is the core tool and should be fun even without progression.

It has two primary functions.

## 5.1 Physical suction

While the player holds LMB, the vacuum creates a suction field in front of the nozzle.

Suckable Rigidbody objects inside this field receive force toward the nozzle.

Conceptually:

```text
Vacuum force
↓
Rigidbody
↓
Object accelerates toward nozzle
```

The exact physics implementation should prioritize stability over realism.

The force should feel responsive and slightly exaggerated.

The game is not a physically accurate vacuum simulator.

---

## 5.2 Object mass

Object mass naturally determines how easily it moves.

Examples:

- bottle: flies immediately;
- cardboard box: easily pulled;
- chair: slides and rotates;
- television: noticeably heavy;
- refrigerator: difficult;
- piano: extremely difficult;
- forklift: nearly impossible early in the game.

There should be no explicit UI saying:

> You need Vacuum Level 4.

The player learns through physical feedback.

---

# 6. Suction power

Each personal vacuum has a **Suction Power** stat.

Suction Power affects the physical force applied to objects.

More powerful vacuums:

- accelerate heavy objects faster;
- lift heavier objects more easily;
- allow one player to perform tasks that previously required teammates.

Multiple players can target the same object.

Their forces are applied independently and therefore naturally combine.

Example:

```text
Player A pulls right
Player B pulls upward
Object moves diagonally
```

This should allow players to:

- rotate furniture;
- lift opposite ends of long objects;
- pull large objects through doors;
- rescue objects stuck on geometry;
- throw or launch objects using momentum.

---

# 7. Intake size

Suction power and swallowing ability are separate concepts.

A vacuum can be strong enough to move an object but still have an intake too small to swallow it.

Each personal vacuum therefore has an **Intake Size** stat.

Each suckable object has an approximate **Required Intake Size**.

If:

```text
Vacuum Intake Size >= Object Required Intake Size
```

the object can be swallowed by the player's vacuum.

If not:

- suction force still affects the object;
- the player can drag or lift it;
- the object cannot enter the personal vacuum.

This separation creates an important progression layer.

Example:

A player upgrades suction power.

Now the refrigerator is easy to move.

But the intake is still too small.

The player must still drag it to the truck.

Later, after upgrading intake size, the same refrigerator can be swallowed directly.

---

# 8. Physical swallowing

When a compatible object reaches the vacuum nozzle, it should be physically and visually swallowed.

This is one of the game's signature presentation features.

## 8.1 Cartoon nozzle deformation

The nozzle should temporarily expand to fit the incoming object.

The intended visual reference is cartoon swallowing, similar to a snake swallowing something much larger than its normal body width.

Example:

1. chair approaches nozzle;
2. nozzle widens;
3. chair begins entering;
4. nozzle stretches around the chair;
5. chair disappears inside;
6. a visible bulge travels down the hose;
7. hose returns to normal.

The effect does not need physically accurate deformation.

A stylized scale/deformation animation is sufficient.

The important thing is that the object visibly feels like it passed through the vacuum rather than simply despawning.

---

## 8.2 Swallow animation

During final ingestion:

- object physics can temporarily become controlled;
- object may scale or compress;
- nozzle widens;
- object moves into intake;
- sound effect plays;
- hose bulge animation plays;
- object is removed;
- money is credited.

The physics-to-animation transition should be intentionally forgiving to avoid objects getting stuck at the intake.

---

# 9. The Suck Truck

Every contract has a large collection vehicle parked outside the main play area.

The truck acts as:

- extraction point;
- collection point for oversized objects;
- visual home base during the contract.

It should have a huge industrial intake at the rear.

The truck intake can swallow almost any intended collectible object regardless of personal vacuum intake size.

---

# 10. Why the truck matters

The truck creates a second category of gameplay.

## Small objects

Can be swallowed directly.

Examples:

- bottles;
- cans;
- boxes;
- small electronics;
- kitchen items.

These are fast, safe income.

## Large objects

Must often be physically transported to the truck.

Examples:

- refrigerator;
- couch;
- piano;
- safe;
- industrial machine;
- forklift.

These are slower and riskier, but worth much more money.

This creates a natural economy of:

> easy low-value loot vs difficult high-value loot.

---

# 11. Transporting large objects

Large-object transport should rely almost entirely on physics.

Players should be allowed to:

- drag;
- pull;
- push indirectly;
- lift;
- rotate;
- roll;
- drop;
- throw;
- slide;
- launch;
- use stairs;
- use slopes;
- use windows.

No custom carrying system is required.

No "grab both handles" interaction is required.

The vacuum itself is the manipulation tool.

---

# 12. Co-op object manipulation

Multiple players may target one object simultaneously.

Example: moving a couch through a doorway.

Player A pulls from outside.

Player B pulls one side sideways to rotate it.

Player C clears smaller obstacles.

Player D pulls from behind.

The system should not explicitly coordinate them.

The players coordinate themselves.

This is intended to create moments similar to physical co-op games where the humor comes from imperfect human coordination.

---

# 13. Solo possibilities

The game should never say that an object is impossible solo.

A single player may discover creative solutions.

Examples:

- push object down stairs;
- drop object through a window;
- use gravity;
- build momentum;
- pull it down a slope;
- move nearby clutter out of the way;
- repeatedly reposition it;
- use geometry as leverage.

This creates emergent gameplay without additional systems.

---

# 14. Object data

Almost all collectible level objects should use one generic component.

Conceptual structure:

```csharp
SuckableObject
{
    float Value;
    float RequiredIntakeSize;
}
```

Mass comes from Rigidbody.

Optional lightweight properties may include:

```csharp
bool CanBeSwallowedByPlayer;
bool CanBeSwallowedByTruck;
```

Avoid unique scripts for individual objects.

---

# 15. Object categories

Suggested approximate categories:

| Category | Examples |
|---|---|
| Tiny | cans, bottles, cutlery |
| Small | boxes, pots, books |
| Medium | chairs, microwaves, monitors |
| Large | TVs, office chairs, small cabinets |
| Heavy | refrigerators, couches, washing machines |
| Massive | pianos, safes, machines, forklifts |

These categories are primarily for content balancing.

Physics should still use actual Rigidbody mass.

---

# 16. Economy

There is only one currency:

**Money**

No:

- XP;
- crafting materials;
- skill points;
- reputation;
- multiple currencies.

When an object is successfully collected, its monetary value is added to the contract total.

Small items collected through personal vacuums count immediately.

Large items count when swallowed by the Suck Truck.

---

# 17. Contract structure

Every level is a timed contract.

Each contract defines:

```text
Required Quota
Time Limit
Available Loot
```

Example:

```text
OLD HOUSE

Quota: $1,500
Time: 7:00
```

Players must collect at least the quota before the timer ends.

---

# 18. Contract loop

```text
Choose location
↓
Arrive with Suck Truck
↓
Timer starts
↓
Search level
↓
Collect small valuables
↓
Discover large valuable objects
↓
Decide whether they are worth transporting
↓
Reach quota
↓
Choose whether to leave or keep looting
↓
Return to truck
↓
Extract
↓
Receive money
↓
Upgrade equipment
↓
Next contract
```

---

# 19. Extraction

Once quota is reached, the team is allowed to leave.

Near the truck is an extraction zone.

When all currently active players are inside the zone:

```text
HOLD E TO LEAVE
```

After a short hold:

- contract ends;
- team keeps collected money;
- results screen appears.

Requiring all players to return creates simple multiplayer tension.

Someone will inevitably still be inside trying to steal one last expensive object.

---

# 20. Time limit

The timer exists to make greed dangerous.

Without it, the optimal strategy would always be:

> collect everything.

The timer creates decisions.

Example:

```text
Quota: $3,000
Collected: $4,200
Time remaining: 00:52
```

Players notice a safe worth $1,500 upstairs.

They must decide whether to risk it.

---

# 21. Failure

Keep failure rules simple.

Recommended:

## Successful extraction

Players keep all collected contract money.

## Timer expires before extraction

Contract fails.

Players keep only a small percentage of earnings, for example:

**25%**

This discourages reckless greed without completely deleting progress.

Exact percentage can be tuned after testing.

No player deaths are required.

---

# 22. No combat

The game should not contain:

- enemies;
- weapons;
- health;
- damage;
- death;
- revives;
- combat AI.

The challenge comes from:

- physics;
- object size;
- level layout;
- time;
- greed;
- cooperation.

This dramatically reduces implementation scope.

---

# 23. Vacuum progression

Use a very small progression system.

The player upgrades only two meaningful properties:

1. **Suction Power**
2. **Intake Size**

These may be presented as new vacuum models rather than tiny incremental upgrades.

---

# 24. Vacuum tiers

Example structure.

## MK1

Starter vacuum.

```text
Power: Low
Intake: Small
```

Good for:

- trash;
- bottles;
- boxes;
- small props.

Heavy furniture is difficult.

---

## MK2

```text
Power: Medium
Intake: Medium
```

Can easily manipulate:

- chairs;
- TVs;
- small furniture.

Can swallow more medium-sized objects.

---

## MK3

```text
Power: High
Intake: Large
```

Large household appliances become manageable.

Objects that previously required truck delivery can now sometimes be swallowed directly.

---

## MK4

Industrial vacuum.

```text
Power: Very High
Intake: Very Large
```

Can casually move extremely heavy objects.

Can swallow many large props directly.

Massive industrial objects still encourage truck use.

---

# 25. Why upgrade tiers are preferable

Do not create a complex skill tree.

The player should have clear goals:

> I need $5,000 for MK3.

Buying a new vacuum should feel transformative.

It is easier to:

- understand;
- balance;
- implement;
- present visually;
- generate with AI.

---

# 26. Visual vacuum progression

Each tier should look visibly more absurd.

## MK1

Small improvised machine.

Looks weak and cheap.

## MK2

Larger motor and hose.

## MK3

Industrial backpack or turbine.

## MK4

Comically oversized machine that looks barely portable.

The final vacuum should visually communicate:

> This thing should not be legal.

---

# 27. Level progression

The first complete version should contain only a few reusable levels.

Three strong locations are better than ten weak ones.

Recommended initial structure:

1. Old House
2. Supermarket
3. Warehouse

---

# 28. Level 1: Old House

A cluttered two-story house.

Purpose:

- tutorial through gameplay;
- teach basic suction;
- introduce heavy objects;
- introduce truck delivery.

Typical items:

- bottles;
- boxes;
- books;
- cookware;
- chairs;
- lamps;
- microwave;
- television;
- small cabinets;
- refrigerator;
- couch.

Signature oversized object:

**Piano**

Early players can barely move it.

Its high value encourages the whole team to attempt transporting it.

Later, returning with stronger equipment should make the piano dramatically easier.

---

# 29. Old House layout principles

The map should create physical transport problems.

Include:

- narrow doors;
- hallway corners;
- staircase;
- second floor;
- windows;
- backyard;
- front entrance.

The environment should deliberately allow shortcuts.

For example, throwing a refrigerator out a window should be valid.

Avoid invisible walls that unnecessarily block creative solutions.

---

# 30. Level 2: Supermarket

Larger and more open than the house.

Typical items:

- food boxes;
- baskets;
- shopping carts;
- checkout props;
- shelving;
- TVs or electronics;
- vending machines;
- refrigerators;
- freezer units;
- pallets.

The environment introduces more medium and heavy objects.

Signature oversized object:

**Large freezer display**

High value.

Awkward shape.

Requires coordination or clever physical manipulation.

---

# 31. Level 3: Warehouse

The largest first-version level.

Typical items:

- crates;
- barrels;
- pallets;
- metal cabinets;
- generators;
- machine tools;
- industrial equipment.

Signature objects:

- industrial machine;
- large safe;
- forklift.

The forklift should act as the first major "can we actually suck that?" moment.

It should be extremely difficult early in progression and satisfying later.

---

# 32. Replaying old locations

Old maps remain relevant.

Progression should change how players perceive them.

First visit to Old House:

> refrigerator = major problem.

Later visit:

> refrigerator = trivial.

This is valuable because it creates progression payoff without requiring additional content.

The same map effectively plays differently with stronger equipment.

---

# 33. Level design philosophy

Level design should focus on:

- object placement;
- transport paths;
- choke points;
- verticality;
- shortcuts;
- risky valuable objects.

Do not rely on:

- puzzles;
- scripted events;
- locked-door quest chains;
- NPC interactions.

The level itself should act as a physical obstacle course for large objects.

---

# 34. Value placement

Valuable objects should become harder to retrieve.

Example:

Low-value items:

- near entrances;
- easy to suck;
- plentiful.

High-value items:

- upstairs;
- behind narrow corridors;
- in awkward rooms;
- deep inside the level.

This creates natural risk without introducing additional mechanics.

---

# 35. Emergent gameplay examples

The design should intentionally support situations such as:

### Piano on staircase

Players try to rotate it.

It wedges sideways.

One player pulls too hard.

The piano rolls downstairs and knocks everyone around.

### Refrigerator shortcut

Instead of carrying it downstairs, a player throws it out the window.

The team runs outside and drags it to the truck.

### Conflicting forces

Two players try to help but pull a couch in opposite directions.

It spins uncontrollably.

### Last-second greed

Quota is complete.

One player insists on getting a safe.

Everyone returns to help.

The timer reaches ten seconds.

The team abandons the safe and sprints for the truck.

These moments are the actual "content" of the game.

---

# 36. Physics philosophy

Physics should be exaggerated and readable.

Priorities:

1. fun;
2. stability;
3. responsiveness;
4. comedic results;
5. realism.

Objects should have enough weight to feel meaningful, but not so much that movement becomes tedious.

The vacuum should feel powerful.

Players should be able to create ridiculous momentum.

---

# 37. Physics implementation constraints

Because the game is intended to be largely produced by an AI coding agent, physics should remain technically conservative.

Recommended:

- standard Unity Rigidbody;
- standard colliders;
- AddForce or equivalent controlled force;
- capped velocities where necessary;
- simple drag tuning;
- minimal custom solver logic.

Avoid:

- soft-body simulation;
- rope simulation;
- complex destructible meshes;
- runtime mesh cutting;
- bespoke physical joints for every object.

---

# 38. Multiplayer physics

Multiplayer physics is the largest technical risk.

The project should minimize networking complexity while preserving the appearance of physical interaction.

Recommended high-level rule:

> The network authority simulates collectible Rigidbody objects.

Clients send vacuum interaction intent.

The authority applies suction forces and synchronizes the resulting transforms.

Exact implementation depends on the networking solution chosen by the AI agent.

The design should favor:

- host/server authority;
- simple ownership rules;
- low object counts;
- no prediction-heavy gameplay requirement.

The game does not require competitive precision.

Small physics discrepancies are acceptable if the experience remains funny and playable.

---

# 39. Object count

Do not create thousands of active Rigidbody objects.

Each map should use a manageable number of meaningful physical objects.

Suggested target:

**50 to 150 active suckable objects per level**

Exact amount depends on performance testing.

Small decorative clutter can be static or combined where necessary.

---

# 40. Suck targeting

The vacuum should feel broad and forgiving.

The player should not need pixel-perfect aim.

Recommended behavior:

- forward cone;
- distance limit;
- preference for objects near screen center;
- continuous force while LMB is held.

The suction field may affect multiple small objects simultaneously.

This is desirable.

A pile of cans exploding toward the nozzle should feel satisfying.

---

# 41. Multiple-object suction

Small lightweight objects should be able to fly toward the vacuum together.

This is important for visual satisfaction.

Examples:

- pile of bottles;
- kitchen utensils;
- small boxes;
- trash.

Large objects naturally dominate the player's attention because they move more slowly.

No artificial single-target lock is necessary unless required for stability.

---

# 42. Truck intake

The Suck Truck uses the same basic visual language as the player's vacuum, but at a much larger scale.

When a large object reaches the truck intake:

1. truck intake activates;
2. object is physically pulled toward it;
3. intake expands if necessary;
4. object is swallowed;
5. large cartoon bulge/effect plays;
6. money is added.

The truck should make oversized object delivery feel like a payoff.

---

# 43. Truck presentation

The truck should be visually memorable.

Possible design:

- ugly industrial van/truck;
- enormous rear turbine;
- oversized flexible intake;
- pipes and tanks;
- shaky engine;
- absurd suction effects.

The truck should communicate the game's tone immediately.

---

# 44. Art style

Use the same broad philosophy that makes How to Fish readable and charming:

- low-poly;
- stylized;
- simple materials;
- strong silhouettes;
- exaggerated proportions;
- intentionally rough/comedic visual character.

Do not copy identifiable assets or exact visual design.

The goal is stylistic simplicity, not imitation.

---

# 45. Environment art

Keep environments visually simple enough that AI-assisted production is realistic.

Recommended:

- modular walls;
- simple props;
- flat or low-detail materials;
- small texture sets;
- baked or simple real-time lighting.

Avoid:

- photorealism;
- dense foliage systems;
- advanced shaders;
- cinematic post-processing pipelines.

---

# 46. Character art

Characters should be simple low-poly workers.

Requirements:

- readable silhouette;
- basic locomotion animation;
- arms/hands sufficient to hold vacuum;
- simple multiplayer color variation.

No complex facial animation.

No dialogue system.

No character customization system required for first version.

---

# 47. UI

Keep in-game UI extremely small.

Main HUD:

```text
$1,850 / $2,500

03:42
```

Optional:

- small extraction prompt;
- vacuum upgrade indicator;
- contextual interaction prompt.

Avoid:

- minimap;
- quest tracker;
- inventory grid;
- health bars;
- stamina;
- ability cooldowns.

---

# 48. Audio

Audio is disproportionately important because the core mechanic is simple.

Vacuum should have:

- idle motor loop;
- active suction loop;
- pitch response based on load;
- rattling when objects approach;
- large swallowing sound;
- truck swallowing sound.

Large objects should sound heavier.

The final ingestion should produce a very satisfying:

> FWUMP / SCHLOOP / THOOMP

Audio should exaggerate the cartoon feel.

---

# 49. Camera feedback

Use modest camera feedback.

Possible effects:

- slight shake when heavy object enters intake;
- stronger shake for truck ingestion;
- small FOV reaction when vacuum activates;
- subtle impulse when something massive is swallowed.

Avoid excessive motion sickness.

---

# 50. Object feedback

An object being sucked should visibly communicate force.

Possible effects:

- small vibration;
- rotation toward nozzle;
- dust particles;
- stretched motion;
- slight cartoon squash;
- collisions with nearby objects.

Do not use floating progress bars over objects.

The player should understand resistance from physics.

---

# 51. Shop

After contracts, players can purchase new vacuum tiers.

Shop should be extremely simple.

Example:

```text
MK2 VACUUM
$2,000

Power: ++
Intake: ++

BUY
```

No shop NPC required.

A simple menu is enough.

---

# 52. Progression pacing

The first complete version should be short.

Suggested target:

**45 to 90 minutes to see the full progression once**

The project is primarily designed as:

- a fun playable prototype;
- a YouTube development experiment;
- something immediately understandable with friends.

Do not artificially extend playtime.

---

# 53. Example progression

Possible first balancing pass:

```text
Start
MK1

Old House
↓
Earn ~$2,000
↓
Buy MK2
↓
Supermarket
↓
Earn ~$6,000
↓
Buy MK3
↓
Warehouse
↓
Earn ~$15,000
↓
Buy MK4
↓
Replay levels and trivialize previously difficult objects
```

Numbers are placeholders and should be tuned through testing.

---

# 54. Main progression payoff

The game should deliberately reuse iconic difficult objects.

Example:

First hour:

> Four players struggle to move the Old House piano.

Later:

> One player returns with MK4 and casually sucks the entire piano through the nozzle.

This should be one of the game's strongest emotional rewards.

---

# 55. Content production rules

Every new collectible prop should preferably require no new gameplay code.

Pipeline:

```text
Import model
↓
Add collider
↓
Add Rigidbody
↓
Add SuckableObject
↓
Set Value
↓
Set Required Intake Size
↓
Done
```

This is critical for keeping the project feasible for autonomous AI development.

---

# 56. Explicitly excluded mechanics

The first version should NOT contain:

- enemies;
- monsters;
- combat;
- guns;
- health;
- death;
- revive;
- crafting;
- inventory management;
- skill trees;
- quests;
- dialogue;
- NPC AI;
- procedural generation;
- destructible buildings;
- runtime mesh destruction;
- vehicles that players drive;
- weather;
- day/night cycle;
- survival stats;
- hunger;
- stamina;
- complex character abilities;
- individual custom logic for every prop.

If the AI agent proposes one of these systems, it should reject the addition unless it is absolutely necessary for the existing core loop.

---

# 57. Technical simplicity rule

The project's primary engineering principle:

> Prefer emergent behavior from a small number of reusable systems over handcrafted mechanics.

Examples:

Bad:

> Create a custom piano-carrying mechanic.

Good:

> Let the piano be a heavy Rigidbody affected by vacuum forces.

Bad:

> Create a special refrigerator extraction sequence.

Good:

> Let players physically drag the refrigerator however they want.

Bad:

> Add a bespoke two-player interaction.

Good:

> Let two vacuum forces naturally combine.

---

# 58. AI development constraint

The game is intended to be created mostly autonomously by an AI coding agent from one large specification.

Therefore the agent should:

1. create the smallest viable architecture;
2. implement reusable generic systems;
3. avoid speculative features;
4. avoid overengineering;
5. avoid creating abstractions without a clear need;
6. use placeholder art where necessary;
7. prioritize a complete playable loop over polish;
8. keep every mechanic testable independently;
9. prefer built-in engine systems;
10. not expand scope without explicit instruction.

---

# 59. Minimum playable version

The true MVP needs only:

- one first-person player;
- one vacuum;
- physics suction;
- physical swallowing;
- cartoon nozzle expansion;
- generic SuckableObject;
- one room;
- several object sizes;
- Suck Truck;
- truck ingestion;
- money quota;
- timer;
- extraction.

The AI should prove this loop works before implementing multiplayer or additional maps.

---

# 60. Vertical slice

After the MVP, create one complete Old House contract.

Vertical slice requirements:

- 1 to 4 players;
- Old House;
- Suck Truck;
- 20+ suckable props;
- at least 5 noticeably different mass classes;
- quota;
- timer;
- extraction;
- one vacuum upgrade;
- one large signature object such as piano;
- functional UI;
- basic sound;
- basic stylized presentation.

If this is fun, only then build additional content.

---

# 61. Final first-version scope

Target final prototype:

### Systems

- first-person controller;
- multiplayer 1-4;
- physical vacuum suction;
- personal intake swallowing;
- deforming cartoon nozzle;
- Suck Truck;
- large-object transportation;
- money;
- timed contracts;
- quota;
- extraction;
- vacuum progression;
- saving progression.

### Content

- 3 maps;
- 4 vacuum tiers;
- approximately 30-50 reusable object types;
- several massive signature objects.

---

# 62. What makes the game fun

The game should not be fun because it has many mechanics.

It should be fun because simple systems collide.

A valuable object is upstairs.

It is heavy.

The door is narrow.

The timer is running.

Four players pull from different directions.

The object suddenly falls downstairs.

Someone gets hit.

The team starts laughing.

Then someone realizes:

> "Throw it through the window."

That is the game.

---

# 63. One-sentence pitch

> **How to Suck is a 1-4 player co-op physics game where increasingly powerful vacuums let you physically drag, launch and eventually swallow progressively larger junk while racing to fill a giant Suck Truck before time runs out.**

---

# 64. Core loop in one line

```text
Suck → Drag → Deliver → Earn → Upgrade → Suck Bigger Things
```

---

# 65. Design test

Whenever a new feature is proposed, ask:

> Does this make sucking, moving, transporting or upgrading objects more fun?

If the answer is no, it probably does not belong in the first version.

---

# 66. Final design identity

The game should feel like:

- **How to Fish** in progression simplicity and stylized absurdity;
- a physics sandbox in moment-to-moment interaction;
- a co-op game in the chaos created by multiple players manipulating the same objects;
- an extraction game only in the very lightweight sense of quota, greed and choosing when to leave.

It should **not** become:

- a cleaning simulator;
- a combat game;
- a survival game;
- a complicated extraction shooter;
- a realistic logistics simulator.

The entire game should revolve around one increasingly ridiculous question:

> **Can we suck that?**
