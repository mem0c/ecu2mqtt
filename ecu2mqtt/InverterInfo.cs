namespace ecu2mqtt;

internal record class InverterInfo
{
    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public int ModbusId { get; init; }
    public int InverterType { get; init; }

    // String registers: each register = 2 ASCII chars
    static string DecodeString(short[] regs, int startAddr, int baseAddr, int count)
    {
        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            var r = (ushort)regs[startAddr - baseAddr + i];
            bytes[i * 2] = (byte)(r >> 8);
            bytes[i * 2 + 1] = (byte)(r & 0xFF);
        }
        return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0').Trim();
    }

    // Single bulk read covers 40004 to 40069 (66 registers)
    public static InverterInfo FromRegisters(short[] regs)
    {
        const int BASE = 40004;
        return new InverterInfo
        {
            Manufacturer = DecodeString(regs, 40004, BASE, 16), // [0..15]
            Model = DecodeString(regs, 40020, BASE, 16), // [16..31]
            Version = DecodeString(regs, 40044, BASE, 8),  // [40..47]
            SerialNumber = DecodeString(regs, 40052, BASE, 16), // [48..63]
            ModbusId = (ushort)regs[40068 - BASE],          // [64]
            InverterType = (ushort)regs[40070 - BASE],          // [66] — 101=single phase, 103=three phase
        };
    }

    public string InverterTypeLabel => InverterType switch
    {
        101 => "Single Phase",
        102 => "Split Phase",
        103 => "Three Phase",
        _ => $"Unknown ({InverterType})"
    };

    public override string ToString() => $"{Manufacturer} {Model} | Modbus ID: {ModbusId} | S/N: {SerialNumber} | FW: {Version}| {InverterTypeLabel}";
}
