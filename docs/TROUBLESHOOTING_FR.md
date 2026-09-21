# Dépannage — Français

## Les contrôles Display Name sont grisés dans Firestorm

Videz une fois le cache Firestorm, fermez complètement le viewer, redémarrez-le puis reconnectez-vous. C'est particulièrement utile pour les utilisateurs qui s'étaient déjà connectés avant l'ajout des CAPS Display Name.

## `DisplayNameSimModule` est chargé deux fois

Un démarrage correct doit indiquer un seul module partagé. Vérifiez qu'il n'existe pas de déclaration/fichier en double, arrêtez le simulateur, renommez `addin-db-004` puis redémarrez.

## `/get`, `/set` ou `/reset` renvoie 404

Vérifiez la réécriture Apache, les permissions `.htaccess` et l'URL `ServiceBase`. `ServiceBase` doit pointer vers le dossier contenant le `.htaccess` et `index.php` fournis.

## Les POST échouent après une redirection

Utilisez directement l'URL HTTPS finale. Évitez les redirections 301/302 pour les POST. Si une redirection est indispensable, utilisez 307/308 afin de conserver la méthode HTTP.

## Les amis Hypergrid apparaissent en `Unknown`

Le module ne doit pas fabriquer un enregistrement local pour un UUID inconnu du backend local. Le code fourni place les UUID non résolus dans `bad_ids`, ce qui permet au mécanisme legacy/Hypergrid normal de récupérer leur nom.

## La base est modifiée mais Firestorm annonce un échec

La réponse CAPS doit utiliser un statut entier (`200` en cas de succès). Le code fourni le fait.

## Le reset modifie le backend mais les viewers ne se rafraîchissent pas

Le code validé envoie `DisplayNameUpdate` via l'EventQueue et crée un nouvel événement pour chaque avatar destinataire. Le source fourni contient ce broadcast en direct.

## Beaucoup de 404 pour des UUID distants

Les UUID Hypergrid peuvent légitimement être inconnus du backend Display Names local. Le code fourni les traite comme des UUID non résolus au lieu de les transformer en utilisateurs locaux `Unknown`.
