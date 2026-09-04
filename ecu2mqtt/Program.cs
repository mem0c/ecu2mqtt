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

        return EcuModbus.PollModbusServerAsync(ecuHost, ecuPort, mqttHost, mqttPort, mqttUser, mqttPassword, TimeSpan.FromSeconds(pollInterval), slaveIds, debug, cts.Token);
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