# 📘 Complete Installation — English

This guide is intended for a first installation and explains precisely **where each file goes** and **where each configuration block must be added**.

The supplied precompiled build has been validated with **OpenSimulator 0.9.3.1**.

---

## 📌 1. Before you start

You need:

- OpenSimulator **0.9.3.1** to use the supplied precompiled DLL directly;
- or the exact OpenSimulator **0.9.3.x** source tree used by your grid if you want to compile the addon;
- an HTTPS web server;
- PHP with PDO MySQL;
- MySQL;
- read access to the Robust `UserAccounts` table.

The reference deployment was validated with Apache 2.4, PHP 8.3 and MySQL 8.0.

### Two separate components are used

The system contains:

```text
1. One web service + one database
2. One module installed on each simulator where Display Names are enabled
```

The web service and database are shared by the whole grid.

The module must be installed on each simulator where you want Display Names enabled.

---

# 🗄️ 2. Create the Display Names database

You may choose any database name.

Example:

```sql
CREATE DATABASE opensim_displaynames
CHARACTER SET utf8mb4
COLLATE utf8mb4_general_ci;
```

Select that database and import:

```text
database/displaynames.sql
```

The script creates two tables:

```text
display_names
display_name_history
```

The SQL file does not force a database name.

---

# 🔐 3. Create a dedicated MySQL user

Using `root` is not recommended.

Example:

```sql
CREATE USER 'displaynames_user'@'localhost'
IDENTIFIED BY 'CHANGE_ME_STRONG_PASSWORD';

GRANT SELECT ON `robust`.`UserAccounts`
TO 'displaynames_user'@'localhost';

GRANT SELECT, INSERT, UPDATE, DELETE
ON `opensim_displaynames`.*
TO 'displaynames_user'@'localhost';

FLUSH PRIVILEGES;
```

Adapt these values to your installation:

```text
robust
opensim_displaynames
displaynames_user
password
MySQL host
```

The account needs:

- `SELECT` on `UserAccounts` in the Robust database;
- `SELECT`, `INSERT`, `UPDATE` and `DELETE` on the Display Names database.

---

# 🌐 4. Install the web service

Copy:

```text
web/displayname/
```

to your web server.

Linux example:

```text
/var/www/html/displayname/
```

Your public URL may then be:

```text
https://grid.example/displayname
```

### Backend configuration

Copy:

```text
web/displayname/config/config.php.example
```

to:

```text
web/displayname/config/config.php
```

Then enter your values:

```php
'db_host' => '127.0.0.1',
'db_port' => 3306,
'db_user' => 'displaynames_user',
'db_pass' => 'YOUR_PASSWORD',
'db_displaynames' => 'opensim_displaynames',
'db_robust' => 'robust',
```

You may use any Display Names database name. Just use the same name in:

```php
'db_displaynames'
```

### Delay between changes

```php
'change_delay_days' => 0,
```

Examples:

```text
0 = no delay
7 = one change every 7 days
```

### Remote simulators / dynamic IPs

The supplied backend supports:

```php
'allow_all_ips' => true,
```

This allows remotely hosted simulators or simulators whose public IP may change.

Read `SECURITY.md` before exposing the service publicly.

### Logs

If:

```php
'debug' => true,
```

the web server must be able to write to:

```text
web/displayname/logs/
```

---

# 🧪 5. Test the web service BEFORE OpenSimulator

Use the UUID of a **local** avatar from your grid.

Test:

```bash
curl -sS "https://grid.example/displayname/get?id=LOCAL-AVATAR-UUID"
```

You must receive a JSON response.

⚠️ **Do not continue with the OpenSimulator module until this test works.**

The `ServiceBase` used later in OpenSimulator must point to this exact service:

```text
https://grid.example/displayname
```

Do not append `/get`, `/set`, `/reset` or `/index.php` to `ServiceBase`.

---

# 📦 6A. Precompiled installation — OpenSimulator 0.9.3.1

If you use **OpenSimulator 0.9.3.1**, stop the simulator first.

Inside:

```text
precompiled/OpenSimulator-0.9.3.1/
```

you will find:

```text
DisplayNameSimModule.dll
DisplayNameSimModule.pdb
DisplayNameSimModule.addin.xml
```

Copy all three files into the simulator's `bin` directory.

Examples:

```text
/home/opensim/OpenSim/bin/
```

or:

```text
C:\OpenSim\bin\
```

⚠️ If an older version is already installed, back up the existing files before replacing them.

---

# 🛠️ 6B. Compile from source — OpenSimulator 0.9.3.x

For another 0.9.3.x revision, compile the addon against the exact OpenSimulator sources used by your grid.

Copy:

```text
source/addon-modules/DisplayNameSimModule
```

to:

```text
OpenSim/addon-modules/DisplayNameSimModule
```

You should end up with a structure similar to:

```text
OpenSim/
├── addon-modules/
│   └── DisplayNameSimModule/
├── bin/
├── OpenSim/
├── Robust/
├── runprebuild.bat
└── OpenSim.sln
```

On Windows, from the root of the OpenSimulator source tree:

```bat
runprebuild.bat
```

then:

```bat
dotnet build OpenSim.sln --configuration Release
```

The compiled module will be placed in:

```text
OpenSim/bin/
```

### Reproducible build without a personal Windows path

Example:

