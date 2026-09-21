# Troubleshooting — English

## Firestorm Display Name controls are greyed out

Clear the Firestorm cache once, close the viewer completely, restart it and reconnect. This is especially relevant for users who connected before the simulator exposed the Display Name CAPS.

## `DisplayNameSimModule` is loaded twice

A correct startup reports one shared module. Check for duplicate module declarations/files, stop the simulator, rename `addin-db-004`, then restart.

## `/get`, `/set` or `/reset` returns 404

Check Apache rewrite support, `.htaccess` permissions and the `ServiceBase` URL. `ServiceBase` must point to the directory containing the supplied `.htaccess` and `index.php`.

## POST fails after a redirect

Use the final HTTPS URL directly. Avoid 301/302 redirects for POST. If a redirect is unavoidable, use a method-preserving 307/308 redirect.

## Hypergrid friends appear as `Unknown`

The module must not fabricate a local Display Name record for UUIDs unknown to the local backend. The supplied code places unresolved IDs in `bad_ids`, allowing the normal legacy/Hypergrid lookup to resolve them.

## The database changes but Firestorm reports failure

The CAPS reply must use an integer status (`200` on success). The supplied code implements this.

## Reset changes the backend but viewers do not refresh

The validated code sends `DisplayNameUpdate` through the EventQueue and creates a separate event for every avatar recipient. The supplied source implements the live broadcast.

## Many backend 404 messages for remote UUIDs

Remote/Hypergrid UUIDs may legitimately be unknown to the local Display Names backend. The supplied code treats them as unresolved IDs instead of turning them into local `Unknown` records.
