-- Restore the persistent container table required by ItemManager and character creation.
-- The historical updater records can be present even when this table was never imported.
CREATE TABLE IF NOT EXISTS `item_containers` (
  `container_id` INT UNSIGNED NOT NULL,
  `container_type` VARCHAR(64) COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'ItemContainer'
    COMMENT 'Partial Container Class Name',
  `slot_type` ENUM('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate')
    CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL
    COMMENT 'Internal Container Type',
  `container_size` INT NOT NULL DEFAULT 50 COMMENT 'Maximum Container Size',
  `owner_id` INT UNSIGNED NOT NULL COMMENT 'Owning Character Id',
  `mate_id` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Owning Mate Id',
  PRIMARY KEY (`container_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
