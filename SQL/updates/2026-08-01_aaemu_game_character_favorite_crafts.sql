-- Recipes a player has pinned in the crafting window. The client keeps its own copy and expects
-- the server to hold the real one, but nothing stored them before, so the pins vanished on logout.
-- Each statement is kept on one line: editors that wrap long statements break them apart.

CREATE TABLE IF NOT EXISTS `character_favorite_crafts` ( `owner` int unsigned NOT NULL COMMENT 'Owning Character Id', `craft_id` int unsigned NOT NULL COMMENT 'crafts.id', PRIMARY KEY (`owner`,`craft_id`), KEY `idx_character_favorite_crafts_owner` (`owner`) ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
