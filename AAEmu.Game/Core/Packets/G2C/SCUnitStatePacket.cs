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
    /// <summary>Equipment slots the model/posture block describes, one bit each in its mask.</summary>
    private const int Protocol1810EquipmentSlots = 35;

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
            // The building posture. Sending the empty form instead was tried once and made the
            // client crash rather than ignore the building - but that was measured on a body that
            // was already two bytes out at the front, so it proved nothing either way. Left as it
            // is until it can be judged on a packet that is otherwise correct.
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
            // The byte written just above the switch is the tag of a tagged identity, and this is
            // its variant for an ordinary player. A reading that called the tag missing was nearly
            // acted on here; the tag is the unit-type byte itself, and the seven types are the
            // seven tags. The shipyard variant coming to thirteen bytes and the building's to nine
            // only works with the byte counted once.
            case BaseUnitType.Character:
                stream.Write((ulong)(character?.Id ?? 0u)); // identityValue     : u64
                stream.Write(0L);                           // identitySecondary : u64, unread here
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(npc.ObjId);    // objId
                stream.Write(npc.TemplateId); // npc templateId
                stream.Write(0L);              // type(id), uint64 in target
                stream.Write((byte)0);        // clientDriven
                break;
            // Ships and land vehicles. The branch names the vehicle first - its own persistent
            // id and its own handle - and only then the character who owns it.
            //
            // Both used to be filled with the owner: the summoner's persistent id where the
            // vehicle's belongs, the summoner's object id truncated to sixteen bits where the
            // handle belongs, and a zero where the owner actually goes. A vehicle whose owner
            // reads as nobody still appears in the world, but the client never registers it as
            // locally controlled, which is what stops it being driven.
            case BaseUnitType.Slave:
                var slave = (Slave)_unit;
                stream.Write((long)slave.Id);                      // slaveId           : i64
                stream.Write(slave.TlId);                          // tl                : u16
                stream.Write(slave.TemplateId);                    // slaveTemplateId   : u32
                stream.Write((long)(slave.Summoner?.Id ?? 0u));    // masterId          : i64
                stream.Write((byte)(slave.Summoner?.Transform.WorldId ?? 0)); // masterWorldId : u8
                stream.Write(slave.TemplateId);                    // visualSlaveDescId : u32
                break;                                             // 28 bytes with the discriminator
            // Buildings, nine bytes with the discriminator: the handle at two, the design at four
            // and the construction stage at two. Both the handle and the stage are read through
            // the client's sixteen-bit archive helper, and writing either of them as four bytes
            // pushes everything behind it along - the stage did exactly that for a while, on the
            // word of a table that has now been withdrawn.
            //
            // The stage is the ordinal the design's own step rows are numbered by: the client
            // looks up the row whose step matches to know what the half-built house looks like,
            // and takes the finished model when there is no such row. It is not a count of what
            // is left to do, which is what the negative value here used to be.
            case BaseUnitType.Housing:
                var house = (House)_unit;

                // How much building is left, counted from the end: the work done less the work
                // the design asks for. So it runs negative and climbs to zero, minus one means
                // one action short, and zero means finished - which is also what a design with no
                // stages at all sends, both its counts being zero.
                //
                // Not the ordinal of the current stage, which is what went out for a while. That
                // happened to be zero for a building just placed, which is the right answer by
                // accident, and would have drifted the moment anyone hammered a nail into it.
                var buildStep = house.CurrentStep == -1 ? 0 : house.CurrentAction - house.AllAction;

                stream.Write(house.TlId);         // tl         : u16
                stream.Write(house.TemplateId);   // designType : u32
                stream.Write((short)buildStep);   // buildstep  : i16
                break;                            // 9 bytes with the discriminator
            case BaseUnitType.Transfer:
                var transfer = (Transfer)_unit;
                stream.Write(transfer.TlId); // tl
                stream.Write(transfer.TemplateId); // templateId
                break;
            // Pets and mounts. The first field is the mate's own handle, not the owner's -
            // this is the only place the handle and the world object ever meet.
            //
            // A slave gets that pairing from its own message: SCMySlave carries the object id
            // and the handle together. A mate has no such message - SCMateSpawned files the
            // record under the handle and never mentions the object. So if this field does not
            // carry the handle, the record and the animal in the world are never connected: the
            // mount stands there with no record behind it, which is an empty skill bar and
            // nothing to ride. The owner is identified by the persistent character id below,
            // and separately by the master name that follows the branch.
            case BaseUnitType.Mate:
                var mount = (Mate)_unit;
                stream.Write(mount.TlId);          // tl             : u16
                stream.Write(mount.TemplateId);    // mateTemplateId : u32
                stream.Write((long)mount.OwnerId); // masterId       : i64
                break;                             // 15 bytes with the discriminator
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

        // Humanoid NPCs get the Skin variant of this block. Nothing else gets an override at
        // all: sending one broadly put TEST text and black hands back, including on the 2583
        // NPCs whose look comes from a genuine total_character_customs row, so the fault is
        // not confined to the appearances LoadCustom invents.
        //
        // In this client's layout the Skin discriminator stops the block after three colour
        // fields:
        //
        //     type:u8, hairColorId:u32, hornColorId:u32, skinColorId:u32   - 13 bytes
        //
        // No model id, no body normal map, and no Face sub-block. That is enough for the
        // races whose NPCs read acceptably without one.
        //
        // Firran get the full Face variant, because for them it is not optional. Their
        // customs all name face_id 0, so every Firran NPC of a gender wears the same face
        // body part; everything that makes one differ from the next lives in the Face block
        // and nowhere else. Across the 114 male rows: 98 distinct morph modifiers, 81
        // distinct pupil colours, 13 face normal maps. Suppress the block and every male
        // Firran is necessarily identical - which is exactly what it looked like.
        //
        // The layout used here matches the verified 0x0133 field order end to end, including
        // bodyNormalMapId/Weight sitting after the two-tone fields and immediately before the
        // face payload, the modifier written as a u16 length followed by its bytes rather
        // than as a bare 128-byte run, and the 20-byte visual-race/wing tail after it. Any
        // of those three wrong shifts everything that follows.
        //
        // The skin colour is sent as stored. It used to be zeroed together with the body
        // normal map, and the two are not equivalent - the data says so. body_normal_maps is
        // numbered from 1, yet 0 is what every Nuian, Elf, Hariharan and Firran custom
        // actually stores: it is that field's "no override" sentinel. skin_colors is numbered
        // 1..171 with no row 0 at all, so zeroing it handed the client an id that cannot
        // resolve. An NPC whose skin colour did not resolve gets no block rather than a zero.
        //
        // The copy is packet-local on purpose: never mutate the NPC template or player data.
        var modelParams = _unit.ModelParams ?? new UnitCustomModelParams(UnitCustomModelType.None);
        if (npc != null)
        {
            if (npc.ModelId is 20 or 21 && modelParams.Face != null && modelParams.SkinColorId != 0)
            {
                modelParams = new UnitCustomModelParams(UnitCustomModelType.Face)
                    .SetId(modelParams.Id)
                    .SetHairColorId(modelParams.HairColorId)
                    .SetHornColorId(modelParams.HornColorId)
                    .SetSkinColorId(modelParams.SkinColorId)
                    .SetModelId(modelParams.ModelId)
                    .SetDefaultHairColor(modelParams.DefaultHairColor)
                    .SetTwoToneHair(modelParams.TwoToneHair)
                    .SetTwoToneFirstWidth(modelParams.TwoToneFirstWidth)
                    .SetTwoToneSecondWidth(modelParams.TwoToneSecondWidth)
                    .SetBodyNormalMapId(0)
                    .SetBodyNormalMapWeight(0f)
                    .SetFace(modelParams.Face);
            }
            else if (npc.ModelId is 10 or 11 or 14 or 15 or 16 or 17 or 18 or 19 or 24 or 25
                     && modelParams.SkinColorId != 0)
            {
                modelParams = new UnitCustomModelParams(UnitCustomModelType.Skin)
                    .SetHairColorId(modelParams.HairColorId)
                    .SetHornColorId(modelParams.HornColorId)
                    .SetSkinColorId(modelParams.SkinColorId);
            }
            else
            {
                modelParams = new UnitCustomModelParams(UnitCustomModelType.None);
            }
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
                    // The point byte leads this block exactly as it does the one above, and the
                    // rest only follows when it is not the System sentinel. It was missing here,
                    // so a slave bonded to another one shifted the whole tail by a byte. We have
                    // no attach point modelled for bonding, and any non-sentinel value keeps the
                    // tuple, so None goes out until there is something real to send.
                    stream.Write((byte)AttachPointKind.None); // point
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

        // Three packed groups. The first ends with the appellation and holds nothing else anyone
        // reads; the handle that used to sit in its second slot was never confirmed and is not
        // wanted. The middle group is the affiliations. The last one belongs to characters; for
        // everything else it is parsed and thrown away.
        //
        // The faction leads the middle group. Two separate audits place it third instead, one of
        // them on a client consumer that publishes the third slot under that name - but with it
        // third every unit in the world came out of the same side as the player, mobs included,
        // and putting it back in front made them hostile again. That was measured twice, in play,
        // which beats a reading of the binary. Guild follows it; family is not sent at all.
        if (_unit is Character)
        {
            stream.WritePisc(0, 0, 0, character.Appellations.ActiveAppellation);        // A3 = appellation
            stream.WritePisc(character.Faction?.Id ?? 0, character.Expedition?.Id ?? 0, 0);
            stream.WritePisc(character.HonorGainedInCombat, character.HostileFactionKills, 0, 0);
        }
        else
        {
            stream.WritePisc(0, 0, 0, 0);                                           // nothing here is read
            stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0);
            stream.WritePisc(0, 0, 0, 0);
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
        stream.WritePisc(buff.Template.BuffId, Math.Max(1, buff.StackCount), 0, 0);
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
            // A mate is the one family that does not describe slots 19 to 25 in full. Those are
            // the body-part references, and for a mate the client reads nothing but the type
            // from them - writing the whole item record there puts everything after it out of
            // place. A slave uses the ordinary full record for the same slots.
            case Mate mate:
                {
                    items = mate.Equipment.GetSlottedItemsList();
                    var mateValidFlags = CalculateProtocol1810ValidFlags(items);
                    stream.Write(mateValidFlags);

                    for (var slot = 0; slot < Protocol1810EquipmentSlots; slot++)
                    {
                        if ((mateValidFlags & (1UL << slot)) == 0)
                            continue;

                        if (slot is >= 19 and <= 25)
                            stream.Write(items[slot].TemplateId);
                        else
                            stream.Write(items[slot]);
                    }
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

                    // Target serializer 0x3996AB80 iterates all 35 mask
                    // bits and selects the NPC entry shape by protocol slot,
                    // not by the server-side runtime Item subclass.
                    for (var slot = 0; slot < Protocol1810EquipmentSlots; slot++)
                    {
                        if ((validFlags & (1UL << slot)) == 0)
                            continue;

                        var item = npc.Equipment.GetItemBySlot(slot);
                        if (item == null)
                            throw new InvalidOperationException(
                                $"NPC equipment mask references empty slot {slot} for template {npc.TemplateId}");

                        if (slot is >= 19 and <= 25)
                        {
                            // FACE..BEARD are body-part references.
                            stream.Write(item.TemplateId);
                        }
                        else if (slot == 27 || slot is >= 30 and <= 33)
                        {
                            // COSPLAY plus target visual/full-item slots.
                            stream.Write(item);
                        }
                        else
                        {
                            // Compact NPC item: templateId:u32, iid:u64, grade:u8.
                            stream.Write(item.TemplateId);
                            stream.Write(item.Id);
                            stream.Write(item.Grade);
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
        var count = Math.Min(items.Count, Protocol1810EquipmentSlots);
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
