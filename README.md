# 🌐 OpenSimulator Display Names

**🇬🇧 English** · [🇫🇷 Français](README_FR.md)

**OpenSimulator Display Names** is an unofficial community addon that adds **Firestorm-compatible Display Name support** to OpenSimulator 0.9.3.x.

The repository contains:

- 🧩 the OpenSimulator addon source;
- 📦 a precompiled build validated with **OpenSimulator 0.9.3.1**;
- 🌐 the PHP/MySQL web service;
- 🗄️ the SQL database schema;
- 📘 English and French installation guides;
- 🛠️ troubleshooting documentation;
- 🧪 a separate technical development history.

> The supplied precompiled DLL is specifically validated with **OpenSimulator 0.9.3.1**.  
> For another 0.9.3.x revision, compile the module against the exact OpenSimulator source revision used by your grid.

---

## 🚀 Quick start

### ✅ You use OpenSimulator 0.9.3.1

Use the ready-to-install files from:

```text
precompiled/OpenSimulator-0.9.3.1/
```

then follow:

➡️ [Full English installation guide](docs/INSTALL_EN.md)

### 🛠️ You use another OpenSimulator 0.9.3.x revision

Compile:

```text
source/addon-modules/DisplayNameSimModule/
```

against your exact OpenSimulator source tree.

➡️ [Installation and compilation guide](docs/INSTALL_EN.md)

---

## 📂 Repository contents

```text
source/
    Compilable OpenSimulator addon source

precompiled/OpenSimulator-0.9.3.1/
    Precompiled DLL, PDB and Mono.Addins manifest

web/displayname/
    PHP web service

database/displaynames.sql
    Display Names database structure

docs/
    Installation, troubleshooting and technical history
```

---

## ⚙️ OpenSimulator configuration

The module uses this section in `OpenSim.ini`:

```ini
[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

📌 **Where should this block go?**

Add it as a **new independent section**, preferably **at the very end of `OpenSim.ini`**, after your existing configuration.

Do not place `ServiceBase`, `HttpTimeoutMs`, and the other settings inside `[Startup]`, `[Network]`, `[Modules]` or another section.

In the `GridCommon.ini` used by the simulator, find the existing `[Modules]` section and add:

```ini
SharedRegionModules = "DisplayNameCapsModule"
```

⚠️ Do not remove any other existing configuration.

➡️ Exact paths, examples and validation checks are documented in [INSTALL_EN.md](docs/INSTALL_EN.md).

---

## ✨ Validated behavior

The build validated with OpenSimulator 0.9.3.1 supports:

- ✅ Display Name lookup;
- ✅ Display Name changes;
- ✅ reset to the legacy name;
- ✅ legacy username display;
- ✅ immediate Firestorm refresh;
- ✅ live updates for nearby viewers;
- ✅ Firestorm profile display;
- ✅ Hypergrid fallback without turning remote contacts into `Unknown`.

After first enabling Display Names, Firestorm may require **one cache clear** followed by a full viewer restart.

---

## 🔐 Security

The supplied backend can accept calls from remotely hosted simulators when:

```php
'allow_all_ips' => true,
```

Read [SECURITY.md](SECURITY.md) before deploying it publicly.

---

## 🧪 Technical history

The main README intentionally stays simple.

The project origin, module reconstruction, OpenSimulator / Firestorm / OSgrid references and technical validation are documented separately in:

➡️ [docs/DEVELOPMENT_HISTORY.md](docs/DEVELOPMENT_HISTORY.md)

---

## 📜 License

This project is released under **The Unlicense**.

➡️ [LICENSE](LICENSE)
