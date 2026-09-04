using FluentModbus;
using MQTTnet;
using MQTTnet.Protocol;

namespace ecu2mqtt;

internal static class EcuModbus
{
    public static async Task PollModbusServerAsync(string modbusHost, int modbusPort, string mqttHost, int mqttPort, string mqttUser, string mqttPassword, TimeSpan pollInterval, byte[] slaveIds, bool debug, CancellationToken token)
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
                .WithWillTopic(MqttConstants.BridgeAvailabilityTopic)
                .WithWillPayload("""{"state": "offline"}""")
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            var mqttFactory = new MqttClientFactory();
            mqttClient = mqttFactory.CreateMqttClient();
            await mqttClient.ConnectAsync(options, token);

            await mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(MqttConstants.BridgeAvailabilityTopic)
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
}

