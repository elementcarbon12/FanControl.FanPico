using FanControl.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FanControl.FanPico
{
    //
    // FanControl plugin for the FanPico fan controller (https://github.com/tjko/fanpico).
    //
    // Exposes:
    //   A device specefic number of fan spped controls
    //   A device specefic number of fan RPM sensors
    //   3 temerature sensors (2 thermistors and one Pi Pico temperature sensor)
    //       TODO: look into discovery and reporting of additional I2C/1-wire sensors
    //
    // Mainboard sensors are ignored since the system can theoretically monitor via those. 
    // While that could result in duplicated data, the real value in this plugin is 
    // full independent control of all channels the assumption is that the mbfan headers 
    // will not be used.
    //  
    // Device discovery uses *IDN? identification over every Raspberry Pi com port
    // so it coexists safely with other RP2040-based devices.
    // NOTE: This plugin currently only supports a single FanPico device
    //
    public class FanPicoPlugin : IPlugin2
    {
        private readonly IPluginLogger _logger;
        private readonly IPluginDialog _dialog;
        private readonly object        _lock = new object();

        private FanPicoDevice              _device;
        private FanPicoFanSensor[]         _fanSensors; // array of IPluginSensor for fans
        private FanPicoControlSensor[]     _controls; // array of IPluginControlSensor objects
        private FanPicoTemperatureSensor[] _tempSensors; // array of IPluginSensor for temperature sensors

        // Used to save original configured fan sources
        // A source is something used to control the PWM signal for a fan
        // We need to save this so we can resore FanPico behavior on exit.
        private Dictionary<int, string> _originalSources = new Dictionary<int, string>();
        private bool     _sourcesQueried    = false;
        private bool     _initialized       = false;
        private DateTime _lastReconnectTime = DateTime.MinValue; // used as a reconnect cooldown

        private const int RECONNECT_COOLDOWN_SEC = 15;

        // Setting to 8 for now for the 804/804d boards
        private const int DEFAULT_FAN_COUNT = 8;
        // FanPico boards have 3 base temperature sensors (sensor1, sensor2, pico_temp).
        // TODO: add additional sensors via I2C or 1-wire to see how to support them
        private const int TEMP_SENSOR_COUNT = 3;

        private int _fanCount = DEFAULT_FAN_COUNT;

        public string Name => "FanPico";


        public FanPicoPlugin(IPluginLogger logger, IPluginDialog dialog)
        {
            _logger = logger;
            _dialog = dialog;
        }

        public void Initialize()
        {
            _logger.Log("FanPico: initializing...");
            _lastReconnectTime = DateTime.MinValue;

            string port = FanPicoDevice.AutoDetect(msg => _logger.Log(msg));
            if (port != null)
            {
                _device = new FanPicoDevice(port, msg => _logger.Log(msg));
                if (_device.Connect())
                {
                    _fanCount = _device.FanCount;
                    _logger.Log($"FanPico: connected {_device.ModelName} on {port} ({_fanCount} fans)");
                }
                else
                {
                    _logger.Log($"FanPico: found port {port} but IDN check failed.");
                }
            }
            else
            {
                _logger.Log("FanPico: device not found in registry — will retry.");
            }

            _initialized = true;
        }

        public void Load(IPluginSensorsContainer container)
        {
            if (!_initialized) return;

            _fanSensors = Enumerable.Range(1, _fanCount)
                .Select(i => new FanPicoFanSensor($"fan{i}", i))
                .ToArray();

            _controls = Enumerable.Range(1, _fanCount)
                .Select(i => new FanPicoControlSensor(i))
                .ToArray();

            _tempSensors = Enumerable.Range(1, TEMP_SENSOR_COUNT)
                .Select(i => new FanPicoTemperatureSensor($"sensor{i}", i))
                .ToArray();

            container.FanSensors.AddRange(_fanSensors);
            container.ControlSensors.AddRange(_controls);
            container.TempSensors.AddRange(_tempSensors);
        }

        //
        // Using the IPlugin2.Update method so we can update all the sensors in one go
        // 
        public void Update()
        {
            lock (_lock)
            {
                if (_device == null || !_device.IsConnected)
                {
                    // If not connected, try to connect but don't hammer the port
                    if ((DateTime.Now - _lastReconnectTime).TotalSeconds >= RECONNECT_COOLDOWN_SEC)
                    {
                        _lastReconnectTime = DateTime.Now;
                        TryReconnect();
                    }

                    if (_device == null || !_device.IsConnected)
                    {
                        SetAllNull();
                        return;
                    }
                }

                if (!_sourcesQueried)
                    // build the map of the fan sensors if we need to
                    QueryOriginalSources();

                foreach (var ctrl in _controls)
                {
                    if (ctrl.NeedsReset)
                    {
                        // For FanPico-0401 boards, there is only 1 MBFAN connector
                        int mbFan = 1;
                        if (_fanCount == 8) {
                            // For FanPico-0804 boards, there are 4 MBFAN connectors
                            // and the output fans map in a circular pattern
                            mbFan = ((ctrl.FanNum - 1) % 4) + 1;
                        }
                        string src = _originalSources.TryGetValue(ctrl.FanNum, out string s)
                            ? s
                            : $"MBFAN,{mbFan}"; // restore default mapping if lookup fails
                        _device.SetFanSource(ctrl.FanNum, src);
                        ctrl.ResetConfirmed();
                    }
                    else if (ctrl.NeedsApply)
                    {
                        // Apply new % speed target supplied by FanPicoControlSensor : IPluginControlSensor
                        _device.SetFanFixed(ctrl.FanNum, (int)ctrl.PendingPercent);
                        ctrl.ApplyConfirmed();
                    }
                }

                // Actual sensor value updates happen here
                var status = _device.ReadStatus();
                if (status == null)
                {
                    _logger.Log("FanPico: status read failed — device may have disconnected.");
                    SetAllNull();
                    return;
                }
                // Pull data from the sensors
                foreach (var sensor in _fanSensors)
                {
                    float? rpm = null;
                    if (status.Fans.TryGetValue(sensor.ChannelKey, out FanChannelStatus fan))
                        rpm = fan.Rpm;
                    sensor.UpdateValue(rpm);
                }

                foreach (var sensor in _tempSensors)
                {
                    float? temp = null;
                    if (status.Temperatures.TryGetValue(sensor.ChannelKey, out float t))
                        temp = t;
                    sensor.UpdateValue(temp);
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _initialized    = false;
                _sourcesQueried = false;
                _originalSources.Clear();

                _device?.Disconnect();
                _device?.Dispose();
                _device = null;

                _fanSensors    = null;
                _controls      = null;
                _tempSensors   = null;
            }
        }

        private void TryReconnect()
        {
            try
            {
                if (_device == null)
                {
                    string port = FanPicoDevice.AutoDetect(msg => _logger.Log(msg));
                    if (port == null) return;
                    _device         = new FanPicoDevice(port, msg => _logger.Log(msg));
                    _sourcesQueried = false;
                }

                if (_device.Connect())
                {
                    _fanCount = _device.FanCount;
                    _logger.Log($"FanPico: reconnected {_device.ModelName} on {_device.PortName} ({_fanCount} fans)");
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"FanPico: reconnect failed: {ex.Message}");
            }
        }

        //
        // Get the names and signal numbers for the devices we want to monitor and control
        // 
        private void QueryOriginalSources()
        {
            _originalSources.Clear();
            for (int i = 1; i <= _fanCount; i++)
            {
                string src = _device.GetFanSource(i);
                if (src != null)
                    _originalSources[i] = src;
            }
            _sourcesQueried = true;
            _logger.Log($"FanPico: stored original sources for {_originalSources.Count} fan(s).");
        }

        private void SetAllNull()
        {
            if (_fanSensors  != null) foreach (var s in _fanSensors)  s.UpdateValue(null);
            if (_tempSensors != null) foreach (var s in _tempSensors) s.UpdateValue(null);
            if (_controls    != null) foreach (var c in _controls)    c.SetValueNull();
        }
    }
}
