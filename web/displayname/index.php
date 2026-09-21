<?php

declare(strict_types=1);

/*
 * ============================================================
 * OPENSIMULATOR GRID
 * Display Names backend
 * ============================================================
 *
 * Compatible avec DisplayNameSimModule OSgrid.
 *
 * Endpoints :
 *
 *   GET  /displayname/get?id=<UUID>
 *   POST /displayname/set
 *   POST /displayname/reset
 *
 * Base OpenSimulator :
 *
 *   robust.UserAccounts
 *
 * Base Display Names :
 *
 *   display-names database
 *
 * IMPORTANT :
 *
 * Les appels sont acceptés depuis toutes les adresses IP.
 *
 * Cela permet aux simulateurs de la grille d'être hébergés
 * sur les serveurs de la grille ou directement chez leurs
 * propriétaires avec IP dynamique.
 *
 * ============================================================
 */


/*
 * ------------------------------------------------------------
 * Configuration
 * ------------------------------------------------------------
 */

$config =
    require __DIR__ .
    '/config/config.php';


/*
 * ------------------------------------------------------------
 * Headers
 * ------------------------------------------------------------
 */

header(
    'Content-Type: application/json; charset=utf-8'
);

header(
    'Cache-Control: no-store, no-cache, must-revalidate'
);


/*
 * ------------------------------------------------------------
 * Journal
 * ------------------------------------------------------------
 */

function dnLog(
    string $message
): void {

    global $config;


    if (
        empty(
            $config['debug']
        )
    ) {

        return;
    }


    $line =
        '[' .
        gmdate(
            'Y-m-d H:i:s'
        ) .
        ' UTC] ' .
        $message .
        PHP_EOL;


    @file_put_contents(
        $config['log_file'],
        $line,
        FILE_APPEND |
        LOCK_EX
    );
}


/*
 * ------------------------------------------------------------
 * Réponse JSON
 * ------------------------------------------------------------
 */

function dnResponse(
    array $data,
    int $httpCode = 200
): never {

    http_response_code(
        $httpCode
    );


    echo json_encode(
        $data,
        JSON_UNESCAPED_UNICODE |
        JSON_UNESCAPED_SLASHES
    );


    exit;
}


/*
 * ------------------------------------------------------------
 * Connexion MySQL
 * ------------------------------------------------------------
 */

try {

    $pdo =
        new PDO(

            sprintf(
                'mysql:host=%s;port=%d;charset=utf8mb4',

                $config['db_host'],

                $config['db_port']
            ),

            $config['db_user'],

            $config['db_pass'],

            [
                PDO::ATTR_ERRMODE =>
                    PDO::ERRMODE_EXCEPTION,

                PDO::ATTR_DEFAULT_FETCH_MODE =>
                    PDO::FETCH_ASSOC,

                PDO::ATTR_EMULATE_PREPARES =>
                    false,
            ]
        );

} catch (
    Throwable $e
) {

    dnLog(
        'MYSQL ERROR: ' .
        $e->getMessage()
    );


    dnResponse(
        [
            'status' =>
                'error',

            'reason' =>
                'Database unavailable',
        ],
        500
    );
}


/*
 * ------------------------------------------------------------
 * Validation UUID
 * ------------------------------------------------------------
 */

function validUuid(
    string $uuid
): bool {

    return preg_match(

        '/^[0-9a-fA-F]{8}-' .
        '[0-9a-fA-F]{4}-' .
        '[0-9a-fA-F]{4}-' .
        '[0-9a-fA-F]{4}-' .
        '[0-9a-fA-F]{12}$/',

        $uuid

    ) === 1;
}


/*
 * ------------------------------------------------------------
 * Adresse IP cliente
 * ------------------------------------------------------------
 *
 * L'IP n'est PAS utilisée pour autoriser/refuser.
 *
 * Elle est seulement conservée dans les logs
 * et dans l'historique des changements.
 * ------------------------------------------------------------
 */

function clientIp(): string
{
    return
        $_SERVER['REMOTE_ADDR']
        ?? '';
}


/*
 * ------------------------------------------------------------
 * Autorisation SET / RESET
 * ------------------------------------------------------------
 *
 * Le service accepte les simulateurs provenant de n'importe
 * quelle adresse IP.
 *
 * La fonction existe volontairement afin de pouvoir
 * réintroduire facilement une politique de sécurité plus
 * stricte plus tard sans réécrire le reste du backend.
 * ------------------------------------------------------------
 */

