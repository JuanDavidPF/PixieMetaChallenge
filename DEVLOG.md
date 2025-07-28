# DEVLOG.md - Pixie Meta Unity Dev Challenge (Path A)

## 🛰️ Goal

Transform the NetCode Asteroids sample using Unity 6.1 (Entities, NetCode, Burst) by adding AI-controlled ships that intelligently interact with the game environment and scoring system.

---

## ✅ AI Ship Instantiation

* Cloned and set up the original Unity NetCode Asteroids sample.
* Created an AI ship prefab derived from the existing player ship, removing input authority.
* Registered the prefab in the `AsteroidsSpawner` component and set up server-side spawning logic.
* Wrote a system that instantiates AI ships up to a maximum count, driven by parameters in `LevelComponent`.
* Tagged AI ships using an `AIShipCommandData` component to differentiate them from player-controlled ships.

---

## ✅ AI Ship Steering System

* Created `AISteeringSystem` to handle all AI ship movement and behavior.
* Implemented a `LocatePlayerJob` that finds the nearest player ship (excluding AI-tagged entities).
* Computed direction and angle between the AI ship and target to steer and thrust toward the player if they are outside a preferred distance.
* Integrated aiming logic with shooting behavior: AI shoots if the angle between its forward vector and the target is below a configurable threshold.
* When no players are in detection range, fallback logic searches for the nearest asteroid instead.
* Behavior is consistent whether targeting players or asteroids.

---

## ✅ Level Difficulty Integration

* Implemented `LevelDifficultySystem` that updates `LevelComponent` values every tick using `DeltaTime`.
* Dynamically scales gameplay parameters such as:

  * Number of AI ships spawned (`numAsteroids` field repurposed).
  * Bullet cooldown (`bulletRofCooldownTicks`).
  * AI ship detection radius (`relevancyRadius`).
* These values are referenced in `AISteeringSystem` to modulate AI behavior based on game time.

---

## ✅ Scoring System Correction

* AI bullets were contributing to the player score when destroying asteroids.
* Updated `DestroyAsteroidJob` in the `CollisionSystem` to only count bullet hits toward the score if the bullet's `GhostOwner.NetworkId == 0` (local player).
* Pulled in `GhostOwner` component into the bullet chunk data and added filtering logic before incrementing the score counter.
* Ensured bullet-asteroid collisions by AI no longer affect the score, resolving the unintended behavior.

---

## 🧪 Test Results

* AI ships successfully spawn and exhibit behavior aligned with nearby players or asteroids.
* Steering and firing logic behaves as expected under varying difficulty.
* Score correctly tracks only local player bullet kills.
* Game scales AI behavior properly over time based on elapsed level time.
