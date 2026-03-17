using FanControl.Plugins;

namespace FanControl.FanPico
{
    //
    // Fan RPM sensor reader class.
    // Values are pushed by FanPicoPlugin.Update() from the R? response.
    //
    public class FanPicoFanSensor : IPluginSensor
    {
        private readonly string _channelKey;
        private readonly int    _num;

        public FanPicoFanSensor(string channelKey, int num)
        {
            _channelKey = channelKey;
            _num        = num;
        }

        public string Id     => $"FanPico/Fan/{_num}";
        public string Name   => $"FanPico Fan #{_num}";
        public string Origin => "FanPico";

        public float? Value { get; private set; }

        public void Update() { }

        internal string ChannelKey => _channelKey;

        internal void UpdateValue(float? rpm) => Value = rpm;
    }
    //
    // built in temperature sensor reader class.
    // Temperatures are in degrees Celsius.
    // TODO: See if this can be used for vsensor I2C and 1-wire sensors
    //
    public class FanPicoTemperatureSensor : IPluginSensor
    {
        private readonly string _channelKey;
        private readonly int    _num;

        public FanPicoTemperatureSensor(string channelKey, int num)
        {
            _channelKey = channelKey;
            _num        = num;
        }

        public string Id     => $"FanPico/Sensor/{_num}";
        public string Name   => $"FanPico Sensor #{_num}";
        public string Origin => "FanPico";

        public float? Value { get; private set; }

        public void Update() { }


        internal string ChannelKey => _channelKey;

        internal void UpdateValue(float? temp) => Value = temp;
    }
}
