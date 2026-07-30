-- ArcheAge skill-system MySQL schema audit (read-only)
-- Date: 2026-07-30
--
-- No MySQL migration is required by this patch. The existing `skills` table
-- already matches the persistence model used by CharacterSkills:
--   id     INT UNSIGNED NOT NULL
--   level  TINYINT NOT NULL
--   type   ENUM('Skill','Buff') NOT NULL
--   owner  INT UNSIGNED NOT NULL
--   PRIMARY KEY (id, owner)
--
-- Run the statements below against the AAEmu game database to verify the
-- deployed schema. They do not alter any data or metadata.

SELECT
    TABLE_SCHEMA,
    TABLE_NAME,
    ENGINE,
    TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'skills';

SELECT
    ORDINAL_POSITION,
    COLUMN_NAME,
    COLUMN_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT,
    EXTRA
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'skills'
ORDER BY ORDINAL_POSITION;

SELECT
    INDEX_NAME,
    NON_UNIQUE,
    SEQ_IN_INDEX,
    COLUMN_NAME,
    INDEX_TYPE
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'skills'
ORDER BY INDEX_NAME, SEQ_IN_INDEX;

-- Expected result summary:
-- 1) Exactly four columns: id, level, type, owner.
-- 2) id and owner are unsigned integers.
-- 3) type accepts Skill and Buff.
-- 4) PRIMARY KEY column order is (id, owner).
