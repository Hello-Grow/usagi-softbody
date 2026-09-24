# Usagi Softbody

Usagi is a Unity physics playground built around a soft-body Usagi. The player is made from connected rigidbody points instead of a standard character controller, so movement, balance, collisions, and impacts are handled through physics.

## AI declaration

AI tools were used during development for debugging and code editing.

## Features

- Physics-based soft-body player movement
- Camera-relative third-person controls
- Procedural balance and leg movement
- Ragdoll
- Interactive collision and touch sounds
- Speed- and hit-frequency-based impact audio
- Moving platforms and rotating objects
- Configurable slow motion

## Requirements

- Unity 6000.0.25f1

## Walkthrough

1. Open the project in Unity 6000.0.25f1.
2. Open one of the scenes in `Assets/Soft Body`:
   - `SampleScene.unity` is the main playground.
3. Press Play.
4. Use `WASD` to move the character.
5. Move the mouse to orbit the camera. Click to lock the cursor again after pressing Escape.
6. Test the character against the platforms, props, and hazards in the scene.
7. Press `R` to activate ragdoll.

## Walkthrough (BUILD)

1. Extract build.zip from releases
2. click on usagi.exe

## Development notes

Most gameplay values are exposed in the Unity Inspector. If the character behaves incorrectly, check the soft-body point assignments, left and right leg assignments, rigidbody settings, colliders, and ground layers before changing the scripts.
This is an experimental playground.