function writeAllowed(): bool
{
    global $config;


    return
        !empty(
            $config['allow_all_ips']
        );
}


/*
 * ------------------------------------------------------------
 * Délai entre deux changements de Display Name
 * ------------------------------------------------------------
 *
 * Configurable dans :
 *
 *   config/config.php
 *
 * Clé :
 *
 *   'change_delay_days' => 0,
 *
 * 0 = aucun délai
 * 1 = 1 jour
 * 7 = 7 jours
 * etc.
 * ------------------------------------------------------------
 */

function changeDelayDays(): int
{
    global $config;


    return max(
        0,
        (int)(
            $config['change_delay_days']
            ?? 0
        )
    );
}


/*
 * ------------------------------------------------------------
 * Lecture du corps POST
 * ------------------------------------------------------------
 */

function requestData(): array
{
    /*
     * application/x-www-form-urlencoded
     *
     * C'est le format attendu du module OSgrid.
     */

    if (
        !empty(
            $_POST
        )
    ) {

        return $_POST;
    }


    $raw =
        file_get_contents(
            'php://input'
        );


    if (!$raw) {

        return [];
    }


    /*
     * Accepte également JSON pour nos tests manuels.
     */

    $json =
        json_decode(
            $raw,
            true
        );


    if (
        is_array(
            $json
        )
    ) {

        return $json;
    }


    /*
     * Dernière possibilité :
     *
     * données URL-encoded directement dans le corps.
     */

    parse_str(
        $raw,
        $form
    );


    return
        is_array(
            $form
        )
        ? $form
        : [];
}


/*
 * ------------------------------------------------------------
 * Compte OpenSimulator
 * ------------------------------------------------------------
 */

function getUserAccount(
    PDO $pdo,
    string $uuid
): ?array {

    global $config;


    $database =
        str_replace(
            '`',
            '',
            $config['db_robust']
        );


    $sql = "
        SELECT
            PrincipalID,
            FirstName,
            LastName

        FROM
            `{$database}`.`UserAccounts`

        WHERE
            PrincipalID = :uuid

        LIMIT 1
    ";


    $stmt =
        $pdo->prepare(
            $sql
        );


    $stmt->execute(
        [
            ':uuid' =>
                $uuid,
        ]
    );


    $row =
        $stmt->fetch();


    return
        $row ?: null;
}


/*
 * ------------------------------------------------------------
 * Protection des noms d'avatars locaux
 * ------------------------------------------------------------
 *
 * Un Display Name ne peut pas reprendre le nom OpenSimulator
 * (FirstName + LastName) d'un AUTRE compte local.
 *
 * Le propre nom d'origine de l'avatar reste évidemment autorisé.
 * ------------------------------------------------------------
 */

function getRobustNameConflict(
    PDO $pdo,
    string $uuid,
    string $displayName
): ?array {

    global $config;


    $database =
        str_replace(
            '`',
            '',
            $config['db_robust']
        );


    $sql = "
        SELECT
            PrincipalID,
            FirstName,
            LastName

        FROM
            `{$database}`.`UserAccounts`

        WHERE
            PrincipalID <> :uuid

            AND LOWER(
                TRIM(
                    CONCAT(
                        FirstName,
                        ' ',
                        LastName
                    )
                )
            ) = LOWER(:display_name)

        LIMIT 1
    ";


    $stmt =
        $pdo->prepare(
            $sql
        );


    $stmt->execute(
        [
            ':uuid' =>
                $uuid,

            ':display_name' =>
                trim($displayName),
        ]
    );


    $row =
        $stmt->fetch();


    return
        $row ?: null;
}


/*
 * ------------------------------------------------------------
 * Display Name enregistré
 * ------------------------------------------------------------
 */

function getDisplayRecord(
    PDO $pdo,
    string $uuid
): ?array {

    global $config;


    $database =
        str_replace(
            '`',
            '',
            $config[
                'db_displaynames'
            ]
        );


    $sql = "
        SELECT
            PrincipalID,
            DisplayName,
            CreatedAt,
            UpdatedAt,
            NextChangeAllowed

        FROM
            `{$database}`.`display_names`

        WHERE
            PrincipalID = :uuid

        LIMIT 1
    ";


    $stmt =
        $pdo->prepare(
            $sql
        );


    $stmt->execute(
        [
            ':uuid' =>
                $uuid,
        ]
    );


    $row =
        $stmt->fetch();


    return
        $row ?: null;
}


