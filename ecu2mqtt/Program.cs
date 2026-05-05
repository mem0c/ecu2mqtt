using FluentModbus;
using MQTTnet;
using MQTTnet.Protocol;

namespace ecu2mqtt;

class Program
{
    private static Task Main()
    {
        var ecuHost = RequireEnv("ECU_HOST");
        var ecuPort = int.TryParse(OptionalEnv("ECU_PORT", "502"), out var p1) ? p1 : 502;

        var mqttHost = RequireEnv("MQTT_HOST");
        var mqttPort = int.TryParse(OptionalEnv("MQTT_PORT", "1883"), out var p2) ? p2 : 1883;
        var mqttUser = RequireEnv("MQTT_USER");
        var mqttPassword = RequireEnv("MQTT_PASSWORD");

        var pollInterval = int.TryParse(OptionalEnv("POLL_INTERVAL", "300"), out var i) ? i : 300;
        var slaveIds = RequireEnv("INVERTER_IDS").Split(',').Select(byte.Parse).ToArray();

        var debug = OptionalEnv("ECU2MQTT_DEBUG", "0") == "1";

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return PollModbusServerAsync(ecuHost, ecuPort, mqttHost, mqttPort, mqttUser, mqttPassword, TimeSpan.FromSeconds(pollInterval), slaveIds, debug, cts.Token);
    }

    private static async Task PollModbusServerAsync(string modbusHost, int modbusPort, string mqttHost, int mqttPort, string mqttUser, string mqttPassword, TimeSpan pollInterval, byte[] slaveIds, bool debug, CancellationToken token)
    {
        Console.WriteLine($"[INFO] Connecting to Modbus server at {modbusHost}:{modbusPort} and MQTT broker at {mqttHost}:{mqttPort}");

        // Modbus client
        using var modbusClient = new ModbusTcpClient();
        var modbusRemoteEndPoint = $"{modbusHost}:{modbusPort}";

        // MQTT client
        IMqttClient? mqttClient = null;
        if (!debug)
        {
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(mqttHost, mqttPort)
                .WithClientId("ecu2mqtt")
                .WithCredentials(mqttUser, mqttPassword)
                .WithWillTopic("myinverter/bridge/availability")
                .WithWillPayload("""{"state": "offline"}""")
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            var mqttFactory = new MqttClientFactory();
            mqttClient = mqttFactory.CreateMqttClient();
            await mqttClient.ConnectAsync(options, token);
                    
            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(InverterDataJsonSerializer.BridgeAvailabilityTopic)
                .WithPayload("""{"state": "online"}""")
                .WithRetainFlag(true)
                .Build(), token);
        }

        Console.WriteLine("[INFO] Started — press Ctrl+C or send SIGTERM to stop");

        var inverters = slaveIds.Select(id => new Inverter { SlaveId = id }).ToArray();

