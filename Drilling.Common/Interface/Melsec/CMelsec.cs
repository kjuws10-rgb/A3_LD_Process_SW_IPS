using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Drilling.Common.Log;

namespace Drilling.Common.Interface;

public enum EN_MELSEC_DATA_TYPE
{
    Bit,
    Word,
    DWord,
    Double,
    Float,
    String
}

public enum EN_MELSEC_DIRECTION
{
    In,
    Out,
    InOut
}

public enum EN_MELSEC_ACCESS
{
    Read,
    Write,
    ReadWrite
}

public sealed record ST_MELSEC_MAP_DATA(
    string Id,
    bool Use,
    string Group,
    string Name,
    int DeviceNo,
    string Address,
    EN_MELSEC_DATA_TYPE DataType,
    EN_MELSEC_DIRECTION Direction,
    EN_MELSEC_ACCESS Access,
    double Scale,
    int Length,
    int PollMs,
    string Description);

public interface IMelsecMapFile
{
    Task<IReadOnlyList<ST_MELSEC_MAP_DATA>> LoadAll(CancellationToken cancellationToken = default);
}

public interface IMelsec
{
    IReadOnlyList<ST_MELSEC_MAP_DATA> Map { get; }

    void ReloadMap(IReadOnlyList<ST_MELSEC_MAP_DATA> map);

    IReadOnlyList<ST_MELSEC_MAP_DATA> GetMapList(string group = "");

    ST_MELSEC_MAP_DATA GetMapData(string id);

    Task<bool> ReadBit(string id, CancellationToken cancellationToken = default);

    Task WriteBit(string id, bool value, CancellationToken cancellationToken = default);

    Task<int> ReadWord(string id, CancellationToken cancellationToken = default);

    Task WriteWord(string id, int value, CancellationToken cancellationToken = default);

    Task<double> ReadDouble(string id, CancellationToken cancellationToken = default);

    Task WriteDouble(string id, double value, CancellationToken cancellationToken = default);

    Task<string> ReadString(string id, CancellationToken cancellationToken = default);

    Task WriteString(string id, string value, CancellationToken cancellationToken = default);
}

public sealed class CMelsec : IMelsec, IDisposable
{
    private const ushort McCommandBatchRead = 0x0401;
    private const ushort McCommandBatchWrite = 0x1401;
    private const ushort McSubCommandWord = 0x0000;
    private const ushort DefaultMonitoringTimer = 0x0010;
    private const int DefaultConnectTimeoutMs = 700;

    private readonly IInterfaceManager _interfaceManager;
    private readonly ILogManager? _logManager;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly Dictionary<string, ushort> _simulationWords = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, ST_MELSEC_MAP_DATA> _map = new(StringComparer.OrdinalIgnoreCase);
    private TcpClient? _client;
    private int? _connectedDeviceNo;
    private string _connectedEndpoint = "";

    public CMelsec(
        IInterfaceManager interfaceManager,
        ILogManager? logManager = null,
        IReadOnlyList<ST_MELSEC_MAP_DATA>? map = null)
    {
        _interfaceManager = interfaceManager;
        _logManager = logManager;
        ReloadMap(map ?? []);
    }

