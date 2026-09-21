# 🧪 Development History

This document describes the technical origin of **OpenSimulator Display Names** and the main steps that led to the current source-based addon.

The main README files intentionally remain concise. This document exists to preserve the technical history separately.

---

## 1. Starting point: an OSgrid-compiled module

The project initially started from a **compiled Display Name module distributed for OSgrid**.

That binary was not directly reusable on another grid because it was tied to **OSgrid-specific services and endpoints**.

At that stage, the goal was not yet to reconstruct the source code. The first objective was simply to understand how the module behaved and make it usable outside OSgrid.

---

## 2. Adapting the compiled DLL

The original compiled DLL was analyzed and modified so that it could communicate with an independent service instead of the OSgrid infrastructure.

This work included adapting the service endpoint used by the module and validating the behavior expected by Firestorm.

The modified binary went through several iterations before becoming a stable working reference.

---

## 3. Building an independent PHP/MySQL backend

Because the original module depended on OSgrid services, an independent backend had to be created.

A dedicated PHP/MySQL service was developed to provide the required Display Name operations, including:

- Display Name lookup;
- Display Name changes;
- reset to the legacy name;
- cooldown management;
- change history;
- validation against local legacy avatar names.

The backend was developed and validated before the source addon reconstruction began.

---

## 4. The validated modified binary

After several iterations, the modified binary became a fully functional reference implementation on the BloodMoon test environment.

The validated behavior included:

- Display Names visible in Firestorm;
- legacy username shown alongside the Display Name;
- profile integration;
- Display Name change;
- reset to the legacy name;
- immediate live refresh;
- live update visible to other nearby avatars.

This validated binary was later referred to internally as the **v4 reference build**.

It was not redistributed as the final project source.

---

## 5. Reconstructing a real OpenSimulator addon

Only after the backend and modified binary were fully functional did the project move to the next goal:

> rebuild the feature as a proper OpenSimulator addon with compilable source code.

The source addon was created to live under:

```text
OpenSim/addon-modules/DisplayNameSimModule/
```

and to be compiled together with OpenSimulator.

The goal was to remove dependency on a patched binary and make the module portable between grids.

---

## 6. Technical references used during reconstruction

The reconstruction did not rely only on the modified binary.

Several technical references were used to understand and reproduce the expected behavior:

- OpenSimulator addon/module documentation;
- the BlueWall `ExampleSharedRegionModule` example;
- OpenSimulator region-module APIs;
- Firestorm Display Name behavior;
- Display Name CAPS and LLSD structures;
- viewer-side handling of `SetDisplayNameReply`;
- viewer-side handling of `DisplayNameUpdate`;
- Hypergrid name fallback behavior.

The modified v4 binary was used as a **behavioral reference**, while OpenSimulator and viewer code helped reconstruct a clean source implementation.

---

## 7. Main issues discovered during source reconstruction

Several differences between the reconstructed source and the validated v4 behavior were identified and corrected.

### Duplicate module loading

The module was initially declared twice, once in C# and once in the Mono.Addins manifest.

This caused:

```text
loaded 2 modules, 2 shared
```

The duplicate declaration was removed so the module loads exactly once.

### Date parsing

The first source implementation combined incompatible `DateTimeStyles` values.

This was corrected so backend timestamps are parsed safely.

### Unknown / Hypergrid users

Remote Hypergrid UUIDs are not necessarily known by the local Display Name backend.

Returning a fabricated local record caused Hypergrid friends to appear as:

```text
Unknown
```

The final implementation uses `bad_ids` so the viewer can fall back to the normal OpenSimulator/Hypergrid name-resolution path.

### Firestorm SET reply

The viewer expects a numeric HTTP-style status such as:

```text
200
```

for successful Display Name changes.

The reconstructed module was corrected to match that behavior.

### Reset behavior

Firestorm can reset a Display Name by sending an empty Display Name through the Set operation.

The final module supports this behavior and the backend interprets an empty name as a reset.

A dedicated reset endpoint remains available for compatibility.

### Live refresh

The working v4 binary immediately refreshed the Display Name for the requesting avatar and for nearby avatars.

The reconstructed source was updated to send `DisplayNameUpdate` correctly to each relevant avatar.

---

## 8. Final validation

The source-based module was tested with OpenSimulator 0.9.3.1 and Firestorm.

The validated behavior includes:

- Display Name lookup;
- Display Name change;
- reset to the legacy name;
- legacy username display;
- profile display;
- immediate refresh after change;
- immediate refresh after reset;
- live update visible to another connected avatar;
- Hypergrid contacts keeping their real names;
- no hardcoded BloodMoon service URL;
- `ServiceBase` read from `OpenSim.ini`.

The precompiled 0.9.3.1 build included in this repository comes from that validated source.

---

## 9. Project goal

The purpose of this repository is to provide a reusable, understandable and compilable Display Name implementation for OpenSimulator grids.

The repository therefore includes:

```text
source/        compilable OpenSimulator addon
precompiled/   ready-to-use OpenSimulator 0.9.3.1 build
web/           PHP backend
database/      SQL schema
docs/          installation and technical documentation
```

The original OSgrid-compiled binary is **not included** in this repository.

It served only as the initial technical starting point before the independent backend and source addon were created.
