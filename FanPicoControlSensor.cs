using FanControl.Plugins;

namespace FanControl.FanPico
{
    //
    // Fan speed control for a FAN output channel on the FanPico device.
    // FanControl calls Set() to request a new speed and Reset() to restore automatic control.
    // The actual SCPI commands are sent by FanPicoPlugin.Update() after checking the flags.
    // This class doesn't perform any device updates directly.
    // Rather, it sets the variables the FanPicoPlugin : IPlugin2 class needs to perform 
    // its work in one shot. 
    // TODO: Consider rolling this into FanPicoSensor.cs
    //
    public class FanPicoControlSensor : IPluginControlSensor
    {
        private float? _pendingValue;

        public FanPicoControlSensor(int fanNum)
        {
            FanNum = fanNum;
        }

        public string Id     => $"FanPico/Control/{FanNum}";
        public string Name   => $"FanPico Fan #{FanNum}";
        public string Origin => "FanPico";

        public float? Value { get; private set; }

        public void Update() { }

        public void Set(float val)
        {
            _pendingValue = val;
            NeedsApply   = true;
            NeedsReset   = false;
        }

        public void Reset()
        {
            NeedsReset = true;
            NeedsApply = false;
        }

        internal int   FanNum       { get; }
        internal bool  NeedsApply  { get; private set; }
        internal bool  NeedsReset  { get; private set; }
        internal float PendingPercent => _pendingValue ?? 0f;

        internal void ApplyConfirmed()
        {
            Value      = _pendingValue;
            NeedsApply = false;
        }

        internal void ResetConfirmed()
        {
            Value         = null;
            NeedsReset    = false;
            _pendingValue = null;
        }

        internal void SetValueNull() => Value = null;
    }
}
