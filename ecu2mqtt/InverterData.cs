using System.Text.Json;

namespace ecu2mqtt;

internal record class InverterData
{
    public float CurrentAC { get; init; }
    public float VoltageAC { get; init; }
    public float PowerAC { get; init; }
    public float Frequency { get; init; }
    public float PowerApparent { get; init; }
    public float PowerReactive { get; init; }
    public float PowerFactor { get; init; }
    public float Temperature { get; init; }
    public float EnergyTotal { get; init; }
    public int Status { get; init; }
    public float DC1Voltage { get; init; }
    public float DC2Voltage { get; init; }
    public float DC1Current { get; init; }
    public float DC2Current { get; init; }
    public float DC1Power { get; init; }
    public float DC2Power { get; init; }

    public string StatusLabel => Status switch
    {
        1 => "Off",
        2 => "Sleeping",
        3 => "Starting",
        4 => "MPPT",
        5 => "Throttled",
        6 => "Shutting Down",
        7 => "Fault",
        8 => "Standby",
        _ => $"Unknown ({Status})"
    };

    public static InverterData FromRegisters(Span<short> acRegs, Span<float> dcRegs)
    {
        const int AC_BASE = 40072;
        const int DC_BASE = 40214;

        /*       
        manufacturer:       40004, STRING,  16, 0,         ""
        model:              40020, STRING,  16, 0,         ""
        version:            40044, STRING,  8,  0,         ""
        serialnumber:       40052, STRING,  16, 0,         ""
        modbusid:           40068, UINT16,  1,  0,         ""
        type_inverter:      40070, UINT16,  1,  0,         ""  # 101 : single phase, 103 : three phases

        current:            40072, UINT16,  1, 0.01,      "A"
        voltage:            40080, UINT16,  1, 0.1,       "V"
        power_ac:           40084, UINT16,  1, 0.1,       "W"
        frequency:          40086, UINT16,  1, 0.01,     "Hz"
        power_apparent:     40088, UINT16,  1, 0.1,      "VA"
        power_reactive:     40090, UINT16,  1, 0.1,     "VAR"
        power_factor:       40092, UINT16,  1, 0.001, "cos φ"
        energy_total:       40094, UINT32,  2, 0.001,   "kWh"
        temperature:        40103, INT16,   1, 0.1,      "°C"
        status:             40108, INT16,   1, 0,          ""
        connected:          40188, UINT16,  1, 0,          ""
        power_max_lim:      40189, UINT16,  1, 0.1,       "%"
        power_max_lim_ena:  40193, UINT16,  1, 0,          ""

        DC1 voltage:        40214, FLOAT32, 2, 0,         "V"        
        DC2 voltage:        40216, FLOAT32, 2, 0,         "V"        
        DC1 current:        40230, FLOAT32, 2, 0,         "A"        
        DC2 current:        40232, FLOAT32, 2, 0,         "A"        
        DC1 power:          40246, FLOAT32, 2, 0,         "W"        
        DC2 power:          40248, FLOAT32, 2, 0,         "W"        
        */

        return new InverterData
        {
            CurrentAC = (ushort)acRegs[40072 - AC_BASE] * 0.01f,
            VoltageAC = (ushort)acRegs[40080 - AC_BASE] * 0.1f,
            PowerAC = (ushort)acRegs[40084 - AC_BASE] * 0.1f,
            Frequency = (ushort)acRegs[40086 - AC_BASE] * 0.01f,
            PowerApparent = (ushort)acRegs[40088 - AC_BASE] * 0.1f,
            PowerReactive = (ushort)acRegs[40090 - AC_BASE] * 0.1f,
            PowerFactor = (ushort)acRegs[40092 - AC_BASE] * 0.001f,
            EnergyTotal = (uint)((ushort)acRegs[40094 - AC_BASE] << 16 | (ushort)acRegs[40095 - AC_BASE]) * 0.001f,
            Temperature = acRegs[40103 - AC_BASE] * 0.1f, // int16, signed
            Status = acRegs[40108 - AC_BASE], // int16, signed

            DC1Voltage = dcRegs[(40214 - DC_BASE) / 2],
            DC2Voltage = dcRegs[(40216 - DC_BASE) / 2],
            DC1Current = dcRegs[(40230 - DC_BASE) / 2],
            DC2Current = dcRegs[(40232 - DC_BASE) / 2],
            DC1Power = dcRegs[(40246 - DC_BASE) / 2],
            DC2Power = dcRegs[(40248 - DC_BASE) / 2],
        };
    }

    public override string ToString() =>
        $"AC: {PowerAC:F1} W, {VoltageAC:F1} V, {CurrentAC:F2} A, {Frequency:F2} Hz, {PowerApparent:F1} VA, {PowerReactive:F1} VAR, PF: {PowerFactor:F3}, Energy: {EnergyTotal:F3} kWh, Temp: {Temperature:F1} °C, Status: {StatusLabel}, DC1: {DC1Power:F1} W @ {DC1Voltage:F1} V, {DC1Current:F2} A, DC2: {DC2Power:F1} W @ {DC2Voltage:F1} V, {DC2Current:F2} A";

}
