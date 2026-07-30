-- Quest runtime QST4 persists independent progress for every quest_acts.id.
-- TINYBLOB is limited to 255 bytes and is not large enough for long quests.
ALTER TABLE `quests`
    MODIFY COLUMN `data` MEDIUMBLOB NOT NULL;
