using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace FanControl.FanPico
{
    // Status snapshot for a single fan channel from the R? response.
    public class FanChannelStatus
    {
        public float  Rpm  { get; set; }
        public float  Pwm  { get; set; }
        public string Name { get; set; }
    }

    // Full status snapshot parsed from a single R? response.
    public class FanPicoStatus
    {
        // Keyed by lowercase channel name, e.g. "fan<n>".
        public Dictionary<string, FanChannelStatus> Fans { get; } = new Dictionary<string, FanChannelStatus>();
        // Keyed by lowercase channel name, e.g. "sensor1". Value is degrees C.
        public Dictionary<string, float> Temperatures { get; } = new Dictionary<string, float>();
        // Human-readable names keyed the same as Temperatures.
        public Dictionary<string, string> Names { get; } = new Dictionary<string, string>();
    }

    //
    // SCPI-like command protocol for the FanPico device.
    // Documented at https://github.com/tjko/fanpico/blob/main/commands.md
    //
    // This class tries to separate the commands/protocol from the transport.
    // Currently serial is the only available command transport but there are 
    // scenarios where this could change.
    //
    // Identification:
    //   use the *IDN? command to get the board model number
    //
    // R? response format (comma-separated, one channel per line):
    //   fan1,"CPU Fan",rpm,tacho_raw,pwm_pct[,...]
    //   mbfan1,"MB Fan 1",rpm,tacho_raw,pwm_pct[,...]
    //   sensor1,"CPU Temp",celsius[,...]
    // This will cover all RPM and temperature sensors
    //
    // Fan control:
    //   CONF:FANx:SOU FIXED,pct   — Set the PWM to a percentage
    //   CONF:FANx:SOU?            — query current source (used to save values before launch)
    //   CONF:FANx:SOU source      — restore the stored config (on exit)
    //
    public class FanPicoCommands
    {
        private readonly FanPicoSerial _serial;

        public FanPicoCommands(FanPicoSerial serial)
        {
            _serial = serial ?? throw new ArgumentNullException(nameof(serial));
        }

        //
        // Sends *CLS and *IDN? to identify the device.
        // Returns the parsed model name and fan count.
        // Throws <see cref="InvalidOperationException"/> if the response does
        // not contain "FANPICO".
        //
        public (string Model, int FanCount) ScpiIdentify()
        {
            _serial.SendLine("*CLS");
            Thread.Sleep(100);
            _serial.DiscardInput();

            _serial.SendLine("*IDN?");
            string idn = _serial.ReadLine();

            if (idn.IndexOf("FANPICO", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException($"IDN mismatch: [{idn}]");

            // *IDN? format: "TJKO Industries,FANPICO-0804D,<serial>,<version>"
            string[] fields = idn.Split(',');
            string model    = fields.Length >= 2 ? fields[1].Trim() : "UNKNOWN";
            int fanCount    = ParseFanCountFromModel(model);

            return (model, fanCount);
        }

        //
        // Sends R? and reads the full multi-line response, parsing each line
        // into fan/mbfan channels and temperature sensors.
        //
        public FanPicoStatus ScpiReadStatus()
        {
            _serial.DiscardInput();
            _serial.SendLine("R?");

            var status = new FanPicoStatus();

            while (true)
            {
                string line;
                try   { line = _serial.ReadLine(); }
                catch (TimeoutException) { break; }

                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] fields = line.Split(',');
                if (fields.Length < 2) continue;

                string key  = fields[0].Trim().ToLowerInvariant();
                string name = fields[1].Trim().Trim('"', ' ');

                if ((key.StartsWith("fan") || key.StartsWith("mbfan")) && fields.Length >= 5)
                {
                    float rpm = ParseFloat(fields, 2);
                    float pwm = ParseFloat(fields, 4);
                    status.Fans[key] = new FanChannelStatus { Rpm = rpm, Pwm = pwm, Name = name };
                }
                else if (key.StartsWith("sensor") && fields.Length >= 3)
                {
                    float temp = ParseFloat(fields, 2);
                    status.Temperatures[key] = temp;
                    status.Names[key]        = name;
                }
            }

            return status;
        }

        //
        // Sets a fan output to a fixed PWM duty cycle (0-100).
        //
        public void ScpiSetFanFixed(int fanNum, int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            SendFireAndForget($"CONF:FAN{fanNum}:SOU FIXED,{percent}");
        }

        //
        // Queries the current source configuration for a fan.
        //
        public string GetFanSource(int fanNum)
        {
            _serial.DiscardInput();
            _serial.SendLine($"CONF:FAN{fanNum}:SOU?");
            return _serial.ReadLine();
        }

        //
        // Restores a fan's source configuration to a previously saved value.
        //
        public void ScpiSetFanSource(int fanNum, string source)
        {
            SendFireAndForget($"CONF:FAN{fanNum}:SOU {source}");
        }

        //
        // Helper: send a command to the serial port
        //
        private void SendFireAndForget(string command)
        {
            _serial.DiscardInput();
            _serial.SendLine(command);
            Thread.Sleep(50);
            _serial.DiscardInput();
        }

        //
        // Helper: use the board model number to get the number of fans
        // TODO: Consider using the results of the R? command to do this instead
        //
        private static int ParseFanCountFromModel(string model)
        {
            if (model.IndexOf("0804", StringComparison.Ordinal) >= 0) return 8;
            if (model.IndexOf("0401", StringComparison.Ordinal) >= 0) return 4;
            if (model.IndexOf("0200", StringComparison.Ordinal) >= 0) return 2;
            return 8;
        }

        private static float ParseFloat(string[] fields, int index)
        {
            if (index >= fields.Length) return 0f;
            return float.TryParse(fields[index].Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }
    }
}
