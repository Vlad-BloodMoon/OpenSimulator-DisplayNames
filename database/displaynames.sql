-- OpenSimulator Display Names database schema
-- Import this file into the database name of your choice.

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Base de données : utilisez le nom de votre choix
--

-- --------------------------------------------------------

--
-- Structure de la table `display_names`
--

CREATE TABLE `display_names` (
  `PrincipalID` char(36) COLLATE utf8mb4_general_ci NOT NULL,
  `DisplayName` varchar(127) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `UpdatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `NextChangeAllowed` datetime DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Structure de la table `display_name_history`
--

CREATE TABLE `display_name_history` (
  `ID` bigint UNSIGNED NOT NULL,
  `PrincipalID` char(36) COLLATE utf8mb4_general_ci NOT NULL,
  `OldDisplayName` varchar(127) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `NewDisplayName` varchar(127) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci DEFAULT NULL,
  `Action` enum('set','reset') COLLATE utf8mb4_general_ci NOT NULL,
  `ChangedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `SourceIP` varchar(45) COLLATE utf8mb4_general_ci DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Index pour les tables déchargées
--

--
-- Index pour la table `display_names`
--
ALTER TABLE `display_names`
  ADD PRIMARY KEY (`PrincipalID`),
  ADD KEY `NextChangeAllowed` (`NextChangeAllowed`);

--
-- Index pour la table `display_name_history`
--
ALTER TABLE `display_name_history`
  ADD PRIMARY KEY (`ID`),
  ADD KEY `PrincipalID` (`PrincipalID`),
  ADD KEY `ChangedAt` (`ChangedAt`);

--
-- AUTO_INCREMENT pour les tables déchargées
--

--
-- AUTO_INCREMENT pour la table `display_name_history`
--
ALTER TABLE `display_name_history`
  MODIFY `ID` bigint UNSIGNED NOT NULL AUTO_INCREMENT;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
