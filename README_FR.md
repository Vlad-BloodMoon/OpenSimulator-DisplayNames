# 🌐 OpenSimulator Display Names

[🇬🇧 English](README.md) · **🇫🇷 Français**

**OpenSimulator Display Names** est un addon communautaire non officiel qui ajoute la prise en charge des **Display Names compatibles Firestorm** à OpenSimulator 0.9.3.x.

Le dépôt contient :

- 🧩 les sources du module OpenSimulator ;
- 📦 une version précompilée et validée avec **OpenSimulator 0.9.3.1** ;
- 🌐 le service web PHP/MySQL ;
- 🗄️ la structure SQL ;
- 📘 des guides d'installation en anglais et en français ;
- 🛠️ un guide de dépannage ;
- 🧪 un historique technique séparé du README principal.

> La DLL précompilée fournie est spécifiquement validée avec **OpenSimulator 0.9.3.1**.  
> Pour une autre révision 0.9.3.x, il est recommandé de compiler le module avec les sources exactes de votre version OpenSimulator.

---

## 🚀 Installation rapide

### ✅ Vous utilisez OpenSimulator 0.9.3.1

Utilisez directement :

```text
precompiled/OpenSimulator-0.9.3.1/
```

puis suivez le guide complet :

➡️ [Guide d'installation français](docs/INSTALL_FR.md)

### 🛠️ Vous utilisez une autre version OpenSimulator 0.9.3.x

Compilez :

```text
source/addon-modules/DisplayNameSimModule/
```

avec les sources exactes de votre OpenSimulator.

➡️ [Guide d'installation et de compilation](docs/INSTALL_FR.md)

---

## 📂 Contenu du dépôt

```text
source/
    Sources compilables du module OpenSimulator

precompiled/OpenSimulator-0.9.3.1/
    DLL, PDB et manifeste Mono.Addins déjà compilés

web/displayname/
    Service web PHP

database/displaynames.sql
    Structure de la base Display Names

docs/
    Installation, dépannage et historique technique
```

---

## ⚙️ Configuration OpenSimulator

Le module utilise cette section dans `OpenSim.ini` :

```ini
[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

📌 **Où mettre ce bloc ?**

Ajoutez-le comme une **nouvelle section indépendante**, de préférence **tout en bas de `OpenSim.ini`**, après votre configuration existante.

Ne placez pas les paramètres `ServiceBase`, `HttpTimeoutMs`, etc. dans `[Startup]`, `[Network]`, `[Modules]` ou une autre section.

Dans le `GridCommon.ini` utilisé par le simulateur, recherchez la section `[Modules]` déjà existante et ajoutez :

```ini
SharedRegionModules = "DisplayNameCapsModule"
```

⚠️ Ne supprimez aucune autre configuration déjà présente.

➡️ Les emplacements exacts, exemples et contrôles sont détaillés dans [INSTALL_FR.md](docs/INSTALL_FR.md).

---

## ✨ Fonctionnalités validées

La version testée avec OpenSimulator 0.9.3.1 prend en charge :

- ✅ lecture des Display Names ;
- ✅ changement du Display Name ;
- ✅ reset vers le nom legacy ;
- ✅ affichage du username / nom legacy ;
- ✅ mise à jour immédiate dans Firestorm ;
- ✅ mise à jour en direct chez les autres avatars présents ;
- ✅ profils Firestorm ;
- ✅ fallback Hypergrid sans transformer les contacts distants en `Unknown`.

Après une première activation du système, Firestorm peut nécessiter **un seul vidage de cache**, suivi d'un redémarrage complet du viewer.

---

## 🔐 Sécurité

Le backend fourni permet les appels de simulateurs distants lorsque :

```php
'allow_all_ips' => true,
```

Lisez [SECURITY_FR.md](SECURITY_FR.md) avant une mise en production publique.

---

## 🧪 Historique technique

Le README reste volontairement simple.

L'origine du projet, la reconstruction du module, les références OpenSimulator / Firestorm / OSgrid et les validations techniques sont documentées séparément dans :

➡️ [docs/DEVELOPMENT_HISTORY_FR.md](docs/DEVELOPMENT_HISTORY_FR.md)

---

## 📜 Licence

Ce projet est publié sous **The Unlicense**.

➡️ [LICENSE](LICENSE)
