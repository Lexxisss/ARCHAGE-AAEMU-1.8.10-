-- WARNING: run this rollback only after confirming every quests.data value is <= 255 bytes.
-- Otherwise MySQL can reject the ALTER or truncate active quest state.
ALTER TABLE `quests`
    MODIFY COLUMN `data` TINYBLOB NOT NULL;
