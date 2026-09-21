# 📘 Installation complète — Français

Ce guide est destiné à une première installation et décrit précisément **où placer chaque fichier** et **où ajouter chaque bloc de configuration**.

La version précompilée fournie a été validée avec **OpenSimulator 0.9.3.1**.

---

## 📌 1. Avant de commencer

Vous aurez besoin de :

- OpenSimulator **0.9.3.1** pour utiliser directement la DLL précompilée ;
- ou des sources exactes de votre OpenSimulator **0.9.3.x** si vous souhaitez compiler le module ;
- un serveur web HTTPS ;
- PHP avec PDO MySQL ;
- MySQL ;
- un accès en lecture à la table `UserAccounts` de la base Robust.

Le déploiement de référence a été validé avec Apache 2.4, PHP 8.3 et MySQL 8.0.

### Deux éléments distincts sont utilisés

Le système comprend :

```text
1. Un service web + une base de données
2. Un module installé sur chaque simulateur concerné
```

Le service web et la base sont communs à toute la grille.

Le module, lui, doit être installé sur chaque simulateur où vous souhaitez activer les Display Names.

---

# 🗄️ 2. Créer la base Display Names

Vous êtes libre de choisir le nom de cette base.

Exemple :

```sql
CREATE DATABASE opensim_displaynames
CHARACTER SET utf8mb4
COLLATE utf8mb4_general_ci;
```

Sélectionnez ensuite cette base et importez :

```text
database/displaynames.sql
```

Le script crée deux tables :

```text
display_names
display_name_history
```

Le fichier SQL n'impose pas le nom de la base.

---

# 🔐 3. Créer un utilisateur MySQL dédié

Il est recommandé de ne pas utiliser `root`.

Exemple :

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

Adaptez à votre installation :

```text
robust
opensim_displaynames
displaynames_user
mot de passe
hôte MySQL
```

Le compte a besoin :

- de `SELECT` sur `UserAccounts` dans la base Robust ;
- de `SELECT`, `INSERT`, `UPDATE` et `DELETE` sur la base Display Names.

---

# 🌐 4. Installer le service web

Copiez :

```text
web/displayname/
```

dans votre serveur web.

Exemple Linux :

```text
/var/www/html/displayname/
```

Votre URL publique pourra alors être :

```text
https://grid.example/displayname
```

### Configuration du backend

Copiez :

```text
web/displayname/config/config.php.example
```

vers :

```text
web/displayname/config/config.php
```

Puis renseignez vos valeurs :

```php
'db_host' => '127.0.0.1',
'db_port' => 3306,
'db_user' => 'displaynames_user',
'db_pass' => 'VOTRE_MOT_DE_PASSE',
'db_displaynames' => 'opensim_displaynames',
'db_robust' => 'robust',
```

Vous pouvez choisir n'importe quel nom de base Display Names : il suffit de reporter ce nom dans :

```php
'db_displaynames'
```

### Délai entre deux changements

```php
'change_delay_days' => 0,
```

Exemples :

```text
0 = aucun délai
7 = un changement tous les 7 jours
```

### Simulateurs distants / IP dynamiques

Le backend fourni permet :

```php
'allow_all_ips' => true,
```

Cela permet d'utiliser le service avec des simulateurs hébergés à distance ou dont l'IP peut changer.

Lisez `SECURITY_FR.md` avant d'exposer le service publiquement.

### Logs

Si :

```php
'debug' => true,
```

le serveur web doit pouvoir écrire dans :

```text
web/displayname/logs/
```

---

# 🧪 5. Tester le service web AVANT OpenSimulator

Prenez l'UUID d'un avatar **local** de votre grille.

Test :

```bash
curl -sS "https://grid.example/displayname/get?id=UUID-DE-L-AVATAR"
```

Vous devez obtenir une réponse JSON.

⚠️ **Ne continuez pas l'installation du module tant que ce test ne fonctionne pas.**

Le `ServiceBase` utilisé ensuite dans OpenSimulator devra pointer vers exactement le même service :

```text
https://grid.example/displayname
```

