using System;
using System.Diagnostics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks.SaveTask;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SaveManager : Singleton<SaveManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private double Delay = 1;
    private bool _enabled;
    private bool _isSaving;
    private object _lock = new();
    private SaveTickStartTask saveTask;

    public SaveManager()
    {
        _enabled = false;
        _isSaving = false;
    }

    public void Initialize()
    {
        Logger.Info("Initialising Save Manager...");
        _enabled = true;
        Delay = AppConfiguration.Instance.World.AutoSaveInterval;
        SaveTickStart();
    }

    public async void Stop()
    {
        _enabled = false;
        if (saveTask == null)
        {
            return;
        }
        var result = await saveTask.CancelAsync();
        if (result)
        {
            saveTask = null;
        }
        // Do one final save here
        DoSave();
    }

    public void SaveTickStart()
    {
        // Logger.Warn("SaveTickStart: Started");
        saveTask = new SaveTickStartTask();
        TaskManager.Instance.Schedule(saveTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
    }

    public bool DoSave()
    {
        if (_isSaving)
            return false;
        var saved = false;
        lock (_lock)
        {
            _isSaving = true;
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            try
            {
                // Save stuff
                Logger.Debug("Saving DB ...");
                using (var connection = MySQL.CreateConnection())
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        // Houses
                        var savedHouses = HousingManager.Instance.Save(connection, transaction);
                        // Mail
                        var savedMails = MailManager.Instance.Save(connection, transaction);
                        // Items
                        var saveItems = ItemManager.Instance.Save(connection, transaction);
                        //Auction House
                        var savedAuctionHouse = AuctionManager.Instance.Save(connection, transaction);

                        // Characters
                        var savedCharacters = 0;
                        foreach (var c in WorldManager.Instance.GetAllCharacters())
                        {
                            if (c.Save(connection, transaction))
                                savedCharacters++;
                            else
                                Logger.Error("Failed to get save data for character {0} - {1}", c.Id, c.Name);
                        }

                        // Slaves
                        var savedSlaves = 0;
                        foreach (var slave in WorldManager.Instance.GetAllSlaves())
                        {
                            if (slave.Save(connection, transaction))
                                savedSlaves++;
                        }

                        var totalCommits = 0;
                        totalCommits += savedHouses.Item1 + savedHouses.Item2;
                        totalCommits += savedMails.Item1 + savedMails.Item2;
                        totalCommits += saveItems.Item1 + saveItems.Item2 + saveItems.Item3;
                        totalCommits += savedAuctionHouse.Item1 + savedAuctionHouse.Item2;
                        totalCommits += savedCharacters;
                        totalCommits += savedSlaves;

                        if (totalCommits <= 0)
                        {
                            Logger.Debug("No data to update ...");
                            saved = true;
                        }
                        else
                        {
                            try
                            {
                                transaction.Commit();

                                if ((savedHouses.Item1 + savedHouses.Item2) > 0)
                                    Logger.Debug($"Updated {savedHouses.Item1} and deleted {savedHouses.Item2} houses ...");
                                if ((savedMails.Item1 + savedMails.Item2) > 0)
                                    Logger.Debug($"Updated {savedMails.Item1} and deleted {savedMails.Item2} mails ...");
                                if ((saveItems.Item1 + saveItems.Item2) > 0)
                                    Logger.Debug($"Updated {saveItems.Item1} and deleted {saveItems.Item2} items in {saveItems.Item3} containers ...");
                                if ((saveItems.Item3) > 0)
                                    Logger.Debug($"Updated {saveItems.Item3} item containers ...");
                                if ((savedAuctionHouse.Item1 + savedAuctionHouse.Item2) > 0)
                                    Logger.Debug($"Updated {savedAuctionHouse.Item1} and deleted {savedAuctionHouse.Item2} auction items ...");
                                if (savedCharacters > 0)
                                    Logger.Debug($"Updated {savedCharacters} characters ...");
                                if (savedSlaves > 0)
                                    Logger.Debug($"Updated {savedSlaves} slaves ...");

                                saved = true;
                            }
                            catch (Exception e)
                            {
                                Logger.Error(e);
                                try
                                {
                                    transaction.Rollback();
                                }
                                catch (Exception eRollback)
                                {
                                    Logger.Error(eRollback);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "DoSave Exception\n");
            }
            stopWatch.Stop();
            Logger.Debug("Saving data took {0}", stopWatch.Elapsed);
        }
        _isSaving = false;
        return saved;
    }

    /// <summary>
    /// Persists all dirty item containers and items belonging to one character immediately.
    /// </summary>
    /// <remarks>
    /// Slave equipment changes must be durable before the success packet is sent. Otherwise a
    /// server stop between the equipment swap and the next world autosave restores the old ship
    /// loadout. The same lock used by the global save serializes both transactions.
    /// </remarks>
    public bool SaveItemsForOwner(uint ownerId, string reason)
    {
        if (ownerId == 0)
            return false;

        lock (_lock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var transaction = connection.BeginTransaction();

                var saved = ItemManager.Instance.Save(connection, transaction, ownerId);
                transaction.Commit();

                Logger.Info(
                    "Persisted owner item state: owner={0}, reason={1}, items={2}, deleted={3}, containers={4}",
                    ownerId, reason ?? string.Empty, saved.Item1, saved.Item2, saved.Item3);
                return true;
            }
            catch (Exception e)
            {
                // ItemManager marks rows clean after the SQL statement succeeds, before the
                // transaction commit. Re-mark them when the transaction fails so a later save can
                // retry instead of silently treating rolled-back data as durable.
                ItemManager.Instance.MarkOwnerItemsDirty(ownerId);
                Logger.Error(e,
                    "Failed to persist owner item state: owner={0}, reason={1}",
                    ownerId, reason ?? string.Empty);
                return false;
            }
        }
    }

    /// <summary>Persists one character immediately, including return_district and portal book.</summary>
    public bool SaveCharacter(Character character, string reason)
    {
        if (character == null || character.Id == 0)
            return false;

        lock (_lock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var transaction = connection.BeginTransaction();
                if (!character.Save(connection, transaction))
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                Logger.Info("Persisted character state: character={0}, reason={1}",
                    character.Id, reason ?? string.Empty);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to persist character={0}, reason={1}",
                    character.Id, reason ?? string.Empty);
                return false;
            }
        }
    }

    public void SaveTick()
    {
        if (!_enabled)
        {
            Logger.Warn("Auto-Saving disabled, skipping ...");
            return;
        }
        DoSave();
    }

    public void SetAutoSaveInterval()
    {
        Delay = AppConfiguration.Instance.World.AutoSaveInterval;
    }
}