/*
 * ------------------------------------------------------------
 * Nom OpenSimulator classique
 * ------------------------------------------------------------
 */

function legacyName(
    array $account
): string {

    return trim(
        $account['FirstName'] .
        ' ' .
        $account['LastName']
    );
}


/*
 * ------------------------------------------------------------
 * Date ISO UTC
 * ------------------------------------------------------------
 */

function isoUtc(
    ?string $mysqlDate
): string {

    /*
     * Pas encore de délai enregistré :
     *
     * on fournit une date située une seconde dans le passé.
     *
     * Le viewer considère donc le premier changement
     * comme immédiatement autorisé.
     */

    if (!$mysqlDate) {

        return gmdate(
            'Y-m-d\TH:i:s\Z',
            time() - 1
        );
    }


    $timestamp =
        strtotime(
            $mysqlDate .
            ' UTC'
        );


    if (
        $timestamp === false
    ) {

        return gmdate(
            'Y-m-d\TH:i:s\Z',
            time() - 1
        );
    }


    return gmdate(
        'Y-m-d\TH:i:s\Z',
        $timestamp
    );
}


/*
 * ------------------------------------------------------------
 * Validation Display Name
 * ------------------------------------------------------------
 */

function validateDisplayName(
    string $name
): ?string {

    $name =
        trim(
            $name
        );


    /*
     * Nom vide interdit pour SET.
     *
     * Pour revenir au nom classique,
     * il faut utiliser RESET.
     */

    if (
        $name === ''
    ) {

        return
            'Display name cannot be empty';
    }


    /*
     * Limite compatible Firestorm / Second Life.
     */

    if (
        mb_strlen(
            $name,
            'UTF-8'
        ) > 31
    ) {

        return
            'Display name is too long';
    }


    /*
     * Interdiction des caractères de contrôle.
     */

    if (
        preg_match(
            '/[\x00-\x1F\x7F]/u',
            $name
        )
    ) {

        return
            'Invalid characters in display name';
    }


    return null;
}


/*
 * ------------------------------------------------------------
 * Action demandée
 * ------------------------------------------------------------
 */

$action =
    trim(
        (string)(
            $_GET['action']
            ?? ''
        )
    );


dnLog(
    sprintf(
        '%s %s action=%s ip=%s',

        $_SERVER[
            'REQUEST_METHOD'
        ]
        ?? '',

        $_SERVER[
            'REQUEST_URI'
        ]
        ?? '',

        $action,

        clientIp()
    )
);


/*
 * ============================================================
 * GET
 * ============================================================
 */

if (
    $action === 'get'
) {

    $uuid =
        trim(
            (string)(
                $_GET['id']
                ?? ''
            )
        );


    /*
     * UUID valide ?
     */

    if (
        !validUuid(
            $uuid
        )
    ) {

        dnResponse(
            [
                'status' =>
                    'error',

                'reason' =>
                    'Invalid UUID',
            ],
            400
        );
    }


    /*
     * Compte local existant ?
     */

    $account =
        getUserAccount(
            $pdo,
            $uuid
        );


    if (!$account) {

        dnResponse(
            [
                'status' =>
                    'error',

                'reason' =>
                    'Unknown avatar',
            ],
            404
        );
    }


    /*
     * Legacy Name :
     *
     * FirstName + LastName
     */

    $legacy =
        legacyName(
            $account
        );


    /*
     * Display Name éventuel.
     */

    $record =
        getDisplayRecord(
            $pdo,
            $uuid
        );


    /*
     * Pas de Display Name enregistré :
     *
     * le nom classique devient automatiquement
     * le nom d'affichage.
     */

    $displayNameSet =
        (
            $record &&
            $record['DisplayName'] !== null &&
            trim(
                $record[
                    'DisplayName'
                ]
            ) !== ''
        );


    $displayName =
        $displayNameSet
        ?
        $record[
            'DisplayName'
        ]
        :
        $legacy;


    /*
     * Réponse destinée au module OSgrid.
     */

    dnResponse(
        [
            'ok' =>
                'true',

            'status' =>
                'ok',

            'display_name_set' =>
                $displayNameSet
                ? 'true'
                : 'false',

            'display_name' =>
                $displayName,

            'legacy_name' =>
                $legacy,

            'next_change_allowed' =>
                (
                    changeDelayDays() > 0
                )
                ?
                isoUtc(
                    $record[
                        'NextChangeAllowed'
                    ]
                    ?? null
                )
                :
                isoUtc(
                    null
                ),
        ]
    );
}


