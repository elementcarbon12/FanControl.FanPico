using System;
using System.Linq;

namespace FanControl.FanPico
{
    //
    // High-level interface for a FanPico device.
    // This locates a serial port from a potetnal FanPico device and saves that in the class
    // It uses the FanPicoCommands class to issue the actual commands.
    //
    // All public methods are thread-safe.
    // This plugin will take ownership of the serial port for the duration of the program and
    // is part of the motivation for separating the protocol and transport.
    //
    public class FanPicoDevice : IDisposable
    {
        private FanPicoSerial    _serial;
        private FanPicoCommands  _commands;
        private readonly object  _lock = new object();
        private bool             _connected;
        private readonly Action<string> _log;

        private const int READ_TIMEOUT = 300;   // Timeput in MS to use after the inital handshake

        public bool   IsConnected => _connected;
        public string PortName    { get; }
        public string ModelName   { get; private set; } = "UNKNOWN";
        public int    FanCount    { get; private set; } = 8;

        public FanPicoDevice(string portName, Action<string> log = null)
        {
            PortName = portName;
            _log     = log;
        }

        //
        // Scans the Windows device registry for COM ports matching the
        // Raspberry Pi Pico's USB VID and DID.
        // Returns the first match, or null.  No serial ports are opened.
        // TODO: ensure this can coexist with other Raspberry Pi Pico devices
        //   that use serial transport.
        //
        public static string AutoDetect(Action<string> log = null)
        {
            var ports = FanPicoSerial.FindPorts("2E8A", "000A");
            if (ports.Count == 0)
                log?.Invoke("FanPico: no VID=2E8A/PID=000A device found in registry.");
            else
                log?.Invoke($"FanPico: found candidate port(s): {string.Join(", ", ports)}");
            return ports.FirstOrDefault();
        }

        //
        // Opens the serial port and verifies the device identity via *IDN?.
        // The port stays open until Disconnect.
        // Returns true on success.
        //
        public bool Connect()
        {
            lock (_lock)
            {
                if (_connected) return true;

                try
                {
                    _serial = new FanPicoSerial(PortName, _log);
                    _serial.Open();

                    _commands = new FanPicoCommands(_serial);
                    var (model, fanCount) = _commands.ScpiIdentify();

                    ModelName = model;
                    FanCount  = fanCount;
                    _serial.SetReadTimeout(READ_TIMEOUT);

                    _connected = true;
                    return true;
                }
                catch (InvalidOperationException ex)
                {
                    _log?.Invoke($"FanPico: IDN mismatch on {PortName}: {ex.Message}");
                    CleanupSerial();
                    return false;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"FanPico: Connect() exception: {ex.GetType().Name}: {ex.Message}");
                    _connected = false;
                    CleanupSerial();
                    return false;
                }
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                _connected = false;
                CleanupSerial();
            }
        }

        //
        // Sends R? and parses the full multi-line response.
        // Returns null if the device is not connected or communication fails.
        //
        public FanPicoStatus ReadStatus()
        {
            lock (_lock)
            {
                if (!_connected || !IsSerialReady) return null;

                try
                {
                    return _commands.ScpiReadStatus();
                }
                catch (Exception)
                {
                    _connected = false;
                    CleanupSerial();
                    return null;
                }
            }
        }

        //
        // Sets a fan output to a fixed PWM duty cycle (0-100).
        //
        public void SetFanFixed(int fanNum, int percent)
        {
            lock (_lock)
            {
                if (!_connected || !IsSerialReady) return;

                try
                {
                    _commands.ScpiSetFanFixed(fanNum, percent);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"FanPico: SendCommand exception: {ex.Message}");
                    _connected = false;
                    CleanupSerial();
                }
            }
        }

        //
        // Queries the current source configuration for a fan
        //
        public string GetFanSource(int fanNum)
        {
            lock (_lock)
            {
                if (!_connected || !IsSerialReady) return null;

                try
                {
                    return _commands.GetFanSource(fanNum);
                }
                catch { return null; }
            }
        }

        //
        // Restores a fan's source configuration to a previously saved value.
        //
        public void SetFanSource(int fanNum, string source)
        {
            lock (_lock)
            {
                if (!_connected || !IsSerialReady) return;

                try
                {
                    _commands.ScpiSetFanSource(fanNum, source);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"FanPico: SendCommand exception: {ex.Message}");
                    _connected = false;
                    CleanupSerial();
                }
            }
        }

        private bool IsSerialReady => _serial != null && _serial.IsOpen;

        private void CleanupSerial()
        {
            try { _serial?.Close(); } catch { }
            _serial   = null;
            _commands = null;
        }

        public void Dispose() => Disconnect();
    }
}
