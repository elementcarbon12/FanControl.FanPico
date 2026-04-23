# FanControl.FanPico

A [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin for the
[FanPico](https://github.com/tjko/fanpico) open-source fan controller.

The plugin auto-detects the FanPico board over USB serial, reads fan RPM and
temperature data, and lets FanControl set individual fan speeds through PWM
control.

The plugin will also make the two thermistor based temperature sensors and 
the Pi Pico's onboard temperature sensor available in FanControl.

This project is not associated with Timo Kokkonen or Remi Mercier.
## Supported boards

- FANPICO-0804 / 0804D -- used for development
- FANPICO-0401D -- untested
- FANPICO-0200 -- untested

[Where to buy a FanPico](https://github.com/tjko/fanpico/discussions/12)

## Functionality
The FanPico needs to be connected to the host system running FanControl via USB.

The FanPico will also require an external 12v power source connected either via
the floppy power connector or DC barrel jack (whichever option you choose to install).

This plugin does not support reporting any info to the mainboard via the MB FAN headers.
The expectation is that this is done via the USB connection and is used by FanControl directly

## Building

This project was built using the dotnet 8 SDK
This project uses System.IO.Ports 8.0.0

From the project root:

```
dotnet build
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build
```

The output DLL is written to bin\Debug\net8.0-windows\FanControl.FanPico.dll.

For a release build:

```
dotnet build -c Release
```

Output: bin\Release\net8.0-windows\FanControl.FanPico.dll.

## Installing

See the the [FanControl GitHub page](https://github.com/Rem0o/FanControl.Releases?tab=readme-ov-file#plugins)

## TODO
- Ensure that the settings programed into the FanPico before the plugin starts are restored on exit. The expectation is for the user to set a baseline fan speed that will be safe for the PC before FanControl can load and take control of the fan. the Python script set_fanpico80.py can be used to set default fan speeds the FanPico will set after it powers up and waits for for FanControl. It will set the default to 80% unless the --duty option is specified.
 - Enable extra I2C sensors via the FanPico's I2C interface. Currently the plugin tells FanControl it has a fixed number of temperature sensors but the code to parse additional sensors reported by the FanPico is in place. The plugin needs to be updated to enumerate the temperature sensors reported by FanPico and present all available sensors. The current setting of three was to get something enabled for testing. Testing with I2C sensors is the next step.