/*
 * ============================================================
 * SET
 * ============================================================
 */

if (
    $action === 'set'
) {

    /*
     * Politique actuelle :
     *
     * toutes les IP sont autorisées.
     */

    if (
        !writeAllowed()
    ) {

        dnLog(
            'SET DENIED IP=' .
            clientIp()
        );


        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'reason' =>
                    'Access denied',
            ],
            403
        );
    }


    /*
     * Données envoyées par le module.
     */

    $data =
        requestData();


    $uuid =
        trim(
            (string)(
                $data['id']
                ?? ''
            )
        );


    $name =
        trim(
            (string)(
                $data['name']
                ?? ''
            )
        );


    dnLog(
        'SET DATA id=' .
        $uuid .
        ' name=' .
        json_encode(
            $name,
            JSON_UNESCAPED_UNICODE |
            JSON_UNESCAPED_SLASHES
        )
    );


    /*
     * UUID valide ?
     */

    if (
        !validUuid(
            $uuid
        )
    ) {

        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'reason' =>
                    'Invalid UUID',
            ],
            400
        );
    }


    /*
     * --------------------------------------------------------
     * RESET demandé par Firestorm
     * --------------------------------------------------------
     *
     * Firestorm n'appelle pas un endpoint /reset lorsqu'on
     * clique sur « Réinitialiser ».
     *
     * Il utilise la capability SetDisplayName avec un nouveau
     * nom vide. Le module relaie donc la requête vers /set
     * avec name="".
     *
     * On interprète ici ce nom vide comme un RESET vers le
     * FirstName + LastName d'origine.
     * --------------------------------------------------------
     */

    if (
        $name === ''
    ) {

        $account =
            getUserAccount(
                $pdo,
                $uuid
            );


        if (!$account) {

            dnResponse(
                [
                    'ok' =>
                        'false',

                    'status' =>
                        'error',

                    'display_name_set' =>
                        'false',

                    'reason' =>
                        'Unknown avatar',
                ],
                404
            );
        }


        $legacy =
            legacyName(
                $account
            );


        dnLog(
            'SET interpreted as RESET id=' .
            $uuid .
            ' legacy=' .
            $legacy
        );


        try {

            $pdo->beginTransaction();


            $record =
                getDisplayRecord(
                    $pdo,
                    $uuid
                );


            $oldName =
                (
                    $record &&
                    !empty(
                        $record['DisplayName']
                    )
                )
                ?
                $record['DisplayName']
                :
                $legacy;


            $database =
                str_replace(
                    '`',
                    '',
                    $config['db_displaynames']
                );


            $days =
                changeDelayDays();


            /*
             * Si aucun délai n'est configuré, on nettoie aussi
             * NextChangeAllowed.
             *
             * Si un délai est configuré, on conserve la date
             * existante afin qu'un RESET ne permette pas de
             * contourner la limitation.
             */

            if (
                $days > 0
            ) {

                $sql = "
                    INSERT INTO
                        `{$database}`.`display_names`
                    (
                        PrincipalID,
                        DisplayName,
                        NextChangeAllowed
                    )

                    VALUES
                    (
                        :uuid,
                        NULL,
                        NULL
                    )

                    ON DUPLICATE KEY UPDATE

                        DisplayName = NULL
                ";

            } else {

                $sql = "
                    INSERT INTO
                        `{$database}`.`display_names`
                    (
                        PrincipalID,
                        DisplayName,
                        NextChangeAllowed
                    )

                    VALUES
                    (
                        :uuid,
                        NULL,
                        NULL
                    )

                    ON DUPLICATE KEY UPDATE

                        DisplayName = NULL,
                        NextChangeAllowed = NULL
                ";
            }


            $stmt =
                $pdo->prepare(
                    $sql
                );


            $stmt->execute(
                [
                    ':uuid' =>
                        $uuid,
                ]
            );


            $sql = "
                INSERT INTO
                    `{$database}`.`display_name_history`
                (
                    PrincipalID,
                    OldDisplayName,
                    NewDisplayName,
                    Action,
                    SourceIP
                )

                VALUES
                (
                    :uuid,
                    :old_name,
                    NULL,
                    'reset',
                    :ip
                )
            ";


            $stmt =
                $pdo->prepare(
                    $sql
                );


            $stmt->execute(
                [
                    ':uuid' =>
                        $uuid,

                    ':old_name' =>
                        $oldName,

                    ':ip' =>
                        clientIp(),
                ]
            );


            $pdo->commit();


            $record =
                getDisplayRecord(
                    $pdo,
                    $uuid
                );


            /*
             * Cette requête arrive via /set : display_name_set
             * doit être "true" pour signaler à la DLL que
             * l'opération SetDisplayName a réussi.
             *
             * display_name_reset indique en plus la nature
             * réelle de l'opération.
             */

            dnResponse(
                [
                    'ok' =>
                        'true',

                    'status' =>
                        'ok',

                    'display_name_set' =>
                        'true',

                    'display_name_reset' =>
                        'true',

                    'display_name' =>
                        $legacy,

                    'legacy_name' =>
                        $legacy,

                    'next_change_allowed' =>
                        (
                            $days > 0
                        )
                        ?
                        isoUtc(
                            $record['NextChangeAllowed']
                            ?? null
                        )
                        :
                        isoUtc(
                            null
                        ),

                    'reason' =>
                        '',
                ]
            );


        } catch (
            Throwable $e
        ) {

            if (
                $pdo->inTransaction()
            ) {

                $pdo->rollBack();
            }


            dnLog(
                'RESET VIA SET ERROR: ' .
                $e->getMessage()
            );


            dnResponse(
                [
                    'ok' =>
                        'false',

                    'status' =>
                        'error',

                    'display_name_set' =>
                        'false',

                    'display_name_reset' =>
                        'false',

                    'reason' =>
                        'Internal error',
                ],
                500
            );
        }
    }


    /*
     * Validation du Display Name.
     */

    $validationError =
        validateDisplayName(
            $name
        );


    if (
        $validationError !== null
    ) {

        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'reason' =>
                    $validationError,
            ],
            400
        );
    }


    /*
     * L'UUID doit correspondre à un véritable
     * compte local.
     */

    $account =
        getUserAccount(
            $pdo,
            $uuid
        );


    if (!$account) {

        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'reason' =>
                    'Unknown avatar',
            ],
            404
        );
    }


    $legacy =
        legacyName(
            $account
        );


    /*
     * --------------------------------------------------------
     * Protection contre l'usurpation d'un nom d'avatar local
     * --------------------------------------------------------
     *
     * On refuse le FirstName + LastName d'un AUTRE compte
     * présent dans robust.UserAccounts.
     *
     * Le propre nom d'origine de l'utilisateur reste autorisé
     * grâce à PrincipalID <> :uuid dans la requête.
     * --------------------------------------------------------
     */

    $nameConflict =
        getRobustNameConflict(
            $pdo,
            $uuid,
            $name
        );


    if (
        $nameConflict
    ) {

        $conflictingLegacy =
            trim(
                $nameConflict['FirstName'] .
                ' ' .
                $nameConflict['LastName']
            );


        dnLog(
            'SET DENIED NAME CONFLICT id=' .
            $uuid .
            ' requested=' .
            $name .
            ' conflict=' .
            $conflictingLegacy
        );


        dnResponse(
            [
                'ok' =>
                    'false',

                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'legacy_name' =>
                    $legacy,

                'reason' =>
                    'This name belongs to another local avatar',
            ],
            409
        );
    }


    try {

        $pdo->beginTransaction();


        /*
         * État actuel.
         */

        $record =
            getDisplayRecord(
                $pdo,
                $uuid
            );


        $days =
            changeDelayDays();


        /*
         * ----------------------------------------------------
         * Vérification du délai de changement
         * ----------------------------------------------------
         */

        if (
            $days > 0 &&
            $record &&
            !empty(
                $record[
                    'NextChangeAllowed'
                ]
            )
        ) {

            $next =
                strtotime(
                    $record[
                        'NextChangeAllowed'
                    ] .
                    ' UTC'
                );


            if (
                $next !== false &&
                $next > time()
            ) {

                $pdo->rollBack();


                dnResponse(
                    [
                        'status' =>
                            'error',

                        'display_name_set' =>
                            'false',

                        'display_name' =>
                            $record[
                                'DisplayName'
                            ]
                            ?: $legacy,

                        'legacy_name' =>
                            $legacy,

                        'next_change_allowed' =>
                            isoUtc(
                                $record[
                                    'NextChangeAllowed'
                                ]
                            ),

                        'reason' =>
                            'Display name change is not allowed yet',
                    ],
                    409
                );
            }
        }


        /*
         * Ancien nom pour l'historique.
         */

        $oldName =
            (
                $record &&
                !empty(
                    $record[
                        'DisplayName'
                    ]
                )
            )
            ?
            $record[
                'DisplayName'
            ]
            :
            $legacy;


        /*
         * ----------------------------------------------------
         * Prochaine date autorisée
         * ----------------------------------------------------
         */

        $nextChange =
            (
                $days > 0
            )
            ?
            gmdate(
                'Y-m-d H:i:s',
                time() +
                (
                    $days *
                    86400
                )
            )
            :
            null;


        /*
         * ----------------------------------------------------
         * Base Display Names
         * ----------------------------------------------------
         */

        $database =
            str_replace(
                '`',
                '',
                $config[
                    'db_displaynames'
                ]
            );


        /*
         * ----------------------------------------------------
         * Enregistrement du nouveau nom
         * ----------------------------------------------------
         */

        $sql = "
            INSERT INTO
                `{$database}`.`display_names`
            (
                PrincipalID,
                DisplayName,
                NextChangeAllowed
            )

            VALUES
            (
                :uuid,
                :display_name,
                :next_change
            )

            ON DUPLICATE KEY UPDATE

                DisplayName =
                    VALUES(DisplayName),

                NextChangeAllowed =
                    VALUES(NextChangeAllowed)
        ";


        $stmt =
            $pdo->prepare(
                $sql
            );


        $stmt->execute(
            [
                ':uuid' =>
                    $uuid,

                ':display_name' =>
                    $name,

                ':next_change' =>
                    $nextChange,
            ]
        );


        /*
         * ----------------------------------------------------
         * Historique
         * ----------------------------------------------------
         */

        $sql = "
            INSERT INTO
                `{$database}`.`display_name_history`
            (
                PrincipalID,
                OldDisplayName,
                NewDisplayName,
                Action,
                SourceIP
            )

            VALUES
            (
                :uuid,
                :old_name,
                :new_name,
                'set',
                :ip
            )
        ";


        $stmt =
            $pdo->prepare(
                $sql
            );


        $stmt->execute(
            [
                ':uuid' =>
                    $uuid,

                ':old_name' =>
                    $oldName,

                ':new_name' =>
                    $name,

                ':ip' =>
                    clientIp(),
            ]
        );


        /*
         * Validation transaction.
         */

        $pdo->commit();


        /*
         * Réponse positive destinée au module.
         */

        dnResponse(
            [
                'ok' =>
                    'true',

                'status' =>
                    'ok',

                'display_name_set' =>
                    'true',

                'display_name' =>
                    $name,

                'legacy_name' =>
                    $legacy,

                'next_change_allowed' =>
                    isoUtc(
                        $nextChange
                    ),

                'reason' =>
                    '',
            ]
        );


    } catch (
        Throwable $e
    ) {

        if (
            $pdo->inTransaction()
        ) {

            $pdo->rollBack();
        }


        dnLog(
            'SET ERROR: ' .
            $e->getMessage()
        );


        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_set' =>
                    'false',

                'reason' =>
                    'Internal error',
            ],
            500
        );
    }
}


