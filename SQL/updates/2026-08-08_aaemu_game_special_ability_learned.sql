-- Whether a special ability (Predator 28, Trooper 29) has been learned.
--
-- The client keeps this apart from progression: its learned-ability record is created by one
-- function (dev x2game.dll 0x39CF09F0) whose only caller is the SCSpecialAbilityLearned handler
-- at 0x394E21C0, and the abilityExp array in SCCharacterState does not reach it. So experience
-- cannot stand in for the flag - a form can be learned while its experience is still zero, and
-- the server has to remember which of them were, to tell the client again after a relog.

ALTER TABLE `abilities`
  ADD COLUMN `learned` tinyint(1) NOT NULL DEFAULT 0
  COMMENT 'Special ability has been learned (ids 28/29)';
