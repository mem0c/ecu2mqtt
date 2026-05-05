using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ecu2mqtt;

internal class InverterDataJsonSerializer
{
    private record SensorMeta(string Suffix, string Name, string? DeviceClass, string? StateClass, string? Unit, string Icon);

    private static readonly SensorMeta[] Sensors =
    [
        new("power_ac",       "AC Power",       "power",          "measurement",      "W",   "mdi:lightning-bolt"),
        new("voltage_ac",     "AC Voltage",     "voltage",        "measurement",      "V",   "mdi:sine-wave"),
        new("current_ac",     "AC Current",     "current",        "measurement",      "A",   "mdi:current-ac"),
        new("frequency",      "Frequency",      "frequency",      "measurement",      "Hz",  "mdi:sine-wave"),
        new("power_apparent", "Apparent Power", "apparent_power", "measurement",      "VA",  "mdi:lightning-bolt-outline"),
        new("power_reactive", "Reactive Power", "reactive_power", "measurement",      "VAR", "mdi:lightning-bolt-outline"),
        new("power_factor",   "Power Factor",   "power_factor",   "measurement",      null,  "mdi:angle-acute"),
        new("energy_total",   "Total Energy",   "energy",         "total_increasing", "kWh", "mdi:solar-power"),
        new("temperature",    "Temperature",    "temperature",    "measurement",      "°C",  "mdi:thermometer"),
        new("status",         "Status",         null,             null,               null,  "mdi:information-outline"),
        new("dc1_power",      "DC1 Power",      "power",          "measurement",      "W",   "mdi:solar-panel"),
        new("dc1_voltage",    "DC1 Voltage",    "voltage",        "measurement",      "V",   "mdi:sine-wave"),
        new("dc1_current",    "DC1 Current",    "current",        "measurement",      "A",   "mdi:current-dc"),
        new("dc2_power",      "DC2 Power",      "power",          "measurement",      "W",   "mdi:solar-panel"),
        new("dc2_voltage",    "DC2 Voltage",    "voltage",        "measurement",      "V",   "mdi:sine-wave"),
        new("dc2_current",    "DC2 Current",    "current",        "measurement",      "A",   "mdi:current-dc"),
    ];

    public const string TopicPrefix = "ecu2mqtt";
    public const string BridgeAvailabilityTopic = $"{TopicPrefix}/bridge/availability";

    public static string GetDeviceAvailabilityTopic(string serialNumber) => $"{TopicPrefix}/{serialNumber}/availability";

    public static IEnumerable<(string Topic, string Payload)> GetHomeAssistantDiscoveryTopicsAndPayloads(InverterInfo inverterInfo)
    {
        var deviceId = $"inverter_{inverterInfo.SerialNumber}";
        var stateTopic = $"{TopicPrefix}/{inverterInfo.SerialNumber}/state";
        var deviceAvailabilityTopic = GetDeviceAvailabilityTopic(inverterInfo.SerialNumber);

        foreach (var sensor in Sensors)
        {
            var config = new SensorConfig(
                Name: sensor.Name,
                ObjectId: $"inverter_{inverterInfo.SerialNumber}_{sensor.Suffix}",
                UniqueId: $"{deviceId}_{sensor.Suffix}",
                StateTopic: stateTopic,
                ValueTemplate: $"{{{{ value_json.{sensor.Suffix} }}}}",
                Icon: sensor.Icon,
                Availability:
                [
                    new(BridgeAvailabilityTopic, "{{ value_json.state }}"),
                    new(deviceAvailabilityTopic, "{{ value_json.state }}"),
                ],
                AvailabilityMode: "all",
                DeviceClass: sensor.DeviceClass,
                StateClass: sensor.StateClass,
                UnitOfMeasurement: sensor.Unit,
                Device: new(
                    Identifiers: [deviceId],
                    Name: $"Inverter {inverterInfo.SerialNumber}",
                    Manufacturer: inverterInfo.Manufacturer,
                    Model: inverterInfo.Model,
                    SwVersion: inverterInfo.Version
                )
            );

            var topic = $"homeassistant/sensor/{inverterInfo.SerialNumber}/{sensor.Suffix}/config";
            yield return (topic, JsonSerializer.Serialize(config, AppJsonContext.Default.SensorConfig));
        }
    }

    public static (string Topic, string Payload) GetStateTopicAndPayload(InverterData inverterData, string serialNumber)
    {
        var topic = $"{TopicPrefix}/{serialNumber}/state";
        var payload = new JsonObject
        {
            ["power_ac"] = inverterData.PowerAC,
            ["voltage_ac"] = inverterData.VoltageAC,
            ["current_ac"] = inverterData.CurrentAC,
            ["frequency"] = inverterData.Frequency,
            ["power_apparent"] = inverterData.PowerApparent,
            ["power_reactive"] = inverterData.PowerReactive,
            ["power_factor"] = inverterData.PowerFactor,
            ["energy_total"] = inverterData.EnergyTotal,
            ["temperature"] = inverterData.Temperature,
            ["status"] = inverterData.StatusLabel,
            ["dc1_power"] = inverterData.DC1Power,
            ["dc1_voltage"] = inverterData.DC1Voltage,
            ["dc1_current"] = inverterData.DC1Current,
            ["dc2_power"] = inverterData.DC2Power,
            ["dc2_voltage"] = inverterData.DC2Voltage,
            ["dc2_current"] = inverterData.DC2Current,
        };
        return (topic, payload.ToJsonString());
    }
}

internal partial record AvailabilityEntry(string Topic, string ValueTemplate);
internal partial record DeviceInfo(string[] Identifiers, string Name, string Manufacturer, string Model, string SwVersion);
internal partial record SensorConfig(string Name, string ObjectId, string UniqueId, string StateTopic, string ValueTemplate, string Icon, AvailabilityEntry[] Availability, string AvailabilityMode, string? DeviceClass, string? StateClass, string? UnitOfMeasurement, DeviceInfo Device);

[JsonSerializable(typeof(SensorConfig))]
[JsonSerializable(typeof(AvailabilityEntry))]
[JsonSerializable(typeof(DeviceInfo))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class AppJsonContext : JsonSerializerContext { }