/*
 * ============================================================
 * RESET
 * ============================================================
 */

if (
    $action === 'reset'
) {

    /*
     * Toutes les IP sont autorisées.
     */

    if (
        !writeAllowed()
    ) {

        dnLog(
            'RESET DENIED IP=' .
            clientIp()
        );


        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_reset' =>
                    'false',

                'reason' =>
                    'Access denied',
            ],
            403
        );
    }


    /*
     * Données reçues.
     */

    $data =
        requestData();


    $uuid =
        trim(
            (string)(
                $data['id']
                ?? ''
            )
        );


    /*
     * UUID valide ?
     */

    if (
        !validUuid(
            $uuid
        )
    ) {

        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_reset' =>
                    'false',

                'reason' =>
                    'Invalid UUID',
            ],
            400
        );
    }


    /*
     * Compte local ?
     */

    $account =
        getUserAccount(
            $pdo,
            $uuid
        );


    if (!$account) {

        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_reset' =>
                    'false',

                'reason' =>
                    'Unknown avatar',
            ],
            404
        );
    }


    $legacy =
        legacyName(
            $account
        );


    try {

        $pdo->beginTransaction();


        /*
         * État actuel.
         */

        $record =
            getDisplayRecord(
                $pdo,
                $uuid
            );


        /*
         * Ancien nom.
         */

        $oldName =
            (
                $record &&
                !empty(
                    $record[
                        'DisplayName'
                    ]
                )
            )
            ?
            $record[
                'DisplayName'
            ]
            :
            $legacy;


        $database =
            str_replace(
                '`',
                '',
                $config[
                    'db_displaynames'
                ]
            );


        /*
         * ----------------------------------------------------
         * Reset
         * ----------------------------------------------------
         *
         * Le DisplayName repasse à NULL.
         *
         * IMPORTANT :
         *
         * NextChangeAllowed est volontairement conservé
         * lorsqu'une ligne existe déjà.
         *
         * Cela évite :
         *
         * SET
         * RESET
         * SET immédiatement
         *
         * pour contourner le délai de 7 jours.
         * ----------------------------------------------------
         */

        if (
            changeDelayDays() > 0
        ) {

            $sql = "
                INSERT INTO
                    `{$database}`.`display_names`
                (
                    PrincipalID,
                    DisplayName,
                    NextChangeAllowed
                )

                VALUES
                (
                    :uuid,
                    NULL,
                    NULL
                )

                ON DUPLICATE KEY UPDATE

                    DisplayName = NULL
            ";

        } else {

            $sql = "
                INSERT INTO
                    `{$database}`.`display_names`
                (
                    PrincipalID,
                    DisplayName,
                    NextChangeAllowed
                )

                VALUES
                (
                    :uuid,
                    NULL,
                    NULL
                )

                ON DUPLICATE KEY UPDATE

                    DisplayName = NULL,
                    NextChangeAllowed = NULL
            ";
        }


        $stmt =
            $pdo->prepare(
                $sql
            );


        $stmt->execute(
            [
                ':uuid' =>
                    $uuid,
            ]
        );


        /*
         * ----------------------------------------------------
         * Historique du reset
         * ----------------------------------------------------
         */

        $sql = "
            INSERT INTO
                `{$database}`.`display_name_history`
            (
                PrincipalID,
                OldDisplayName,
                NewDisplayName,
                Action,
                SourceIP
            )

            VALUES
            (
                :uuid,
                :old_name,
                NULL,
                'reset',
                :ip
            )
        ";


        $stmt =
            $pdo->prepare(
                $sql
            );


        $stmt->execute(
            [
                ':uuid' =>
                    $uuid,

                ':old_name' =>
                    $oldName,

                ':ip' =>
                    clientIp(),
            ]
        );


        /*
         * Validation.
         */

        $pdo->commit();


        /*
         * Recharge après reset.
         */

        $record =
            getDisplayRecord(
                $pdo,
                $uuid
            );


        /*
         * Réponse.
         */

        dnResponse(
            [
                'ok' =>
                    'true',

                'status' =>
                    'ok',

                'display_name_reset' =>
                    'true',

                'display_name_set' =>
                    'false',

                'display_name' =>
                    $legacy,

                'legacy_name' =>
                    $legacy,

                'next_change_allowed' =>
                    (
                        changeDelayDays() > 0
                    )
                    ?
                    isoUtc(
                        $record[
                            'NextChangeAllowed'
                        ]
                        ?? null
                    )
                    :
                    isoUtc(
                        null
                    ),

                'reason' =>
                    '',
            ]
        );


    } catch (
        Throwable $e
    ) {

        if (
            $pdo->inTransaction()
        ) {

            $pdo->rollBack();
        }


        dnLog(
            'RESET ERROR: ' .
            $e->getMessage()
        );


        dnResponse(
            [
                'status' =>
                    'error',

                'display_name_reset' =>
                    'false',

                'reason' =>
                    'Internal error',
            ],
            500
        );
    }
}


/*
 * ============================================================
 * Route inconnue
 * ============================================================
 */

dnResponse(
    [
        'status' =>
            'error',

        'reason' =>
            'Unknown endpoint',
    ],
    404
);