Ne mettez pas `/get`, `/set`, `/reset` ou `/index.php` à la fin de `ServiceBase`.

---

# 📦 6A. Installation précompilée — OpenSimulator 0.9.3.1

Si vous utilisez **OpenSimulator 0.9.3.1**, arrêtez d'abord le simulateur.

Dans :

```text
precompiled/OpenSimulator-0.9.3.1/
```

vous trouverez :

```text
DisplayNameSimModule.dll
DisplayNameSimModule.pdb
DisplayNameSimModule.addin.xml
```

Copiez ces trois fichiers dans le dossier `bin` du simulateur.

Exemples :

```text
/home/opensim/OpenSim/bin/
```

ou :

```text
C:\OpenSim\bin\
```

⚠️ Si une ancienne version du module est déjà installée, sauvegardez les anciens fichiers avant de les remplacer.

---

# 🛠️ 6B. Compilation depuis les sources — OpenSimulator 0.9.3.x

Pour une autre révision 0.9.3.x, il est préférable de compiler le module avec les sources exactes utilisées par votre grille.

Copiez :

```text
source/addon-modules/DisplayNameSimModule
```

vers :

```text
OpenSim/addon-modules/DisplayNameSimModule
```

Vous devez obtenir une structure similaire à :

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

Sous Windows, depuis la racine des sources OpenSimulator :

```bat
runprebuild.bat
```

puis :

```bat
dotnet build OpenSim.sln --configuration Release
```

Le module compilé sera placé dans :

```text
OpenSim/bin/
```

### Compilation reproductible sans chemin Windows personnel

Exemple :

```bat
dotnet build OpenSim.sln --configuration Release --no-incremental -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:PathMap="C:\OpenSim=/_/OpenSim"
```

Remplacez :

```text
C:\OpenSim
```

par le chemin réel de vos sources.

---

# ⚙️ 7. Configurer `OpenSim.ini`

C'est l'étape la plus importante côté simulateur.

Ouvrez le fichier `OpenSim.ini` du simulateur concerné.

Exemple :

```text
OpenSim/bin/OpenSim.ini
```

## 📍 Où mettre le bloc ?

Ajoutez le bloc Display Names comme une **nouvelle section indépendante**.

👉 Pour éviter toute confusion, le plus simple est de le placer **tout en bas du fichier `OpenSim.ini`**, après votre configuration existante.

Ajoutez exactement :

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

### Exemple très simple

Avant :

```ini
[SomeExistingSection]
SomeSetting = true
```

Après :

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

⚠️ Ne mettez pas les lignes :

```text
ServiceBase
HttpTimeoutMs
CacheTtlMinutes
MaxConcurrentServiceCalls
Verbose
```

dans une autre section comme :

```text
[Startup]
[Network]
[Modules]
[ClientStack.LindenUDP]
```

Elles doivent se trouver sous :

```ini
[DisplayNameCaps]
```

## ✏️ Que faut-il modifier ?

Dans la plupart des installations, une seule ligne doit être adaptée :

```ini
ServiceBase = "https://grid.example/displayname"
```

Exemple :

```ini
ServiceBase = "https://mygrid.example/displayname"
```

Les autres valeurs peuvent normalement rester :

```ini
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

⚠️ Utilisez directement votre URL finale en HTTPS.

Évitez de dépendre d'une redirection HTTP 301/302 pour les requêtes POST.

---

# 🧩 8. Activer le module dans `GridCommon.ini`

Ouvrez le `GridCommon.ini` réellement utilisé par votre simulateur.

Dans une installation classique, il se trouve souvent ici :

```text
OpenSim/bin/config-include/GridCommon.ini
```

Cherchez la section **déjà existante** :

```ini
[Modules]
```

Ajoutez dans cette section :

```ini
SharedRegionModules = "DisplayNameCapsModule"
```

Exemple :

```ini
[Modules]

AssetCaching = "FlotsamAssetCache"

Include-FlotsamCache = "config-include/FlotsamCache.ini"

