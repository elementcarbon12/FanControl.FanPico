using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Threading;

namespace FanControl.FanPico
{
    //
    // Low-level serial transport for a FanPico device.
    //
    // This class is NOT thread-safe — callers must synchronize externally.
    //
    public class FanPicoSerial : IDisposable
    {
        private SerialPort _port;
        private readonly Action<string> _log;

        // TODO: Consider making serial port parameters configurable via a config file
        private const int BAUD_RATE             = 115200;
        private const int INITIAL_READ_TIMEOUT  = 1500;   // generous for initial handshake
        private const int WRITE_TIMEOUT         = 500;

        public bool   IsOpen   { get; private set; }
        public string PortName { get; }

        public FanPicoSerial(string portName, Action<string> log = null)
        {
            PortName = portName;
            _log     = log;
        }

        //
        // Opens the serial port with standard settings.
        // The initial read timeout is 1500 ms for device identification.
        // Once the handshake is complete,  call SetReadTimeout() after the
        // handshake to reduce it for normal operation.
        //
        public void Open()
        {
            _port = new SerialPort(PortName, BAUD_RATE, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = INITIAL_READ_TIMEOUT,
                WriteTimeout = WRITE_TIMEOUT,
                DtrEnable    = true,
                RtsEnable    = true
            };
            _log?.Invoke($"FanPico: opening {PortName}...");
            _port.Open();
            Thread.Sleep(200);
            _port.DiscardInBuffer();
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            try { _port?.Close(); } catch { }
            _port = null;
        }

        //
        // Set the desired read timeout. 
        //
        public void SetReadTimeout(int ms)
        {
            if (_port != null)
                _port.ReadTimeout = ms;
        }


        //
        // Thin port write wrapper
        //
        public void SendLine(string line)
        {
            _port.Write(line + "\n");
        }

        //
        // Thin port read wrapper
        //
        public string ReadLine()
        {
            return _port.ReadLine().Trim('\r', '\n', ' ');
        }

        //
        // Clear the recieve buffer
        //
        public void DiscardInput()
        {
            _port?.DiscardInBuffer();
        }

        //
        // Returns all COM port names whose registry entries match the given
        // USB VID and PID.
        //
        public static List<string> FindPorts(string vid, string pid)
        {
            var result     = new List<string>();
            var validPorts = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            var rx         = new Regex($@"^VID_{vid}.*PID_{pid}", RegexOptions.IgnoreCase);

            try
            {
                RegistryKey rk2 = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum");
                if (rk2 == null) return result;

                foreach (string bus in rk2.GetSubKeyNames())
                {
                    RegistryKey rk3 = rk2.OpenSubKey(bus);
                    if (rk3 == null) continue;

                    foreach (string device in rk3.GetSubKeyNames())
                    {
                        if (!rx.IsMatch(device)) continue;

                        RegistryKey rk4 = rk3.OpenSubKey(device);
                        if (rk4 == null) continue;

                        foreach (string instance in rk4.GetSubKeyNames())
                        {
                            RegistryKey rk6 = rk4.OpenSubKey(instance)
                                               ?.OpenSubKey("Device Parameters");
                            if (rk6 == null) continue;

                            string portName = rk6.GetValue("PortName") as string;
                            if (!string.IsNullOrEmpty(portName) && validPorts.Contains(portName))
                                result.Add(portName);
                        }
                    }
                }
            }
            // registry access could fail in some cases
            catch { }

            return result;
        }

        //
        // destructor: close the port
        //
        public void Dispose() => Close();
    }
}
