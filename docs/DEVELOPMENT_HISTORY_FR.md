# 🧪 Historique du développement

Ce document décrit l'origine technique de **OpenSimulator Display Names** ainsi que les principales étapes ayant conduit à l'addon actuel disposant de ses propres sources.

Les README principaux restent volontairement simples. Cet historique est séparé afin de conserver les détails techniques sans alourdir la documentation d'installation.

---

## 1. Point de départ : un module compilé pour OSgrid

Le projet est initialement parti d'un **module Display Name fourni sous forme de DLL compilée pour OSgrid**.

Cette DLL n'était pas directement réutilisable sur une autre grille car elle dépendait de **services et d'endpoints propres à OSgrid**.

À ce stade, l'objectif n'était pas encore de reconstruire les sources. La première étape consistait à comprendre le fonctionnement de cette DLL et à la rendre utilisable en dehors d'OSgrid.

---

## 2. Adaptation de la DLL compilée

La DLL compilée d'origine a été analysée puis modifiée afin qu'elle puisse communiquer avec un service indépendant au lieu de l'infrastructure OSgrid.

Ce travail a notamment consisté à adapter l'endpoint utilisé par le module et à vérifier le comportement attendu par Firestorm.

Plusieurs versions intermédiaires de cette DLL modifiée ont été nécessaires avant d'obtenir une base réellement stable.

---

## 3. Création d'un backend PHP/MySQL indépendant

Comme le module d'origine dépendait des services OSgrid, il a fallu créer notre propre backend.

Un service PHP/MySQL dédié a donc été développé pour fournir les opérations nécessaires aux Display Names, notamment :

- lecture du Display Name ;
- changement du Display Name ;
- reset vers le nom legacy ;
- gestion du délai entre deux changements ;
- historique des modifications ;
- vérification contre les noms legacy des avatars locaux.

Le backend a été développé et validé avant de commencer la reconstruction du module source.

---

## 4. La DLL modifiée devenue référence fonctionnelle

Après plusieurs évolutions, la DLL modifiée est devenue pleinement fonctionnelle sur l'environnement de test BloodMoon.

Le comportement validé comprenait notamment :

- affichage du Display Name dans Firestorm ;
- affichage du username / nom legacy avec le Display Name ;
- intégration dans le profil ;
- changement du Display Name ;
- reset vers le nom legacy ;
- rafraîchissement immédiat ;
- mise à jour visible en direct par les autres avatars présents.

Cette version a ensuite servi de **référence comportementale v4** pendant la reconstruction du module source.

Elle n'est pas redistribuée comme source du projet final.

---

## 5. Reconstruction d'un véritable addon OpenSimulator

Ce n'est qu'une fois le backend et la DLL modifiée totalement fonctionnels que l'étape suivante a commencé :

> reconstruire cette fonctionnalité sous la forme d'un véritable addon OpenSimulator disposant de sources compilables.

Le module source a été créé pour être placé sous :

```text
OpenSim/addon-modules/DisplayNameSimModule/
```

puis compilé avec OpenSimulator.

L'objectif était de ne plus dépendre d'une DLL patchée et de disposer d'un module portable et recompilable pour d'autres grilles.

---

## 6. Références techniques utilisées pendant la reconstruction

La reconstruction ne s'est pas appuyée uniquement sur la DLL modifiée.

Plusieurs références ont été utilisées afin de comprendre et reproduire correctement le comportement attendu :

- documentation des addons/modules OpenSimulator ;
- exemple `BlueWall/ExampleSharedRegionModule` ;
- API des region modules OpenSimulator ;
- comportement des Display Names dans Firestorm ;
- structure des CAPS et des réponses LLSD ;
- traitement de `SetDisplayNameReply` côté viewer ;
- traitement de `DisplayNameUpdate` côté viewer ;
- comportement du fallback de résolution des noms Hypergrid.

La DLL v4 modifiée a servi de **référence comportementale**, tandis que le code OpenSimulator et le code viewer ont permis de reconstruire une implémentation source propre.

---

## 7. Principaux problèmes rencontrés pendant la reconstruction

Plusieurs différences entre le nouveau module source et le comportement validé de la v4 ont été identifiées puis corrigées.

### Chargement du module en double

Le module était initialement déclaré deux fois : une fois dans le code C# et une fois dans le manifeste Mono.Addins.

Cela provoquait :

```text
loaded 2 modules, 2 shared
```

La déclaration en double a été supprimée afin que le module ne soit chargé qu'une seule fois.

### Parsing des dates

Une première version du source combinait des valeurs `DateTimeStyles` incompatibles.

Cela a été corrigé afin d'interpréter correctement les dates renvoyées par le backend.

### Utilisateurs inconnus / Hypergrid

Les UUID Hypergrid ne sont pas nécessairement connus par le backend Display Names local.

Créer malgré tout une fiche locale pour ces UUID pouvait transformer les contacts Hypergrid en :

```text
Unknown
```

La version finale utilise `bad_ids` afin de permettre au viewer de revenir au mécanisme normal de résolution des noms OpenSimulator/Hypergrid.

### Réponse SET attendue par Firestorm

Le viewer attend un statut numérique de type HTTP, par exemple :

```text
200
```

pour considérer le changement comme réussi.

Le module source a été corrigé afin de reproduire correctement ce comportement.

### Fonctionnement du reset

Firestorm peut effectuer un reset en envoyant un Display Name vide via l'opération Set.

La version finale accepte ce comportement et le backend interprète un nom vide comme un reset.

Un endpoint de reset dédié reste disponible pour compatibilité.

### Rafraîchissement en direct

La DLL v4 fonctionnelle mettait immédiatement à jour le Display Name pour l'avatar concerné et pour les autres avatars présents.

Le module source a donc été corrigé afin d'envoyer correctement `DisplayNameUpdate` à chaque avatar concerné.

---

## 8. Validation finale

Le module source a été testé avec OpenSimulator 0.9.3.1 et Firestorm.

Les comportements suivants ont été validés :

- lecture du Display Name ;
- changement du Display Name ;
- reset vers le nom legacy ;
- affichage du username / nom legacy ;
- affichage dans le profil ;
- rafraîchissement immédiat après changement ;
- rafraîchissement immédiat après reset ;
- mise à jour visible en direct par un second avatar connecté ;
- conservation correcte des noms des contacts Hypergrid ;
- aucune URL BloodMoon codée en dur ;
- `ServiceBase` lu depuis `OpenSim.ini`.

La version précompilée 0.9.3.1 fournie dans ce dépôt provient de ce source validé.

---

## 9. Objectif du projet

L'objectif de ce dépôt est de fournir une implémentation Display Names réutilisable, compréhensible et compilable pour les grilles OpenSimulator.

Le dépôt contient donc :

```text
source/        addon OpenSimulator compilable
precompiled/   version prête à l'emploi pour OpenSimulator 0.9.3.1
web/           backend PHP
database/      structure SQL
docs/          documentation d'installation et historique technique
```

La DLL compilée OSgrid d'origine **n'est pas incluse** dans ce dépôt.

Elle a uniquement servi de point de départ technique avant la création du backend indépendant puis du véritable addon disposant de ses propres sources.
