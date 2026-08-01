-- A battle pet's gear lives in its own container, which the server names EquipmentMateBattle.
-- The column never had that value: MySQL truncated it, the insert failed and the whole character
-- save rolled back, so a player carrying anything in a battle pet's slots stopped being saved.
-- None is added for the same reason - it is a real value on the server side.
-- Each statement is kept on one line: editors that wrap a long ENUM list break it apart.

ALTER TABLE `items` MODIFY COLUMN `slot_type` ENUM('None','Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate','EquipmentMateBattle') NOT NULL COMMENT 'Internal Container Type';

ALTER TABLE `item_containers` MODIFY COLUMN `slot_type` ENUM('None','Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate','EquipmentMateBattle') NOT NULL COMMENT 'Internal Container Type';