        try
        {
            if (!token.IsCancellationRequested)
            {
                modbusClient.Connect(modbusRemoteEndPoint, ModbusEndianness.BigEndian);

                await PublishInvertInfosAsync(modbusClient, mqttClient, inverters, token);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!modbusClient.IsConnected)
                    {
                        modbusClient.Connect(modbusRemoteEndPoint, ModbusEndianness.BigEndian);
                    }

                    await PublishInverterDatasAsync(modbusClient, mqttClient, inverters, token);
                }
                catch (OperationCanceledException)
                {
                    throw; // let the outer catch handle it
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] {ex}");
                }
                finally
                {
                    modbusClient.Disconnect();
                }

                await Task.Delay(pollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[INFO] Shutdown signal received");
        }
        finally
        {
            // Graceful cleanup
            if (modbusClient.IsConnected)
                modbusClient.Disconnect();

            if (mqttClient is not null && mqttClient.IsConnected)
            {
                foreach (var inverter in inverters)
                {
                    if (inverter.Info is not null)
                    {
                        await PublishDeviceAvailabilityAsync(mqttClient, inverter.Info.SerialNumber, online: false);
                    }
                }

                await mqttClient.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .Build());
            }

            Console.WriteLine("[INFO] Shutdown complete");
        }
    }

    private record class Inverter
    {
        public byte SlaveId { get; init; }
        public bool IsOnline { get; set; }
        public InverterInfo? Info { get; set; }
        public InverterData? Data { get; set; }
    }

    private static async Task PublishInvertInfosAsync(ModbusClient modbusClient, IMqttClient? mqttClient, Inverter[] inverters, CancellationToken token)
    {
        foreach (var inverter in inverters)
        {
            try
            {
                // 40004 to 40070: 67 registers
                var infoRegs = modbusClient.ReadHoldingRegisters<short>(inverter.SlaveId, 40004, 67).ToArray();
                inverter.Info = InverterInfo.FromRegisters(infoRegs); 
                if (mqttClient is null)
                {
                    Console.WriteLine($"{inverter.Info}");
                }
            }
            catch (Exception ex)
            {
                inverter.Info = null;
                Console.Error.WriteLine($"[WARN] Could not read info for slave {inverter.SlaveId}: {ex.Message}");
            }
        }

        // publish retained so HA always has it
        foreach (var inverter in inverters)
        {
            if (inverter.Info is null)
            {
                continue;
            }

            foreach (var (topic, payload) in InverterDataJsonSerializer.GetHomeAssistantDiscoveryTopicsAndPayloads(inverter.Info))
            {
                if (mqttClient is null)
                {
                    Console.WriteLine($"{topic}:\n{payload}");
                }
                else
                {
                    await PublishDeviceAvailabilityAsync(mqttClient, inverter.Info.SerialNumber, online: true);
                    await mqttClient.PublishStringAsync(topic, payload, retain: true, cancellationToken: token);
                }
            }
        }
    }

    private static async Task PublishInverterDatasAsync(ModbusTcpClient modbusClient, IMqttClient? mqttClient, Inverter[] inverters, CancellationToken token)
    {
        foreach (var inverter in inverters)
        {
            try
            {
                var acRegisters = modbusClient.ReadHoldingRegisters<short>(inverter.SlaveId, 40072, 37).ToArray();
                var dcRegisters = modbusClient.ReadHoldingRegisters<float>(inverter.SlaveId, 40214, 35).ToArray();
                inverter.Data = InverterData.FromRegisters(acRegisters, dcRegisters);
                if (mqttClient is null)
                {
                    Console.WriteLine($"{inverter.Data}");
                }
            }
            catch (Exception ex)
            {
                inverter.Data = null;
                Console.Error.WriteLine($"[WARN] Could not read data for slave {inverter.SlaveId}: {ex.Message}");
            }
        }

        foreach (var inverter in inverters)
        {
            if (inverter.Info is null)
            {
                continue;
            }

            bool isOnline;
            if (inverter.Data != null)
            {
                isOnline = true;

                var (topic, payload) = InverterDataJsonSerializer.GetStateTopicAndPayload(inverter.Data, inverter.Info.SerialNumber);
                if (mqttClient is null)
                {
                    Console.WriteLine($"{topic}:\n{payload}");
                }
                else
                {
                    await mqttClient.PublishStringAsync(topic, payload, cancellationToken: token);
                }
            }
            else
            {
                isOnline = false;
            }

            if (isOnline != inverter.IsOnline)
            {
                inverter.IsOnline = isOnline;
                await PublishDeviceAvailabilityAsync(mqttClient, inverter.Info.SerialNumber, online: isOnline);
            }
        }
    }

    private static async Task PublishDeviceAvailabilityAsync(IMqttClient? mqttClient, string serialNumber, bool online)
    {
        if (mqttClient is null)
            return;

        await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(InverterDataJsonSerializer.GetDeviceAvailabilityTopic(serialNumber))
            .WithPayload(online ? """{"state": "online"}""" : """{"state": "offline"}""")
            .WithRetainFlag(true)
            .Build());
    }

    private static string RequireEnv(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(value))
        {
            Console.Error.WriteLine($"[FATAL] Required environment variable '{key}' is not set");
            Environment.Exit(1);
        }
        return value!;
    }

    private static string OptionalEnv(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(value))
        {
            Console.Error.WriteLine($"[WARN] '{key}' not set, using default: {fallback}");
            return fallback;
        }
        return value;
    }
}