SharedRegionModules = "DisplayNameCapsModule"
```

⚠️ **Ne remplacez pas toute la section `[Modules]`.**

⚠️ **Ne supprimez aucune ligne déjà présente.**

⚠️ Si votre fichier contient déjà une ligne `SharedRegionModules = ...`, ne créez pas une deuxième ligne identique : conservez vos modules existants et ajoutez `DisplayNameCapsModule` à la configuration existante en respectant le format déjà utilisé par votre installation.

---

# 🔄 9. Reconstruire le cache Mono.Addins

Le simulateur doit être **complètement arrêté**.

Dans son dossier `bin`, vous pouvez avoir :

```text
addin-db-004
```

Il est plus prudent de le renommer plutôt que de le supprimer.

Sous Linux :

```bash
mv addin-db-004 addin-db-004.backup
```

Sous Windows, renommez simplement :

```text
addin-db-004
```

en :

```text
addin-db-004.backup
```

Redémarrez ensuite le simulateur.

OpenSimulator recréera automatiquement un nouveau :

```text
addin-db-004
```

---

# 🔍 10. Vérifier que le module est chargé

Dans la console ou dans `OpenSim.log`, recherchez :

```text
Plugin Loaded: DisplayNameSimModule
```

Vous devez également voir quelque chose de similaire à :

```text
From plugin DisplayNameSimModule, (version 1.0), loaded 1 modules, 1 shared, 0 non-shared 0 unknown
```

puis :

```text
[DisplayNameCaps]: MODULE LOADED. ServiceBase=https://grid.example/displayname ...
```

et :

```text
[DisplayNameCaps]: Registered /CAPS/agents and /CAPS/agents/
```

### ✅ Résultat attendu

```text
loaded 1 modules, 1 shared
```

### ❌ Résultat anormal

```text
loaded 2 modules, 2 shared
```

Si le module est chargé deux fois ou si une erreur apparaît, consultez :

```text
docs/TROUBLESHOOTING_FR.md
```

---

# 🔥 11. Première connexion avec Firestorm

Si Firestorm s'était déjà connecté à votre grille **avant l'activation des Display Names** :

1. videz une fois le cache Firestorm ;
2. fermez complètement le viewer ;
3. relancez Firestorm ;
4. reconnectez-vous.

Cette opération n'est normalement nécessaire qu'après la première activation du système.

---

# ✅ 12. Tests recommandés

Une fois connecté, vérifiez :

- ✅ le Display Name est visible au-dessus de l'avatar ;
- ✅ le username / nom legacy reste disponible ;
- ✅ le Display Name apparaît dans le profil ;
- ✅ un changement de Display Name apparaît immédiatement ;
- ✅ Reset remet immédiatement le nom legacy ;
- ✅ un autre avatar présent voit le changement en direct ;
- ✅ les contacts Hypergrid conservent leur vrai nom ;
- ✅ les contacts Hypergrid ne deviennent pas `Unknown`.

---

# 🌍 13. Plusieurs simulateurs

Si votre grille utilise plusieurs simulateurs indépendants, installez le module sur **chaque simulateur où vous souhaitez activer les Display Names**.

Chaque simulateur concerné doit disposer de :

```text
DisplayNameSimModule.dll
DisplayNameSimModule.pdb
DisplayNameSimModule.addin.xml
```

dans son dossier `bin`.

Chaque simulateur concerné doit aussi utiliser :

```ini
[DisplayNameCaps]
ServiceBase = "https://grid.example/displayname"
HttpTimeoutMs = 5000
CacheTtlMinutes = 30
MaxConcurrentServiceCalls = 5
Verbose = true
```

Le service web et la base de données, eux, restent communs à toute la grille.

Si plusieurs simulateurs partagent le même `GridCommon.ini`, l'activation `[Modules]` n'a bien sûr besoin d'être ajoutée qu'une seule fois dans ce fichier partagé.

---

# 🧯 14. En cas de problème

Consultez :

➡️ [TROUBLESHOOTING_FR.md](TROUBLESHOOTING_FR.md)

Pour comprendre l'origine technique du projet et les validations réalisées :

➡️ [DEVELOPMENT_HISTORY_FR.md](DEVELOPMENT_HISTORY_FR.md)
