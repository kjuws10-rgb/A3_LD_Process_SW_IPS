using Drilling.Common.Interface;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Managers;
using Drilling.Common.Motion;
using Drilling.Common.Station;
using System.IO;

namespace Drilling.Common.Managers;

public enum EN_SETTING_TAB
{
    Option,
    Interface,
    Io,
    Motor,
    Alarm
}

public sealed record ST_SYSTEM_PARAMETER(
    EN_SETTING_TAB Section,
    string Name,
    string Value,
    string Unit,
    string Description,
    string Group = "",
    string Key = "",
    string DefaultValue = "",
    EN_RECIPE_DATA_TYPE DataType = EN_RECIPE_DATA_TYPE.String,
    double Min = 0.0,
    double Max = 0.0,
    bool Show = true,
    bool Use = true,
    int DisplayOrder = 0,
    IReadOnlyDictionary<string, string>? Extra = null);

public sealed record ST_SETTING_HISTORY(
    DateTimeOffset ChangedAt,
    EN_SETTING_TAB Section,
    string ParameterName,
    string OldValue,
    string NewValue,
    string OperatorId,
    string Action);

public interface ISettingFile
{
    Task<IReadOnlyList<ST_SYSTEM_PARAMETER>> Load(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default);

    Task Save(
        EN_SETTING_TAB section,
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_SETTING_HISTORY>> LoadHistory(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default);
}

public interface IInterfaceFile
{
    Task<IReadOnlyList<ST_INTERFACE_DATA>> LoadAll(CancellationToken cancellationToken = default);

    Task SaveAll(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        CancellationToken cancellationToken = default);
}
public interface ISettingManager
{
    Task<IReadOnlyList<ST_SYSTEM_PARAMETER>> LoadSection(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default);

    Task<string> GetValue(
        EN_SETTING_TAB section,
        string name,
        string defaultValue = "",
        CancellationToken cancellationToken = default);

    Task SetValue(
        EN_SETTING_TAB section,
        string name,
        string value,
        CancellationToken cancellationToken = default);

    Task SaveSection(
        EN_SETTING_TAB section,
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_SETTING_HISTORY>> LoadHistory(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ST_INTERFACE_DATA>> LoadInterfaceList(
        CancellationToken cancellationToken = default);

    Task SaveInterfaceList(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        CancellationToken cancellationToken = default);

    Task ConnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default);

    Task DisconnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default);

    Task ReconnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default);
}

public sealed class CSettingManager(
    ISettingFile settingFile,
    IInterfaceFile interfaceFile,
    IInterfaceManager interfaceManager) : ISettingManager
{
    public Task<IReadOnlyList<ST_SYSTEM_PARAMETER>> LoadSection(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default)
    {
        return settingFile.Load(section, cancellationToken);
    }

    public async Task<string> GetValue(
        EN_SETTING_TAB section,
        string name,
        string defaultValue = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var parameters = await LoadSection(section, cancellationToken);
        bool MatchItem1(ST_SYSTEM_PARAMETER item)
        {
            return item.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        var parameter = parameters.FirstOrDefault(MatchItem1);

        return parameter is null || string.IsNullOrWhiteSpace(parameter.Value)
            ? defaultValue
            : parameter.Value;
    }

    public async Task SetValue(
        EN_SETTING_TAB section,
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var parameters = await LoadSection(section, cancellationToken);
        var found = false;
        ST_SYSTEM_PARAMETER SelectParameter2(ST_SYSTEM_PARAMETER parameter)
        {
            var isTarget =
                parameter.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

            if (!isTarget)
            {
                return parameter;
            }

            found = true;
            return parameter with { Value = value };
        }
        var editedParameters = parameters
            .Select(SelectParameter2)
            .ToArray();

        if (!found)
        {
            throw new InvalidDataException($"Setting parameter is not defined: {section}/{name}");
        }

        await SaveSection(section, editedParameters, cancellationToken);
    }

    public Task SaveSection(
        EN_SETTING_TAB section,
        IReadOnlyList<ST_SYSTEM_PARAMETER> parameters,
        CancellationToken cancellationToken = default)
    {
        return settingFile.Save(section, parameters, cancellationToken);
    }

    public Task<IReadOnlyList<ST_SETTING_HISTORY>> LoadHistory(
        EN_SETTING_TAB section,
        CancellationToken cancellationToken = default)
    {
        return settingFile.LoadHistory(section, cancellationToken);
    }

    public Task<IReadOnlyList<ST_INTERFACE_DATA>> LoadInterfaceList(
        CancellationToken cancellationToken = default)
    {
        return interfaceFile.LoadAll(cancellationToken);
    }

    public async Task SaveInterfaceList(
        IReadOnlyList<ST_INTERFACE_DATA> interfaces,
        CancellationToken cancellationToken = default)
    {
        await interfaceFile.SaveAll(interfaces, cancellationToken);
        await interfaceManager.Reload(interfaces, reconnect: false, cancellationToken);
    }

    public Task ConnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        return interfaceManager.Connect(module, number, cancellationToken: cancellationToken);
    }

    public Task DisconnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        return interfaceManager.Disconnect(module, number, cancellationToken);
    }

    public Task ReconnectInterface(
        EN_EQP_MODULE module,
        int number,
        CancellationToken cancellationToken = default)
    {
        return interfaceManager.Reconnect(module, number, cancellationToken);
    }
}