    public IReadOnlyList<ST_MELSEC_MAP_DATA> Map
    {
        get
        {
            return _map.Values
        .OrderBy(data => data.Group, StringComparer.OrdinalIgnoreCase)
        .ThenBy(data => data.DeviceNo)
        .ThenBy(data => data.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();
        }
    }

    public void ReloadMap(IReadOnlyList<ST_MELSEC_MAP_DATA> map)
    {
        _map = map
            .Where(data => data.Use)
            .ToDictionary(data => NormalizeId(data.Id), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ST_MELSEC_MAP_DATA> GetMapList(string group = "")
    {
        var normalizedGroup = group.Trim();

        return Map
            .Where(data => string.IsNullOrWhiteSpace(normalizedGroup) ||
                data.Group.Equals(normalizedGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public ST_MELSEC_MAP_DATA GetMapData(string id)
    {
        var normalizedId = NormalizeId(id);

        if (_map.TryGetValue(normalizedId, out var data))
        {
            return data;
        }

        throw new InvalidOperationException(
            $"MELSEC map was not registered: {id}. Available={string.Join(", ", _map.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}");
    }

    public async Task<bool> ReadBit(string id, CancellationToken cancellationToken = default)
    {
        var data = PrepareRead(id, EN_MELSEC_DATA_TYPE.Bit, cancellationToken);
        var address = ParseAddress(data.Address);
        var bitIndex = RequireWordBit(address, data);
        var words = await ReadWords(data, address, 1, cancellationToken);

        return (words[0] & (1 << bitIndex)) != 0;
    }

    public async Task WriteBit(string id, bool value, CancellationToken cancellationToken = default)
    {
        var data = PrepareWrite(id, EN_MELSEC_DATA_TYPE.Bit, cancellationToken);
        var address = ParseAddress(data.Address);
        var bitIndex = RequireWordBit(address, data);
        var words = await ReadWords(data, address, 1, cancellationToken);
        var mask = (ushort)(1 << bitIndex);
        words[0] = value
            ? (ushort)(words[0] | mask)
            : (ushort)(words[0] & ~mask);

        await WriteWords(data, address, words, cancellationToken);
    }

    public async Task<int> ReadWord(string id, CancellationToken cancellationToken = default)
    {
        var data = PrepareRead(id, [EN_MELSEC_DATA_TYPE.Word, EN_MELSEC_DATA_TYPE.DWord], cancellationToken);
        var wordCount = data.DataType == EN_MELSEC_DATA_TYPE.DWord ? Math.Max(2, data.Length) : 1;
        var words = await ReadWords(data, ParseAddress(data.Address), wordCount, cancellationToken);

        return data.DataType == EN_MELSEC_DATA_TYPE.DWord
            ? WordsToInt32(words)
            : words[0];
    }

    public async Task WriteWord(string id, int value, CancellationToken cancellationToken = default)
    {
        var data = PrepareWrite(id, [EN_MELSEC_DATA_TYPE.Word, EN_MELSEC_DATA_TYPE.DWord], cancellationToken);
        var wordCount = data.DataType == EN_MELSEC_DATA_TYPE.DWord ? Math.Max(2, data.Length) : 1;
        var words = data.DataType == EN_MELSEC_DATA_TYPE.DWord
            ? Int32ToWords(value, wordCount)
            : [(ushort)value];

        await WriteWords(data, ParseAddress(data.Address), words, cancellationToken);
    }

    public async Task<double> ReadDouble(string id, CancellationToken cancellationToken = default)
    {
        var data = PrepareRead(id, [EN_MELSEC_DATA_TYPE.Double, EN_MELSEC_DATA_TYPE.Float], cancellationToken);
        var wordCount = Math.Max(2, data.Length);
        var words = await ReadWords(data, ParseAddress(data.Address), wordCount, cancellationToken);

        if (data.DataType == EN_MELSEC_DATA_TYPE.Float)
        {
            return WordsToFloat(words) * ReadScale(data);
        }

        return WordsToInt32(words) * ReadScale(data);
    }

    public async Task WriteDouble(string id, double value, CancellationToken cancellationToken = default)
    {
        var data = PrepareWrite(id, [EN_MELSEC_DATA_TYPE.Double, EN_MELSEC_DATA_TYPE.Float], cancellationToken);
        var wordCount = Math.Max(2, data.Length);
        var rawValue = value / ReadScale(data);
        var words = data.DataType == EN_MELSEC_DATA_TYPE.Float
            ? FloatToWords((float)rawValue, wordCount)
            : Int32ToWords((int)Math.Round(rawValue, MidpointRounding.AwayFromZero), wordCount);

        await WriteWords(data, ParseAddress(data.Address), words, cancellationToken);
    }

    public async Task<string> ReadString(string id, CancellationToken cancellationToken = default)
    {
        var data = PrepareRead(id, EN_MELSEC_DATA_TYPE.String, cancellationToken);
        var words = await ReadWords(data, ParseAddress(data.Address), data.Length, cancellationToken);
        var bytes = new byte[words.Length * 2];

        for (var i = 0; i < words.Length; i++)
        {
            bytes[i * 2] = (byte)(words[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(words[i] >> 8);
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }

    public async Task WriteString(string id, string value, CancellationToken cancellationToken = default)
    {
        var data = PrepareWrite(id, EN_MELSEC_DATA_TYPE.String, cancellationToken);
        var byteLength = data.Length * 2;
        var sourceBytes = Encoding.ASCII.GetBytes(value);
        var bytes = Enumerable.Repeat((byte)' ', byteLength).ToArray();
        Array.Copy(sourceBytes, bytes, Math.Min(sourceBytes.Length, bytes.Length));

        var words = new ushort[data.Length];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        }

        await WriteWords(data, ParseAddress(data.Address), words, cancellationToken);
    }

    public void Dispose()
    {
        DisconnectSocket();
        _ioLock.Dispose();
    }

    private async Task<ushort[]> ReadWords(
        ST_MELSEC_MAP_DATA data,
        ST_MELSEC_ADDRESS address,
        int wordCount,
        CancellationToken cancellationToken)
    {
        if (wordCount <= 0 || wordCount > ushort.MaxValue)
        {
            throw new InvalidOperationException($"MELSEC word count is invalid: {FormatMap(data)}, Count={wordCount}");
        }

        var interfaceData = GetInterfaceData(data);
        var command = CreateCommand("READ_WORD", data, wordCount.ToString(CultureInfo.InvariantCulture));

        if (IsSimulation(interfaceData))
        {
            var simulationWords = ReadSimulationWords(address, wordCount);
            WriteCommandLog(data, interfaceData, command, $"SIMULATION OK / {WordsToHexText(simulationWords)}");
            return simulationWords;
        }

        var option = CInterfaceConnectOption.Parse(interfaceData);
        var mcOption = ReadMcProtocolOption(interfaceData);
        var request = BuildMcRequest(mcOption, McCommandBatchRead, McSubCommandWord, address, (ushort)wordCount, []);
        var responseData = await SendMcRequest(data, interfaceData, option, request, command, cancellationToken);

        if (responseData.Length < wordCount * 2)
        {
            throw new IOException(
                $"MELSEC response is shorter than requested. {FormatMap(data)}, ExpectedBytes={wordCount * 2}, ActualBytes={responseData.Length}");
        }

        var words = new ushort[wordCount];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(responseData.AsSpan(i * 2, 2));
        }

        return words;
    }

    private async Task WriteWords(
        ST_MELSEC_MAP_DATA data,
        ST_MELSEC_ADDRESS address,
        IReadOnlyList<ushort> words,
        CancellationToken cancellationToken)
    {
        if (words.Count <= 0 || words.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"MELSEC word count is invalid: {FormatMap(data)}, Count={words.Count}");
        }

        var interfaceData = GetInterfaceData(data);
        var command = CreateCommand("WRITE_WORD", data, WordsToHexText(words));

        if (IsSimulation(interfaceData))
        {
            WriteSimulationWords(address, words);
            WriteCommandLog(data, interfaceData, command, "SIMULATION OK");
            return;
        }

        var option = CInterfaceConnectOption.Parse(interfaceData);
        var mcOption = ReadMcProtocolOption(interfaceData);
        var request = BuildMcRequest(mcOption, McCommandBatchWrite, McSubCommandWord, address, (ushort)words.Count, words);
        await SendMcRequest(data, interfaceData, option, request, command, cancellationToken);
    }

    private async Task<byte[]> SendMcRequest(
        ST_MELSEC_MAP_DATA data,
        ST_INTERFACE_DATA interfaceData,
        ST_INTERFACE_CONNECT_OPTION option,
        byte[] request,
        string command,
        CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (option.TimeoutMs > 0)
            {
                timeoutCts.CancelAfter(option.TimeoutMs);
            }

            var stream = await EnsureConnected(interfaceData, option, timeoutCts.Token);
            await stream.WriteAsync(request, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            var header = await ReadExact(stream, 9, timeoutCts.Token);
            var bodyLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7, 2));
            var body = await ReadExact(stream, bodyLength, timeoutCts.Token);

            ValidateMcResponse(data, header, body);
            var responseData = body.Skip(2).ToArray();
            WriteCommandLog(data, interfaceData, command, $"OK / {responseData.Length} bytes");

            return responseData;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            DisconnectSocket();
            var message = $"MELSEC command timed out after {option.TimeoutMs} ms.";
            WriteErrorLog(data, interfaceData, command, message);
            throw new TimeoutException(message, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DisconnectSocket();
            WriteErrorLog(data, interfaceData, command, ex.Message);
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<NetworkStream> EnsureConnected(
        ST_INTERFACE_DATA interfaceData,
        ST_INTERFACE_CONNECT_OPTION option,
        CancellationToken cancellationToken)
    {
        if (option.Port <= 0)
        {
            throw new InvalidOperationException($"MELSEC port is not configured: {interfaceData.NickName}");
        }

        var endpoint = $"{option.RemoteAddress}:{option.Port}";
        if (_client?.Connected == true &&
            _connectedDeviceNo == interfaceData.Number &&
            _connectedEndpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return _client.GetStream();
        }

        DisconnectSocket();

        var client = new TcpClient
        {
            NoDelay = true
        };

        var connectTimeoutMs = option.TimeoutMs > 0
            ? Math.Min(option.TimeoutMs, DefaultConnectTimeoutMs)
            : DefaultConnectTimeoutMs;
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(connectTimeoutMs);

        try
        {
            await client.ConnectAsync(option.RemoteAddress, option.Port, connectCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"MELSEC connection timed out after {connectTimeoutMs} ms: {endpoint}", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _connectedDeviceNo = interfaceData.Number;
        _connectedEndpoint = endpoint;

        return client.GetStream();
    }

    private void DisconnectSocket()
    {
        try
        {
            _client?.Close();
            _client?.Dispose();
        }
        finally
        {
            _client = null;
            _connectedDeviceNo = null;
            _connectedEndpoint = "";
        }
    }

    private ST_INTERFACE_DATA GetInterfaceData(ST_MELSEC_MAP_DATA data)
    {
        return _interfaceManager.GetInterfaceData(EN_EQP_MODULE.Melsec, data.DeviceNo)
            ?? throw new InvalidOperationException($"MELSEC interface is not configured: MELSEC_{data.DeviceNo}");
    }

    private bool IsSimulation(ST_INTERFACE_DATA data)
    {
        return data.IsSimulation || _interfaceManager.IsSimul(EN_EQP_MODULE.Melsec, data.Number);
    }

    private ushort[] ReadSimulationWords(ST_MELSEC_ADDRESS address, int wordCount)
    {
        var words = new ushort[wordCount];

        for (var i = 0; i < words.Length; i++)
        {
            _simulationWords.TryGetValue(CreateSimulationKey(address, i), out words[i]);
        }

        return words;
    }

    private void WriteSimulationWords(
        ST_MELSEC_ADDRESS address,
        IReadOnlyList<ushort> words)
    {
        for (var i = 0; i < words.Count; i++)
        {
            _simulationWords[CreateSimulationKey(address, i)] = words[i];
        }
    }

    private static string CreateSimulationKey(ST_MELSEC_ADDRESS address, int wordOffset)
    {
        return $"{address.Device}:{address.Number + wordOffset:X}";
    }

    private static byte[] BuildMcRequest(
        ST_MC_PROTOCOL_OPTION option,
        ushort command,
        ushort subCommand,
        ST_MELSEC_ADDRESS address,
        ushort points,
        IReadOnlyList<ushort> writeWords)
    {
        var bodyLength = 12 + writeWords.Count * 2;
        var request = new byte[9 + bodyLength];

        request[0] = 0x50;
        request[1] = 0x00;
        request[2] = option.NetworkNo;
        request[3] = option.PcNo;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4, 2), option.IoNo);
        request[6] = option.StationNo;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(7, 2), (ushort)bodyLength);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(9, 2), option.MonitoringTimer);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(11, 2), command);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(13, 2), subCommand);

        request[15] = (byte)(address.Number & 0xFF);
        request[16] = (byte)((address.Number >> 8) & 0xFF);
        request[17] = (byte)((address.Number >> 16) & 0xFF);
        request[18] = address.DeviceCode;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(19, 2), points);

        for (var i = 0; i < writeWords.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(21 + i * 2, 2), writeWords[i]);
        }

        return request;
    }

    private static async Task<byte[]> ReadExact(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var readCount = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (readCount <= 0)
            {
                throw new IOException("MELSEC connection was closed by remote host.");
            }

            offset += readCount;
        }

        return buffer;
    }