```bat
dotnet build OpenSim.sln --configuration Release --no-incremental -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:PathMap="C:\OpenSim=/_/OpenSim"
```

Replace:

```text
C:\OpenSim
```

with the actual path to your source tree.

---

# ⚙️ 7. Configure `OpenSim.ini`

This is the most important simulator-side configuration step.

Open the `OpenSim.ini` used by the simulator.

Example:

```text
OpenSim/bin/OpenSim.ini
```

## 📍 Where should the block go?

Add the Display Names block as a **new independent section**.

👉 To avoid confusion, the easiest option is to place it **at the very end of `OpenSim.ini`**, after your existing configuration.

Add exactly:

```ini
; ============================================================
; DISPLAY NAMES
; ============================================================

[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

### Very simple example

Before:

```ini
[SomeExistingSection]
SomeSetting = true
```

After:

```ini
[SomeExistingSection]
SomeSetting = true


; ============================================================
; DISPLAY NAMES
; ============================================================

[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

⚠️ Do not put:

```text
ServiceBase
HttpTimeoutMs
CacheTtlMinutes
MaxConcurrentServiceCalls
Verbose
```

inside another section such as:

```text
[Startup]
[Network]
[Modules]
[ClientStack.LindenUDP]
```

They must be located under:

```ini
[DisplayNameCaps]
```

## ✏️ What needs to be changed?

For most installations, only one line needs to be adapted:

```ini
ServiceBase = "https://grid.example/displayname"
```

Example:

```ini
ServiceBase = "https://mygrid.example/displayname"
```

The other values can normally remain:

```ini
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

⚠️ Use the final HTTPS URL directly.

Avoid relying on an HTTP 301/302 redirect for POST requests.

---

# 🧩 8. Enable the module in `GridCommon.ini`

Open the `GridCommon.ini` actually used by your simulator.

In a typical installation it is often located at:

```text
OpenSim/bin/config-include/GridCommon.ini
```

Find the **existing** section:

```ini
[Modules]
```

Add this line inside that section:

```ini
SharedRegionModules = "DisplayNameCapsModule"
```

Example:

```ini
[Modules]

AssetCaching = "FlotsamAssetCache"

Include-FlotsamCache = "config-include/FlotsamCache.ini"

SharedRegionModules = "DisplayNameCapsModule"
```

⚠️ **Do not replace the whole `[Modules]` section.**

⚠️ **Do not delete any existing lines.**

⚠️ If your file already contains a `SharedRegionModules = ...` line, do not create a second duplicate setting. Preserve your existing modules and add `DisplayNameCapsModule` to the existing configuration using the same format already used by your installation.

---

# 🔄 9. Rebuild the Mono.Addins cache

The simulator must be **fully stopped**.

Inside its `bin` directory you may have:

```text
addin-db-004
```

It is safer to rename it instead of deleting it immediately.

Linux:

```bash
mv addin-db-004 addin-db-004.backup
```

Windows: rename:

```text
addin-db-004
```

to:

```text
addin-db-004.backup
```

Then restart the simulator.

OpenSimulator will automatically recreate:

```text
addin-db-004
```

---

# 🔍 10. Verify that the module loaded

In the console or `OpenSim.log`, look for:

```text
Plugin Loaded: DisplayNameSimModule
```

You should also see something similar to:

```text
From plugin DisplayNameSimModule, (version 1.0), loaded 1 modules, 1 shared, 0 non-shared 0 unknown
```

then:

```text
[DisplayNameCaps]: MODULE LOADED. ServiceBase=https://grid.example/displayname ...
```

and:

```text
[DisplayNameCaps]: Registered /CAPS/agents and /CAPS/agents/
```

### ✅ Expected

```text
loaded 1 modules, 1 shared
```

### ❌ Not expected

```text
loaded 2 modules, 2 shared
```

If the addon is loaded twice or an error appears, read:

```text
docs/TROUBLESHOOTING.md
```

---

# 🔥 11. First Firestorm connection

If Firestorm had already connected to your grid **before Display Names were enabled**:

1. clear the Firestorm cache once;
2. completely close the viewer;
3. start Firestorm again;
4. reconnect.

This is normally only needed after first enabling the system.

---

# ✅ 12. Recommended tests

Once connected, verify:

- ✅ the Display Name is visible above the avatar;
- ✅ the username / legacy name remains available;
- ✅ the Display Name appears in the profile;
- ✅ changing the Display Name refreshes immediately;
- ✅ Reset immediately restores the legacy name;
- ✅ another nearby avatar sees the update live;
- ✅ Hypergrid contacts keep their real names;
- ✅ Hypergrid contacts do not become `Unknown`.

---

# 🌍 13. Multiple simulators

If your grid uses multiple independent simulators, install the module on **every simulator where you want Display Names enabled**.

Each affected simulator must have:

```text
DisplayNameSimModule.dll
DisplayNameSimModule.pdb
DisplayNameSimModule.addin.xml
```

inside its `bin` directory.

Each affected simulator must also use:

```ini
[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

The web service and database remain shared by the entire grid.

If several simulators share the same `GridCommon.ini`, the `[Modules]` activation only needs to be added once to that shared file.

---

# 🧯 14. If something goes wrong

Read:

➡️ [TROUBLESHOOTING.md](TROUBLESHOOTING.md)

For the technical origin of the project and its validation history:

➡️ [DEVELOPMENT_HISTORY.md](DEVELOPMENT_HISTORY.md)
