-- Persistent player-placed housing decoration.
-- The server stores furniture in the general doodads table:
-- owner_type = 3 (Housing), house_id = houses.id, attach_point = 0.

CREATE TABLE IF NOT EXISTS `doodads` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `owner_id` int DEFAULT NULL COMMENT 'Character DB Id',
  `owner_type` tinyint unsigned NOT NULL DEFAULT '255',
  `attach_point` int unsigned NOT NULL DEFAULT '0' COMMENT 'Slot this doodad fits in on the owner',
  `template_id` int unsigned NOT NULL,
  `current_phase_id` int unsigned NOT NULL DEFAULT '0',
  `plant_time` datetime NOT NULL,
  `growth_time` datetime NOT NULL,
  `phase_time` datetime NOT NULL,
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `roll` float NOT NULL DEFAULT '0',
  `pitch` float NOT NULL DEFAULT '0',
  `yaw` float NOT NULL DEFAULT '0',
  `scale` float NOT NULL DEFAULT '1',
  `item_id` bigint unsigned NOT NULL DEFAULT '0' COMMENT 'Exact associated item DB Id',
  `house_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Owning house DB Id',
  `parent_doodad` int unsigned NOT NULL DEFAULT '0' COMMENT 'Parent decoration DB Id',
  `item_template_id` int unsigned NOT NULL DEFAULT '0',
  `item_container_id` bigint unsigned NOT NULL DEFAULT '0',
  `data` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_doodads_owner_house` (`owner_type`,`house_id`),
  KEY `idx_doodads_parent` (`parent_doodad`),
  KEY `idx_doodads_item` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Persistent doodads, including housing decoration';
