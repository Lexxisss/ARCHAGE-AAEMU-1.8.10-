using System.Collections.Generic;
using System.Linq;
using System;
using System.Numerics;
using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitStatePacket : GamePacket
{
    private static int _npcDumpCount;
    private readonly Unit _unit;
    private readonly BaseUnitType _baseUnitType;
    private ModelPostureType _modelPostureType;

    public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 5)
    {
        _unit = unit;
        switch (_unit)
        {
            case Character:
                _baseUnitType = BaseUnitType.Character;
                _modelPostureType = ModelPostureType.None;
                break;
            case Npc npc:
                _baseUnitType = BaseUnitType.Npc;
                _modelPostureType = npc.Template.AnimActionId > 0
                    ? ModelPostureType.ActorModelState
                    : ModelPostureType.None;
                break;
            case Slave:
                _baseUnitType = BaseUnitType.Slave;
                _modelPostureType = ModelPostureType.TurretState; // was TurretState = 8
                break;
            case House:
                _baseUnitType = BaseUnitType.Housing;
                _modelPostureType = ModelPostureType.HouseState;
                break;
            case Transfer:
                _baseUnitType = BaseUnitType.Transfer;
                _modelPostureType = ModelPostureType.TurretState;
                break;
            case Mate:
                _baseUnitType = BaseUnitType.Mate;
                _modelPostureType = ModelPostureType.None;
                break;
            case Shipyard:
                _baseUnitType = BaseUnitType.Shipyard;
                _modelPostureType = ModelPostureType.None;
                break;
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        var bodyStart = stream.Count;
        var equipEnd = 0;
        var modelParamsEnd = 0;
        var pointsEnd = 0;
        var postureEnd = 0;
        var skillsEnd = 0;
        var piscEnd = 0;
        var netUnitEnd = 0;

        #region NetUnit
        stream.WriteBc(_unit.ObjId);
        // Target NPC states carry an empty runtime name. The client resolves
        // the localized name from npc.TemplateId; serializing the server-side
        // name shifts BaseUnitType and every following field.
        stream.Write(_unit is Npc ? string.Empty : _unit.Name);

        // Target 10.8.1 NetUnit header (x2game.dll serializer 0x39A069D0).
        // These fields precede BaseUnitType and were absent from the legacy packet.
        if (_unit.Transform.WorldId > byte.MaxValue)
            Logger.Warn($"SCUnitState worldId {_unit.Transform.WorldId} exceeds the target byte field for ObjId {_unit.ObjId}");
        stream.Write((byte)_unit.Transform.WorldId); // worldId
        stream.Write((byte)0);                       // regionId (global-world region, not zoneId)
        stream.Write(false);                         // isInGlobalWorld

        // Cache character & npc
        var character = _unit as Character;
        var npc = _unit as Npc;

        #region BaseUnitType
        stream.Write((byte)_baseUnitType);
        switch (_baseUnitType)
        {
            case BaseUnitType.Character:
                stream.Write((ulong)(character?.Id ?? 0u)); // characterId
                stream.Write(0L);                           // v
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(npc.ObjId);    // objId
                stream.Write(npc.TemplateId); // npc templateId
                stream.Write(0L);              // type(id), uint64 in target
                stream.Write((byte)0);        // clientDriven
                break;
            case BaseUnitType.Slave:
                var slave = (Slave)_unit;
                stream.Write(slave.Id);             // Id ? slave.Id
                stream.Write(slave.TlId);           // tl
                stream.Write(slave.TemplateId);     // templateId
                stream.Write(slave.Summoner?.ObjId ?? 0); // ownerId ? slave.Summoner.ObjId
                break;
            case BaseUnitType.Housing:
                var house = (House)_unit;
                var buildStep = house.CurrentStep == -1
                    ? 0
                    : -house.Template.BuildSteps.Count + house.CurrentStep;

                stream.Write(house.TlId); // tl
                stream.Write(house.TemplateId); // templateId
                stream.Write((short)buildStep); // buildstep
                break;
            case BaseUnitType.Transfer:
                var transfer = (Transfer)_unit;
                stream.Write(transfer.TlId); // tl
                stream.Write(transfer.TemplateId); // templateId
                break;
            case BaseUnitType.Mate:
                var mount = (Mate)_unit;
                stream.Write(mount.TlId);       // tl
                stream.Write(mount.TemplateId); // teplateId
                stream.Write(mount.OwnerId);    // characterId (masterId)
                break;
            case BaseUnitType.Shipyard:
                var shipyard = (Shipyard)_unit;
                stream.Write(shipyard.ShipyardData.Id);         // type(id)
                stream.Write(shipyard.ShipyardData.TemplateId); // type(id)
                break;
        }
        #endregion BaseUnitType

        if (_unit.OwnerId > 0) // master
        {
            var name = NameManager.Instance.GetCharacterName(_unit.OwnerId);
            stream.Write(name ?? "");
        }
        else
            stream.Write("");

        WriteProtocol1810Position(stream, _unit.Transform.Local.Position);
        stream.Write(_unit.Scale); // scale
        stream.Write(_unit.Level); // level
        stream.Write(npc == null ? (byte)0 : checked((byte)npc.Template.HeirLevel)); // heirLevel
        stream.Write((byte)0);     // visual/override level
        stream.Write((byte)0);     // visual/override heirLevel
        for (var i = 0; i < 4; i++)
            stream.Write((sbyte)-1); // target level-effect slots

        stream.Write(_unit.ModelId); // modelRef

        #region CharacterInfo_3EB0

        //Inventory_Equip1(stream, _unit); // Equip character
        //Inventory_Equip2(stream, _unit, _baseUnitType); // Equip character
        //Inventory_Equip0(stream, _unit); // Equip character
        Inventory_Equip3(stream, _unit); // Equip character
        equipEnd = stream.Count;

        #endregion CharacterInfo_3EB0

        var modelParams = _unit.ModelParams ?? new UnitCustomModelParams(UnitCustomModelType.None);
        if (npc != null && npc.ModelId is 10 or 11 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 24 or 25)
        {
            // Humanoid NPCs need the face modifier and pupil colours from
            // total_character_customs. Sending their skin/body normal-map ids as
            // a live override makes this client select placeholder TEST/black
            // materials, so keep those two material overrides neutral. Build a
            // packet-local copy: never mutate the NPC template or player data.
            modelParams = modelParams.Face == null
                ? new UnitCustomModelParams(UnitCustomModelType.None)
                : new UnitCustomModelParams(UnitCustomModelType.Face)
                    .SetId(modelParams.Id)
                    .SetHairColorId(modelParams.HairColorId)
                    .SetHornColorId(modelParams.HornColorId)
                    .SetSkinColorId(0)
                    .SetModelId(modelParams.ModelId)
                    .SetDefaultHairColor(modelParams.DefaultHairColor)
                    .SetTwoToneHair(modelParams.TwoToneHair)
                    .SetTwoToneFirstWidth(modelParams.TwoToneFirstWidth)
                    .SetTwoToneSecondWidth(modelParams.TwoToneSecondWidth)
                    .SetBodyNormalMapId(0)
                    .SetBodyNormalMapWeight(0f)
                    .SetFace(modelParams.Face);
        }

        stream.Write(modelParams);
        modelParamsEnd = stream.Count;

        stream.WriteBc(0);
        stream.Write((long)_unit.Hp * 100L); // preciseHealth, int64 in target
        stream.Write((long)_unit.Mp * 100L); // preciseMana, int64 in target
        pointsEnd = stream.Count;

        #region AttachPoint1
        switch (_unit)
        {
            case Gimmick:
            case Portal:
            case Character:
            case Npc:
            case House:
            case Mate:
            case Shipyard:
                stream.Write((byte)AttachPointKind.System);   // point
                break;
            case Slave unit:
                stream.Write(unit.AttachPointId);
                if (unit.AttachPointId > -1)
                    stream.WriteBc(unit.OwnerObjId);
                break;
            case Transfer unit:
                if (unit.BondingObjId != 0)
                {
                    stream.Write((byte)unit.AttachPointId);  // point
                    stream.WriteBc(unit.BondingObjId); // point to the owner where to attach
                }
                else
                    stream.Write((byte)AttachPointKind.System);   // point
                break;
        }
        #endregion AttachPoint1

        #region AttachPoint2
        switch (_unit)
        {
            case Character:
                switch (character.Bonding)
                {
                    case null:
                        stream.Write((byte)AttachPointKind.System);   // point
                        break;
                    default:
                        stream.Write(character.Bonding);
                        break;
                }
                break;
            case Npc:
            case House:
            case Mate:
            case Shipyard:
            case Transfer:
                stream.Write((byte)AttachPointKind.System);   // point
                break;
            case Slave unit:
                if (unit.BondingObjId > 0)
                {
                    stream.WriteBc(unit.BondingObjId);
                    stream.Write(0);  // space
                    stream.Write(0);  // spot
                    stream.Write(0);  // type
                }
                else
                    stream.Write((byte)AttachPointKind.System);   // point
                break;
        }
        #endregion AttachPoint2

        #region UnitModelPosture

        Unit.ModelPosture(stream, _unit, _baseUnitType, _modelPostureType);
        postureEnd = stream.Count;

        #endregion

        stream.Write(_unit.ActiveWeapon);

        switch (_unit)
        {
            case Character:
                {
                    var skillList = character.Skills.Skills.Values.ToList();

                    stream.Write((byte)skillList.Count);       // learnedSkillCount
                    if (skillList.Count > 0)
                        Logger.Trace($"Warning! character.learnedSkillCount = {character.Skills.Skills.Count}");

                    stream.Write((byte)character.Skills.PassiveBuffs.Count); // passiveBuffCount
                    if (character.Skills.PassiveBuffs.Count > 0)
                        Logger.Trace($"Warning! character.passiveBuffCount = {character.Skills.PassiveBuffs.Count}");

                    stream.Write(character.HighAbilityRsc);                  // highAbilityRsc

                    stream.Write(0u);   // appellationStampId, uint32 in target
                    stream.Write(0u);   // vechicleDyeing (target field spelling)
                    stream.Write(false); // isTempFaction

                    var hcount = skillList.Count;
                    if (hcount > 0)
                    {
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(skillList[index].Id);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id,
                                            skillList[index + 2].Id);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id,
                                            skillList[index + 2].Id, skillList[index + 3].Id);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }

                    var buffList = character.Skills.PassiveBuffs.Values.ToList();
                    if (buffList.Count > 0)
                    {
                        hcount = buffList.Count;
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId,
                                            buffList[index + 2].Template.BuffId);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId,
                                            buffList[index + 2].Template.BuffId,
                                            buffList[index + 3].Template.BuffId);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }
                    break;
                }
            case Npc:
                {
                    var skills = new List<NpcSkill>();

                    if (npc.Template.BaseSkillId > 0)
                    {
                        skills.Add(new NpcSkill
                        {
                            Id = 0,
                            OwnerId = npc.TemplateId,
                            OwnerType = "Npc",
                            SkillId = (uint)npc.Template.BaseSkillId,
                            SkillUseCondition = SkillUseConditionKind.InCombat,
                            SkillUseParam1 = 0,
                            SkillUseParam2 = 0
                        });
                    }

                    foreach (var skillList in npc.Template.Skills.Values)
                        skills.AddRange(skillList);

                    stream.Write((byte)skills.Count);
                    stream.Write((byte)npc.Template.PassiveBuffs.Count);
                    stream.Write(npc.HighAbilityRsc);
                    stream.Write(0u);    // appellationStampId
                    stream.Write(0u);    // vehicleDyeing
                    stream.Write(false); // isTempFaction

                    for (var index = 0; index < skills.Count; index += 4)
                    {
                        switch (Math.Min(4, skills.Count - index))
                        {
                            case 1:
                                stream.WritePisc(skills[index].SkillId);
                                break;
                            case 2:
                                stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId);
                                break;
                            case 3:
                                stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId,
                                    skills[index + 2].SkillId);
                                break;
                            case 4:
                                stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId,
                                    skills[index + 2].SkillId, skills[index + 3].SkillId);
                                break;
                        }
                    }

                    var passiveBuffs = npc.Template.PassiveBuffs;
                    for (var index = 0; index < passiveBuffs.Count; index += 4)
                    {
                        switch (Math.Min(4, passiveBuffs.Count - index))
                        {
                            case 1:
                                stream.WritePisc(passiveBuffs[index].PassiveBuffId);
                                break;
                            case 2:
                                stream.WritePisc(passiveBuffs[index].PassiveBuffId,
                                    passiveBuffs[index + 1].PassiveBuffId);
                                break;
                            case 3:
                                stream.WritePisc(passiveBuffs[index].PassiveBuffId,
                                    passiveBuffs[index + 1].PassiveBuffId,
                                    passiveBuffs[index + 2].PassiveBuffId);
                                break;
                            case 4:
                                stream.WritePisc(passiveBuffs[index].PassiveBuffId,
                                    passiveBuffs[index + 1].PassiveBuffId,
                                    passiveBuffs[index + 2].PassiveBuffId,
                                    passiveBuffs[index + 3].PassiveBuffId);
                                break;
                        }
                    }
                    break;
                }
            default:
                {
                    stream.Write((byte)0); // learnedSkillCount
                    stream.Write((byte)0); // passiveBuffCount
                    stream.Write(0);       // highAbilityRsc
                    stream.Write(0u);      // appellationStampId, uint32 in target
                    stream.Write(0u);      // vechicleDyeing
                    stream.Write(false);   // isTempFaction
                    break;
                }
        }
        skillsEnd = stream.Count;

        // Rotation
        if (_baseUnitType == BaseUnitType.Housing)
            stream.Write(_unit.Transform.Local.Rotation.Z); // должно быть float
        else
        {
            var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }

        switch (_unit)
        {
            case Character:
                stream.Write(character.RaceGender);
                break;
            case Npc:
                stream.Write(npc.RaceGender);
                break;
            default:
                stream.Write(_unit.RaceGender);
                break;
        }

        if (_unit is Character)
        {
            // ???, ??? and Appellation (Title)
            stream.WritePisc(0, 0, character.Appellations.ActiveAppellation, 0);      // pisc
                                                                                      // Faction and Guild
            stream.WritePisc(character.Faction?.Id ?? 0, character.Expedition?.Id ?? 0, 0); // target group has 3 values
                                                                                               // PvP Honor gained and PvP Kills
            stream.WritePisc(character.HonorGainedInCombat, character.HostileFactionKills, 0, 0); // pisc
        }
        else
        {
            stream.WritePisc(0, _unit.TlId, 0, 0); // target per-unit transient identity
            stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0); // target group has 3 values
            stream.WritePisc(0, 0, 0, 0); // pisc
        }
        piscEnd = stream.Count;

        switch (_unit)
        {
            case Character:
                {
                    var flags = new BitSet(16); // short
                    if (character.Invisible)
                        flags.Set(5);
                    if (character.IdleStatus)
                        flags.Set(13);
                    stream.Write(flags.ToByteArray()); // flags(ushort)

                    /*
                    * 0x0001 - 8bit - режим боя
                    * 0x0002 - 7bit - 
                    * 0x0004 - 6bit - невидимость?
                    * 0x0008 - 5bit - дуэль
                    * 0x0010 - 4bit - 
                    * 0x0040 - 2bit - gmmode, дополнительно 7 байт
                    * 0x0080 - 1bit - дополнительно tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                    * 0x0020
                    * 0x0200
                    * 0x0100 - 16bit - дополнительно 3 байт (bc), firstHitterTeamId(uint)
                    * 0x0400 - 14bit - надпись "Отсутсвует" под именем
                    * 0x1000
                    * 0x0800
                    */
                    break;
                }
            case Npc:
                {
                    // NPC flags are runtime state, not a template-wide constant.
                    // Only bits whose server-side meaning is confirmed are emitted.
                    var flags = new BitSet(16);
                    if (npc.IsInBattle)
                        flags.Set(0); // 0x0001: combat state
                    if (npc.Invisible)
                        flags.Set(5); // 0x0020: invisible
                    stream.Write(flags.ToByteArray());
                    break;
                }
            default:
                stream.Write((ushort)0); // flags
                break;
        }

        stream.Write((byte)0); // attckFactionFlags (target field spelling)

        if (_unit is Character)
        {
            // Target 10.8.1 Character NetUnit tail (x2game.dll sub_39A069D0).
            // sub_39A9EDD0 serializes a fixed table of 29 ability records. Older
            // AAEmu revisions wrote the 12-entry table twice, which shifted every
            // field that followed it and made the client reject the self unit.
            var activeAbilities = character.Abilities.GetActiveAbilities();
            for (var abilityId = 1; abilityId <= 29; abilityId++)
            {
                if (character.Abilities.Abilities.TryGetValue((AbilityType)abilityId, out var ability))
                {
                    stream.Write(ability.Exp);
                    stream.Write(ability.Order);
                }
                else
                {
                    stream.Write(0);
                    stream.Write(byte.MaxValue);
                }
            }

            stream.Write((byte)activeAbilities.Count); // nActive
            foreach (var ability in activeAbilities)
                stream.Write((byte)ability); // active

            stream.WriteBc(0);     // duel target objId
            stream.Write((byte)0); // duelTeamType
            stream.Write((byte)0); // camp

            #region Stp
            stream.Write((byte)30);  // stp
            stream.Write((byte)60);  // stp
            stream.Write((byte)50);  // stp
            stream.Write((byte)0);   // stp
            stream.Write((byte)40);  // stp
            stream.Write((byte)100); // stp

            stream.Write((byte)0x20); // target default visual flags
            stream.Write((byte)0); // cosplay_visual

            //character6.VisualOptions.Write(stream, 0x20); // cosplay_visual
            //character6.VisualOptions.WriteOptions(stream);

            #endregion Stp

            stream.Write(1); // premium

            // _pageInfos: one default page. Each target page contains the five
            // base stat allocations followed by normal/special apply counters.
            stream.Write(1u); // pageInfos count
            for (var i = 0; i < 7; i++)
                stream.Write(0u);

            stream.Write(0u); // selectPageIndex
            stream.Write(0u); // extendMaxStats
            stream.Write(0u); // applyExtendCount

            // equipSlotReinforces: slotInfoList and levelEffectList
            stream.Write(0u); // slotInfoList count
            stream.Write(0u); // levelEffectList count
        }
        netUnitEnd = stream.Count;
        #endregion NetUnit

        #region NetBuff

        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();

        _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);
        var buffLayout = new List<string>();

        stream.Write((byte)goodBuffs.Count); // TODO max 32
        foreach (var buff in goodBuffs)
        {
            var buffStart = stream.Count;
            WriteBuff(stream, buff);
            buffLayout.Add($"G:{buff.Template.BuffId}:{buff.Index}:{stream.Count - buffStart}");
        }

        stream.Write((byte)badBuffs.Count); // TODO max 24 for 1.2, 20 for 3.0.3.0
        foreach (var buff in badBuffs)
        {
            var buffStart = stream.Count;
            WriteBuff(stream, buff);
            buffLayout.Add($"B:{buff.Template.BuffId}:{buff.Index}:{stream.Count - buffStart}");
        }

        stream.Write((byte)hiddenBuffs.Count); // TODO max 24 for 1.2, 28 for 3.0.3.0
        foreach (var buff in hiddenBuffs)
        {
            var buffStart = stream.Count;
            WriteBuff(stream, buff);
            buffLayout.Add($"H:{buff.Template.BuffId}:{buff.Index}:{stream.Count - buffStart}");
        }

        if (_unit is Npc layoutNpc)
        {
            Logger.Info(
                "SCUnitState layout: template={0}, objId={1}, equip={2}, model={3}, points={4}, posture={5}, skills={6}, pisc={7}, netUnit={8}, body={9}",
                layoutNpc.TemplateId, layoutNpc.ObjId, equipEnd - bodyStart, modelParamsEnd - bodyStart,
                pointsEnd - bodyStart, postureEnd - bodyStart, skillsEnd - bodyStart, piscEnd - bodyStart,
                netUnitEnd - bodyStart, stream.Count - bodyStart);

            // Keep a small sample of dynamically generated NPC states for
            // byte-level comparison with donor captures. No donor body is read
            // or replayed by the server.
            var dumpIndex = System.Threading.Interlocked.Increment(ref _npcDumpCount);
            if (dumpIndex <= 5)
            {
                var body = stream.GetBytes().Skip(bodyStart).ToArray();
                var dumpDirectory = Path.Combine(AppContext.BaseDirectory, ".local-runtime", "protocol-dumps");
                Directory.CreateDirectory(dumpDirectory);
                var dumpPath = Path.Combine(
                    dumpDirectory,
                    $"current_npc_133_{dumpIndex:D2}_tpl{layoutNpc.TemplateId}_obj{layoutNpc.ObjId}.bin");
                File.WriteAllBytes(dumpPath, body);
                Logger.Warn(
                    "SCUnitState NPC dump: index={0}, template={1}, objId={2}, body={3}, path={4}",
                    dumpIndex, layoutNpc.TemplateId, layoutNpc.ObjId, body.Length, dumpPath);
            }
        }
        else if (_unit is Character layoutCharacter)
        {
            var body = stream.GetBytes().Skip(bodyStart).ToArray();
            var modelParamsLength = layoutCharacter.ModelParams?.Write(new PacketStream()).Count ?? 0;
            var dumpDirectory = Path.Combine(AppContext.BaseDirectory, ".local-runtime", "protocol-dumps");
            Directory.CreateDirectory(dumpDirectory);
            var dumpPath = Path.Combine(dumpDirectory, "current_self_133.bin");
            File.WriteAllBytes(dumpPath, body);

            Logger.Warn(
                "SCUnitState self layout: charId={0}, race={1}, gender={2}, modelId={3}, modelParams={4}, equip={5}, model={6}, points={7}, posture={8}, skills={9}, pisc={10}, netUnit={11}, body={12}, dump={13}",
                layoutCharacter.Id, layoutCharacter.Race, layoutCharacter.Gender,
                layoutCharacter.ModelId, modelParamsLength,
                equipEnd - bodyStart, modelParamsEnd - bodyStart, pointsEnd - bodyStart,
                postureEnd - bodyStart, skillsEnd - bodyStart, piscEnd - bodyStart,
                netUnitEnd - bodyStart, body.Length, dumpPath);
            Logger.Warn(
                "SCUnitState self buffs: good={0}, bad={1}, hidden={2}, records=[{3}]",
                goodBuffs.Count, badBuffs.Count, hiddenBuffs.Count, string.Join(",", buffLayout));
        }
        #endregion NetBuff

        return stream;
    }

    private void WriteBuff(PacketStream stream, Buff buff)
    {
        // Target NetBuff serializer (x2game.dll 0x399F99E0). Successful
        // 1.8.1.0 self 0x0133 captures contain three PISC groups per buff;
        // the legacy writer emitted only two, so the client treated the next
        // buff/count byte as part of the current record.
        stream.Write(buff.Index);       // runtime buff index
        stream.Write(buff.SkillCaster); // source type + Bc source object id

        var ownerId = buff.Owner?.Id ?? buff.Caster?.Id ?? _unit.Id;
        stream.Write(ownerId); // type/id: owner persistent id in self captures

        // These legacy scalar fields are zero in all inspected target self
        // records. Source level and visibility kind are encoded by PISC below.
        stream.Write((byte)0);
        stream.Write((ushort)0);

        var sourceLevel = buff.Caster?.Level
            ?? (buff.Owner as Unit)?.Level
            ?? _unit.Level;
        var kindFlag = buff.Template.Kind switch
        {
            BuffKind.Good => 1,
            BuffKind.Bad => 2,
            BuffKind.Hidden => 4,
            _ => 0
        };

        // Group 1 is wire-confirmed as level, kind bit, zero, constant 4.
        stream.WritePisc(sourceLevel, kindFlag, 0, 4);

        // Group 2 carries live timer values. Use seconds and clamp infinite
        // buffs to zero; PISC itself records each integer width.
        var durationSeconds = buff.Duration > 0
            ? Math.Max(0L, buff.Duration / 1000L)
            : 0L;
        var remainingSeconds = buff.Duration > 0
            ? Math.Max(0L, (long)Math.Ceiling(buff.GetTimeLeft() / 1000.0))
            : 0L;
        stream.WritePisc(remainingSeconds, durationSeconds, 0, 0);

        // Group 3 is the template identity and stack. This exact third group
        // is what was missing from the previous generated SCUnitState.
        stream.WritePisc(buff.Template.BuffId, Math.Max(1, buff.Stack), 0, 0);
    }

    #region CharacterInfo_3EB0

    private void Inventory_Equip0(PacketStream stream, Unit unit)
    {
        var index = 0;
        var validFlags = 0;
        if (unit is Character character1)
        {
            // calculate validFlags
            var items = character1.Inventory.Equipment.GetSlottedItemsList();
            validFlags = CalculateValidFlags(items);
            stream.Write((uint)validFlags); // validFlags for 3.0.3.0
            var itemSlot = EquipmentItemSlot.Head;
            foreach (var item in items)
            {
                if (item == null)
                {
                    itemSlot++;
                    continue;
                }
                switch (itemSlot)
                {
                    case EquipmentItemSlot.Head:
                    case EquipmentItemSlot.Neck:
                    case EquipmentItemSlot.Chest:
                    case EquipmentItemSlot.Waist:
                    case EquipmentItemSlot.Legs:
                    case EquipmentItemSlot.Hands:
                    case EquipmentItemSlot.Feet:
                    case EquipmentItemSlot.Arms:
                    case EquipmentItemSlot.Back:
                    case EquipmentItemSlot.Undershirt:
                    case EquipmentItemSlot.Underpants:
                    case EquipmentItemSlot.Mainhand:
                    case EquipmentItemSlot.Offhand:
                    case EquipmentItemSlot.Ranged:
                    case EquipmentItemSlot.Musical:
                    case EquipmentItemSlot.Stabilizer:
                    case EquipmentItemSlot.Cosplay:
                        {
                            stream.Write(item);
                            break;
                        }
                    case EquipmentItemSlot.Face:
                    case EquipmentItemSlot.Hair:
                    case EquipmentItemSlot.Glasses:
                    case EquipmentItemSlot.Horns:
                    case EquipmentItemSlot.Tail:
                    case EquipmentItemSlot.Body:
                    case EquipmentItemSlot.Beard:
                        {
                            stream.Write(item.TemplateId);
                            break;
                        }
                    case EquipmentItemSlot.Ear1:
                    case EquipmentItemSlot.Ear2:
                    case EquipmentItemSlot.Finger1:
                    case EquipmentItemSlot.Finger2:
                    case EquipmentItemSlot.Backpack:
                        {
                            break;
                        }
                }
                itemSlot++;
            }
        }
        else if (unit is Npc npc)
        {
            // calculate validFlags for 3.0.3.0
            for (var i = 0; i < npc.Equipment.GetSlottedItemsList().Count; i++)
            {
                var item = npc.Equipment.GetItemBySlot(i);
                if (item != null)
                {
                    validFlags |= 1 << index;
                }

                index++;
            }
            stream.Write((uint)validFlags); // validFlags for 3.0.3.0
            if (validFlags <= 0)
            {
                unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
                return;
            }
            var itemSlot = EquipmentItemSlot.Head;
            var items = npc.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item == null)
                {
                    itemSlot++;
                    continue;
                }
                switch (itemSlot)
                {
                    case EquipmentItemSlot.Head:
                    case EquipmentItemSlot.Neck:
                    case EquipmentItemSlot.Chest:
                    case EquipmentItemSlot.Waist:
                    case EquipmentItemSlot.Legs:
                    case EquipmentItemSlot.Hands:
                    case EquipmentItemSlot.Feet:
                    case EquipmentItemSlot.Arms:
                    case EquipmentItemSlot.Back:
                    case EquipmentItemSlot.Undershirt:
                    case EquipmentItemSlot.Underpants:
                    case EquipmentItemSlot.Mainhand:
                    case EquipmentItemSlot.Offhand:
                    case EquipmentItemSlot.Ranged:
                    case EquipmentItemSlot.Musical:
                        {
                            stream.Write(item.TemplateId);
                            stream.Write(0L);
                            stream.Write((byte)0);
                            break;
                        }
                    case EquipmentItemSlot.Cosplay:
                        {
                            stream.Write(item);
                            break;
                        }
                    case EquipmentItemSlot.Face:
                    case EquipmentItemSlot.Hair:
                    case EquipmentItemSlot.Glasses:
                    case EquipmentItemSlot.Horns:
                    case EquipmentItemSlot.Tail:
                    case EquipmentItemSlot.Body:
                    case EquipmentItemSlot.Beard:
                        {
                            stream.Write(item.TemplateId);
                            break;
                        }
                    case EquipmentItemSlot.Ear1:
                    case EquipmentItemSlot.Ear2:
                    case EquipmentItemSlot.Finger1:
                    case EquipmentItemSlot.Finger2:
                    case EquipmentItemSlot.Backpack:
                    case EquipmentItemSlot.Stabilizer:
                        {
                            break;
                        }
                }
                itemSlot++;
            }
        }
        else // for transfer and Shipyard
        {
            stream.Write(0u); // validFlags for 3.0.3.0
        }

        if (_unit is Character chrUnit)
        {
            index = 0;
            var ItemFlags = 0;
            var items = chrUnit.Inventory.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item != null)
                {
                    var v15 = (int)item.ItemFlags << index;
                    ++index;
                    ItemFlags |= v15;
                }
            }
            stream.Write(ItemFlags); //  ItemFlags flags for 3.0.3.0
        }
    }
    private void Inventory_Equip1(PacketStream stream, Unit unit0, BaseUnitType baseUnitType)
    {
        var unit = new Unit();
        switch (baseUnitType)
        {
            case BaseUnitType.Character:
                {
                    unit = (Character)unit0;
                    break;
                }
            case BaseUnitType.Npc:
                {
                    unit = (Npc)unit0;
                    break;
                }
            case BaseUnitType.Slave:
                {
                    unit = (Slave)_unit;
                    break;
                }
            case BaseUnitType.Housing:
                {
                    unit = (House)_unit;
                    break;
                }
            case BaseUnitType.Transfer:
                {
                    unit = (Transfer)_unit;
                    break;
                }
            case BaseUnitType.Mate:
                {
                    unit = (Mate)_unit;
                    break;
                }
            case BaseUnitType.Shipyard:
                {
                    unit = (Shipyard)_unit;
                    break;
                }
            default:
                {
                    break;
                }
        }

        var items = unit.Equipment.GetSlottedItemsList();
        var validFlags = CalculateValidFlags(items);
        stream.Write((uint)validFlags); // validFlags for 3.0.3.0

        if (validFlags <= 0)
        {
            unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
            return;
        }

        var index = 0;
        do
        {
            if (((validFlags >> index) & 1) != 0)
            {
                Item item;
                //if ((index - 19 >= 0 && index - 19 <= 6) || baseUnitType == BaseUnitType.Slave) // Slave
                if (index - 19 < 0 || index - 19 > 6)
                {
                    //if (index != 27 || baseUnitType != BaseUnitType.Npc)  // not CosPlay || not Npc
                    if (index != 27) // not CosPlay
                    {
                        switch (baseUnitType)
                        {
                            case BaseUnitType.Character: // Character
                            case BaseUnitType.Housing: // Housing
                            case BaseUnitType.Mate: // Mate
                            case BaseUnitType.Slave: // Slave
                                {
                                    item = unit.Equipment.GetItemBySlot(index);
                                    stream.Write(item);
                                    break;
                                }
                            case BaseUnitType.Npc: // Npc
                                {
                                    item = unit.Equipment.GetItemBySlot(index);
                                    stream.Write(item.TemplateId);
                                    stream.Write(item.Id);
                                    stream.Write(item.Grade);
                                    break;
                                }
                            case BaseUnitType.Transfer:
                            case BaseUnitType.Shipyard:
                                {
                                    break;
                                }
                            default:
                                {
                                    break;
                                }
                        }
                    }
                    else
                    {
                        item = unit.Equipment.GetItemBySlot(index);
                        stream.Write(item); // Cosplay [27]
                    }
                }
                else
                {
                    item = unit.Equipment.GetItemBySlot(index);
                    stream.Write(item.TemplateId); // somehow_special [19..26]
                }
            }

            ++index;
        } while (index < 29);

        if (baseUnitType != BaseUnitType.Character) { return; }

        var itemFlags = CalculateItemFlags(items);
        stream.Write(itemFlags); // ItemFlags flags for 3.0.3.0
    }
    private void Inventory_Equip2(PacketStream stream, Unit unit0, BaseUnitType baseUnitType)
    {
        var unit = new Unit();
        switch (baseUnitType)
        {
            case BaseUnitType.Character:
                {
                    unit = (Character)unit0;
                    break;
                }
            case BaseUnitType.Npc:
                {
                    unit = (Npc)unit0;
                    break;
                }
            case BaseUnitType.Slave:
                {
                    unit = (Slave)_unit;
                    break;
                }
            case BaseUnitType.Housing:
                {
                    unit = (House)_unit;
                    break;
                }
            case BaseUnitType.Transfer:
                {
                    unit = (Transfer)_unit;
                    break;
                }
            case BaseUnitType.Mate:
                {
                    unit = (Mate)_unit;
                    break;
                }
            case BaseUnitType.Shipyard:
                {
                    unit = (Shipyard)_unit;
                    break;
                }
            default:
                {
                    break;
                }
        }

        // calculate validFlags
        var items = unit.Equipment.GetSlottedItemsList();
        var validFlags = CalculateValidFlags(items);
        stream.Write((uint)validFlags); // validFlags for 3.0.3.0

        if (validFlags <= 0)
        {
            unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
            return;
        }

        var index = 0;
        do
        {
            if (((validFlags >> index) & 1) != 0)
            {
                Item item;
                switch (index)
                {
                    case 0: // Head
                    case 1: // Neck
                    case 2: // Chest
                    case 3: // Waist
                    case 4: // Legs
                    case 5: // Hands
                    case 6: // Feet
                    case 7: // Arms
                    case 8: // Back
                    case 9: // Ear1
                    case 10: // Ear2
                    case 11: // Finger1
                    case 12: // Finger2
                    case 13: // Undershirt
                    case 14: // Underpants
                    case 15: // Mainhand
                    case 16: // Offhand
                    case 17: // Ranged
                    case 18: // Musical
                    case 26: // Backpack
                    case 28: // Stabilizer
                        {
                            switch (baseUnitType)
                            {
                                case BaseUnitType.Character: // Character
                                case BaseUnitType.Housing:   // Housing
                                case BaseUnitType.Mate:      // Mate
                                case BaseUnitType.Slave:     // Slave
                                    {
                                        item = unit.Equipment.GetItemBySlot(index);
                                        stream.Write(item);
                                        break;
                                    }
                                case BaseUnitType.Npc:       // Npc
                                    {
                                        item = unit.Equipment.GetItemBySlot(index);
                                        stream.Write(item.TemplateId);
                                        stream.Write(item.Id);
                                        stream.Write(item.Grade);
                                        break;
                                    }
                                case BaseUnitType.Transfer:
                                case BaseUnitType.Shipyard:
                                default:
                                    {
                                        break;
                                    }
                            }
                            break;
                        }
                    case 19: // Face
                    case 20: // Hair
                    case 21: // Glasses
                    case 22: // Horns
                    case 23: // Tail
                    case 24: // Body
                    case 25: // Beard
                        {
                            item = unit.Equipment.GetItemBySlot(index);
                            stream.Write(item.TemplateId); // somehow_special [19..25]
                            break;
                        }
                    case 27: // Cosplay
                        {
                            item = unit.Equipment.GetItemBySlot(index);
                            stream.Write(item); // Cosplay [27]
                            break;
                        }
                }
            }

            ++index;
        } while (index < 29);

        if (baseUnitType != BaseUnitType.Character) { return; }

        var itemFlags = CalculateItemFlags(items);
        stream.Write(itemFlags); // ItemFlags flags for 3.0.3.0
    }

    private void Inventory_Equip3(PacketStream stream, Unit unit)
    {
        var items = new List<Item>();

        switch (unit)
        {
            case Character character:
                {
                    items = character.Inventory.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    // Target 10.8.1 character captures use a fixed 60-bit
                    // equipment capability/visibility mask here. This is not
                    // the legacy per-item ItemFlags aggregation.
                    stream.Write(0x0FFFFFFFFFFFFFFFUL);
                    break;
                }
            case House house:
                {
                    items = house.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Mate mate:
                {
                    items = mate.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Slave slave:
                {
                    items = slave.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Npc npc:
                {
                    items = npc.Equipment.GetSlottedItemsList();
                    var validFlags = CalculateProtocol1810ValidFlags(items);
                    stream.Write(validFlags); // target 35-slot mask is uint64

                    if (validFlags == 0)
                        return;

                    for (var i = 0; i < items.Count; i++)
                    {
                        var item = npc.Equipment.GetItemBySlot(i);

                        if (item is BodyPart)
                        {
                            stream.Write(item.TemplateId);
                        }
                        else if (item != null)
                        {
                            // Per G:\Work\UnitState_NPC_0x0133_verified.md: full Item is used only
                            // for NPC slots 27, 31, 32 and 33 - everything else in range uses the
                            // compact 13-byte form. Slots 31-33 were previously falling into the
                            // compact branch, under-writing the body and shifting every field that
                            // follows (buffs etc.) for any NPC equipped in those slots.
                            if (i == 27 || (i >= 31 && i <= 33)) // Cosplay, 31, 32, 33
                            {
                                stream.Write(item);
                            }
                            else
                            {
                                stream.Write(item.TemplateId);
                                stream.Write(item.Id);
                                stream.Write(item.Grade);
                            }
                        }
                    }
                    break;
                }
            // for transfer and Shipyard
            default:
                {
                    stream.Write(0UL); // target 10.8.1 valid-slot mask
                    break;
                }
        }
    }

    private static void WriteEquip(PacketStream stream, List<Item> items)
    {
        var validFlags = CalculateProtocol1810ValidFlags(items);
        stream.Write(validFlags); // target 10.8.1 35-slot mask is uint64
        WriteItems(stream, items);
    }

    private static void WriteItems(PacketStream stream, List<Item> items)
    {
        foreach (var item in items)
        {
            if (item != null)
            {
                stream.Write(item);
            }
        }
    }

    private static int CalculateValidFlags(List<Item> items)
    {
        var validFlags = 0;
        var index = 0;
        foreach (var item in items)
        {
            if (item != null)
            {
                validFlags |= 1 << index;
            }

            index++;
        }

        return validFlags;
    }

    private static ulong CalculateProtocol1810ValidFlags(List<Item> items)
    {
        ulong validFlags = 0;
        var count = Math.Min(items.Count, 35);
        for (var index = 0; index < count; index++)
        {
            if (items[index] != null)
                validFlags |= 1UL << index;
        }

        return validFlags;
    }


    private static void WriteProtocol1810Position(PacketStream stream, Vector3 position)
    {
        // NetUnit uses the target 11-byte WorldPos form, not the legacy
        // nine-byte movement position.
        stream.Write((int)(position.X * 512f));
        stream.Write((int)(position.Y * 512f));

        var zRaw = (int)Math.Floor(((position.Z + 100f) / 4196f * 4194304f) + 0.5f);
        zRaw = Math.Clamp(zRaw, 0, 0xFFFFFF);
        stream.Write(new[]
        {
            (byte)zRaw,
            (byte)(zRaw >> 8),
            (byte)(zRaw >> 16)
        });
    }

    private static int CalculateItemFlags(List<Item> items)
    {
        var itemFlags = 0;
        var index = 0;

        foreach (var tmp in items
                     .Where(item => item != null)
                     .Select(item => (int)item.ItemFlags << index))
        {
            ++index;
            itemFlags |= tmp;
        }

        return itemFlags;
    }

    #endregion CharacterInfo_3EB0

    public override string Verbose()
    {
        return " - " + _baseUnitType + " - " + _unit?.DebugName();
    }
}