    private static void ValidateMcResponse(
        ST_MELSEC_MAP_DATA data,
        byte[] header,
        byte[] body)
    {
        if (header.Length < 9 || header[0] != 0xD0 || header[1] != 0x00)
        {
            throw new IOException($"MELSEC response header is invalid: {FormatMap(data)}");
        }

        if (body.Length < 2)
        {
            throw new IOException($"MELSEC response body is invalid: {FormatMap(data)}");
        }

        var endCode = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2));
        if (endCode != 0)
        {
            throw new IOException($"MELSEC returned error end code 0x{endCode:X4}: {FormatMap(data)}");
        }
    }

    private ST_MELSEC_MAP_DATA PrepareRead(
        string id,
        EN_MELSEC_DATA_TYPE dataType,
        CancellationToken cancellationToken)
    {
        return PrepareRead(id, [dataType], cancellationToken);
    }

    private ST_MELSEC_MAP_DATA PrepareRead(
        string id,
        IReadOnlyList<EN_MELSEC_DATA_TYPE> dataTypes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = GetMapData(id);
        EnsureDataType(data, dataTypes);

        if (data.Access == EN_MELSEC_ACCESS.Write)
        {
            throw new InvalidOperationException($"MELSEC map is write only: {FormatMap(data)}");
        }

        return data;
    }

    private ST_MELSEC_MAP_DATA PrepareWrite(
        string id,
        EN_MELSEC_DATA_TYPE dataType,
        CancellationToken cancellationToken)
    {
        return PrepareWrite(id, [dataType], cancellationToken);
    }

    private ST_MELSEC_MAP_DATA PrepareWrite(
        string id,
        IReadOnlyList<EN_MELSEC_DATA_TYPE> dataTypes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = GetMapData(id);
        EnsureDataType(data, dataTypes);

        if (data.Access == EN_MELSEC_ACCESS.Read)
        {
            throw new InvalidOperationException($"MELSEC map is read only: {FormatMap(data)}");
        }

        return data;
    }

    private static void EnsureDataType(
        ST_MELSEC_MAP_DATA data,
        IReadOnlyList<EN_MELSEC_DATA_TYPE> dataTypes)
    {
        if (dataTypes.Contains(data.DataType))
        {
            return;
        }

        throw new InvalidOperationException(
            $"MELSEC map data type mismatch: {FormatMap(data)}. Expected={string.Join("/", dataTypes)}");
    }

    private static ST_MELSEC_ADDRESS ParseAddress(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        var bitIndex = (int?)null;
        var bitSeparatorIndex = text.IndexOf('.', StringComparison.Ordinal);

        if (bitSeparatorIndex >= 0)
        {
            var bitText = text[(bitSeparatorIndex + 1)..];
            if (string.IsNullOrWhiteSpace(bitText) ||
                !int.TryParse(bitText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedBit) ||
                parsedBit < 0 ||
                parsedBit > 15)
            {
                throw new FormatException($"MELSEC bit index is invalid: {value}");
            }

            bitIndex = parsedBit;
            text = text[..bitSeparatorIndex];
        }

        var digitIndex = text.TakeWhile(char.IsLetter).Count();
        if (digitIndex <= 0 || digitIndex >= text.Length)
        {
            throw new FormatException($"MELSEC address is invalid: {value}");
        }

        var device = text[..digitIndex];
        var numberText = text[digitIndex..];
        var number = ParseDeviceNumber(device, numberText);

        return new ST_MELSEC_ADDRESS(device, number, bitIndex, GetDeviceCode(device));
    }

    private static int ParseDeviceNumber(string device, string value)
    {
        if (value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            return int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        if (value.EndsWith("H", StringComparison.OrdinalIgnoreCase))
        {
            return int.Parse(value[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        var style = IsHexDevice(device) ? NumberStyles.HexNumber : NumberStyles.Integer;
        return int.Parse(value, style, CultureInfo.InvariantCulture);
    }

    private static bool IsHexDevice(string device)
    {
        return device is "X" or "Y" or "B" or "W" or "SB" or "SW";
    }

    private static byte GetDeviceCode(string device)
    {
        return device switch
        {
            "M" => 0x90,
            "SM" => 0x91,
            "L" => 0x92,
            "F" => 0x93,
            "V" => 0x94,
            "X" => 0x9C,
            "Y" => 0x9D,
            "B" => 0xA0,
            "SB" => 0xA1,
            "D" => 0xA8,
            "SD" => 0xA9,
            "R" => 0xAF,
            "ZR" => 0xB0,
            "W" => 0xB4,
            "SW" => 0xB5,
            _ => throw new NotSupportedException($"MELSEC device is not supported: {device}")
        };
    }

    private static int RequireWordBit(
        ST_MELSEC_ADDRESS address,
        ST_MELSEC_MAP_DATA data)
    {
        if (address.BitIndex is { } bitIndex)
        {
            return bitIndex;
        }

        throw new InvalidOperationException($"MELSEC BIT address must include bit index such as W23458.0: {FormatMap(data)}");
    }

    private static int WordsToInt32(IReadOnlyList<ushort> words)
    {
        if (words.Count <= 1)
        {
            return words.Count == 0 ? 0 : words[0];
        }

        return unchecked((int)(words[0] | ((uint)words[1] << 16)));
    }

    private static ushort[] Int32ToWords(int value, int wordCount)
    {
        var words = new ushort[wordCount];
        words[0] = (ushort)(value & 0xFFFF);

        if (wordCount > 1)
        {
            words[1] = (ushort)((value >> 16) & 0xFFFF);
        }

        return words;
    }

    private static float WordsToFloat(IReadOnlyList<ushort> words)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0, 2), words.Count > 0 ? words[0] : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), words.Count > 1 ? words[1] : (ushort)0);

        return BitConverter.ToSingle(bytes, 0);
    }

    private static ushort[] FloatToWords(float value, int wordCount)
    {
        var bytes = BitConverter.GetBytes(value);
        var words = new ushort[wordCount];
        words[0] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));

        if (wordCount > 1)
        {
            words[1] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
        }

        return words;
    }

    private static double ReadScale(ST_MELSEC_MAP_DATA data)
    {
        return Math.Abs(data.Scale) < double.Epsilon ? 1.0 : data.Scale;
    }

    private static ST_MC_PROTOCOL_OPTION ReadMcProtocolOption(ST_INTERFACE_DATA data)
    {
        var options = ReadExtraOptions(data);

        return new ST_MC_PROTOCOL_OPTION(
            ReadByteOption(options, 0x00, "MC_NETWORK_NO", "NETWORK_NO", "NETWORK"),
            ReadByteOption(options, 0xFF, "MC_PC_NO", "PC_NO", "PC"),
            ReadUShortOption(options, 0x03FF, "MC_IO_NO", "IO_NO", "MODULE_IO_NO", "DEST_IO_NO"),
            ReadByteOption(options, 0x00, "MC_STATION_NO", "STATION_NO", "STATION"),
            ReadUShortOption(options, DefaultMonitoringTimer, "MC_TIMER", "MONITORING_TIMER", "TIMER"));
    }

    private static IReadOnlyDictionary<string, string> ReadExtraOptions(ST_INTERFACE_DATA data)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (data.Extra is not null)
        {
            foreach (var pair in data.Extra)
            {
                options[pair.Key] = pair.Value;
                ParseKeyValueOptions(options, pair.Value);
            }
        }

        return options;
    }

    private static void ParseKeyValueOptions(
        IDictionary<string, string> options,
        string value)
    {
        foreach (var token in value.Split([';', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = token.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            options[token[..index].Trim()] = token[(index + 1)..].Trim();
        }
    }

    private static byte ReadByteOption(
        IReadOnlyDictionary<string, string> options,
        byte defaultValue,
        params string[] names)
    {
        var value = ReadUShortOption(options, defaultValue, names);
        return value > byte.MaxValue ? defaultValue : (byte)value;
    }

    private static ushort ReadUShortOption(
        IReadOnlyDictionary<string, string> options,
        ushort defaultValue,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (!options.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            return ParseUShortOption(text, defaultValue);
        }

        return defaultValue;
    }

    private static ushort ParseUShortOption(string value, ushort defaultValue)
    {
        var text = value.Trim();

        if (text.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue)
                ? hexValue
                : defaultValue;
        }

        if (text.EndsWith("H", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(text[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue)
                ? hexValue
                : defaultValue;
        }

        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue)
            ? decimalValue
            : defaultValue;
    }

    private void WriteCommandLog(
        ST_MELSEC_MAP_DATA data,
        ST_INTERFACE_DATA interfaceData,
        string command,
        string response)
    {
        _logManager?.WriteInterfaceCommand(
            EN_EQP_MODULE.Melsec,
            interfaceData.NickName,
            command,
            response,
            FormatMap(data));
    }

    private void WriteErrorLog(
        ST_MELSEC_MAP_DATA data,
        ST_INTERFACE_DATA interfaceData,
        string command,
        string detail)
    {
        _logManager?.WriteInterfaceError(
            EN_EQP_MODULE.Melsec,
            interfaceData.NickName,
            command,
            $"{FormatMap(data)} / {detail}");
    }

    private static string CreateCommand(
        string operation,
        ST_MELSEC_MAP_DATA data,
        string value = "")
    {
        var fields = new[]
        {
            "MELSEC",
            operation,
            data.Id,
            data.Address,
            data.DataType.ToString().ToUpperInvariant(),
            data.Scale.ToString(CultureInfo.InvariantCulture),
            data.Length.ToString(CultureInfo.InvariantCulture),
            value
        };

        return string.Join(":", fields);
    }

    private static string WordsToHexText(IReadOnlyList<ushort> words)
    {
        return string.Join(" ", words.Select(word => word.ToString("X4", CultureInfo.InvariantCulture)));
    }

    private static string FormatMap(ST_MELSEC_MAP_DATA data)
    {
        return $"{data.Id}({data.Address}, {data.DataType}, MELSEC_{data.DeviceNo})";
    }

    private static string NormalizeId(string id)
    {
        return id.Trim().ToUpperInvariant();
    }

    private sealed record ST_MELSEC_ADDRESS(
        string Device,
        int Number,
        int? BitIndex,
        byte DeviceCode);

    private sealed record ST_MC_PROTOCOL_OPTION(
        byte NetworkNo,
        byte PcNo,
        ushort IoNo,
        byte StationNo,
        ushort MonitoringTimer);
}
