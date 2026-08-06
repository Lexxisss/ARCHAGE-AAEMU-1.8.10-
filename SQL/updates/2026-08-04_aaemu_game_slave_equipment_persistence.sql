-- Persist ship / land-vehicle equipment as normal item instances bound to a slave DB id.
-- Both enum columns must accept the wire/storage container names used by SlotType.

ALTER TABLE `items`
  MODIFY COLUMN `slot_type`
  ENUM(
    'None','Equipment','Inventory','Bank','Trade','Mail','System',
    'EquipmentMate','EquipmentMateBattle',
    'EquipmentSlavePreliminary','EquipmentSlave'
  ) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci
  NOT NULL COMMENT 'Internal Container Type';

ALTER TABLE `item_containers`
  MODIFY COLUMN `slot_type`
  ENUM(
    'None','Equipment','Inventory','Bank','Trade','Mail','System',
    'EquipmentMate','EquipmentMateBattle',
    'EquipmentSlavePreliminary','EquipmentSlave'
  ) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci
  NOT NULL COMMENT 'Internal Container Type';
