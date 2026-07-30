using System;
using System.Collections.Generic;
using System.Globalization;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Utils.DB;
using Jace;
using Jace.Execution;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FormulaManager : Singleton<FormulaManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static bool _loaded = false;

    private Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>> _unitFormulas;
    private Dictionary<WearableFormulaType, WearableFormula> _wearableFormulas;
    private Dictionary<uint, Formula> _formulas;

    private Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>
        _unitVariables;

    public CalculationEngine CalculationEngine { get; private set; }

    private static void RegisterOptionalFunction(
        CalculationEngine engine,
        string name,
        Func<double, double> function)
    {
        try
        {
            engine.AddFunction(name, function);
        }
        catch (Exception ex) when (ex.Message.Contains("cannot be overwrit", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Debug("Formula function {0} is already registered by Jace; keeping the existing implementation", name);
        }
    }

    private static void RegisterOptionalFunction(
        CalculationEngine engine,
        string name,
        Func<double, double, double> function)
    {
        try
        {
            engine.AddFunction(name, function);
        }
        catch (Exception ex) when (ex.Message.Contains("cannot be overwrit", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Debug("Formula function {0} is already registered by Jace; keeping the existing implementation", name);
        }
    }

    private static void RegisterOptionalFunction(
        CalculationEngine engine,
        string name,
        Func<double, double, double, double> function)
    {
        try
        {
            engine.AddFunction(name, function);
        }
        catch (Exception ex) when (ex.Message.Contains("cannot be overwrit", StringComparison.OrdinalIgnoreCase))
        {
            // Jace changes its built-in function set between versions. Re-registering a
            // built-in must not abort the entire game-server startup.
            Logger.Debug("Formula function {0} is already registered by Jace; keeping the existing implementation", name);
        }
    }

    public UnitFormula GetUnitFormula(FormulaOwnerType owner, UnitFormulaKind kind)
    {
        if (_unitFormulas.TryGetValue(owner, out var value)
            && value.TryGetValue(kind, out var kindFound))
            return kindFound;

        return null;
    }

    public float GetUnitVariable(uint formulaId, UnitFormulaVariableType type, uint key)
    {
        if (_unitVariables.TryGetValue(formulaId, out var unitFormulas)
            && unitFormulas.TryGetValue(type, out var formulaVariables)
            && formulaVariables.TryGetValue(key, out var formulaVariable))
            return formulaVariable.Value;

        return 0f;
    }

    public WearableFormula GetWearableFormula(WearableFormulaType type)
    {
        return _wearableFormulas.TryGetValue(type, out var value) ? value : null;
    }

    public Formula GetFormula(uint id)
    {
        return _formulas.TryGetValue(id, out var value) ? value : null;
    }

    public void Load()
    {
        if (_loaded)
            return;

        CalculationEngine = new(new JaceOptions
        {
            CacheEnabled = true,
            OptimizerEnabled = true,
            CaseSensitive = true,
            ExecutionMode = ExecutionMode.Compiled,
            CultureInfo = CultureInfo.InvariantCulture,
        });
        // Register every function used by the client database. Registration is
        // idempotent: if this Jace build already provides one, its implementation wins.
        RegisterOptionalFunction(CalculationEngine, "min", (left, right) => Math.Min(left, right));
        RegisterOptionalFunction(CalculationEngine, "max", (left, right) => Math.Max(left, right));
        RegisterOptionalFunction(CalculationEngine, "floor", value => Math.Floor(value));
        RegisterOptionalFunction(CalculationEngine, "sqrt", value => Math.Sqrt(value));
        RegisterOptionalFunction(CalculationEngine, "abs", value => Math.Abs(value));
        RegisterOptionalFunction(CalculationEngine, "log", value => Math.Log(value));
        RegisterOptionalFunction(CalculationEngine, "clamp",
            (value, minimum, maximum) => Math.Clamp(value, minimum, maximum));
        RegisterOptionalFunction(CalculationEngine, "if_negative",
            (value, whenNegative, otherwise) => value < 0 ? whenNegative : otherwise);
        RegisterOptionalFunction(CalculationEngine, "if_positive",
            (value, whenPositive, otherwise) => value > 0 ? whenPositive : otherwise);
        RegisterOptionalFunction(CalculationEngine, "if_zero",
            (value, whenZero, otherwise) => value == 0 ? whenZero : otherwise);

        _unitFormulas = new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>();
        foreach (var owner in Enum.GetValues(typeof(FormulaOwnerType)))
            _unitFormulas.Add((FormulaOwnerType)owner, new Dictionary<UnitFormulaKind, UnitFormula>());
        _wearableFormulas = new Dictionary<WearableFormulaType, WearableFormula>();
        _unitVariables =
            new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();
        _formulas = new Dictionary<uint, Formula>();

        using (var connection = SQLite.CreateSkillConnection())
        {
            Logger.Info("Loading formulas...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from unit_formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new UnitFormula
                        {
                            Id = reader.GetUInt32("id"),
                            TextFormula = reader.GetString("formula"),
                            Kind = (UnitFormulaKind)reader.GetByte("kind_id"),
                            Owner = (FormulaOwnerType)reader.GetByte("owner_type_id")
                        };
                        if (!formula.Prepare())
                            continue;

                        if (!_unitFormulas.TryGetValue(formula.Owner, out var ownerFormulas))
                        {
                            ownerFormulas = new Dictionary<UnitFormulaKind, UnitFormula>();
                            _unitFormulas.Add(formula.Owner, ownerFormulas);
                            Logger.Info("Discovered formula owner type {0} in the client database", (byte)formula.Owner);
                        }

                        ownerFormulas[formula.Kind] = formula;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from unit_formula_variables";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var variable = new UnitFormulaVariable
                        {
                            FormulaId = reader.GetUInt32("unit_formula_id"),
                            Type = (UnitFormulaVariableType)reader.GetByte("variable_kind_id"),
                            Key = reader.GetUInt32("key"),
                            Value = reader.GetFloat("value")
                        };
                        if (!_unitVariables.TryGetValue(variable.FormulaId, out var variablesByType))
                        {
                            variablesByType = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>();
                            _unitVariables.Add(variable.FormulaId, variablesByType);
                        }

                        if (!variablesByType.TryGetValue(variable.Type, out var variablesByKey))
                        {
                            variablesByKey = new Dictionary<uint, UnitFormulaVariable>();
                            variablesByType.Add(variable.Type, variablesByKey);
                        }

                        variablesByKey[variable.Key] = variable;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from wearable_formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new WearableFormula
                        {
                            //formula.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                            Type = (WearableFormulaType)reader.GetByte("kind_id"),
                            TextFormula = reader.GetString("formula")
                        };
                        if (formula.Prepare())
                            _wearableFormulas[formula.Type] = formula;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new Formula
                        {
                            Id = reader.GetUInt32("id"),
                            TextFormula = reader.GetString("formula")
                        };
                        if (formula.Prepare())
                            _formulas[formula.Id] = formula;
                    }
                }
            }

            Logger.Info("Formulas loaded");
        }
        _loaded = true;
    }
}
