# FanControl.FanPico

A [FanControl](https://github.com/Rem0o/FanControl.Releases) plugin for the
[FanPico](https://github.com/tjko/fanpico) open-source fan controller.

The plugin auto-detects the FanPico board over USB serial, reads fan RPM and
temperature data, and lets FanControl set individual fan speeds through PWM
control.

This project is not associated with Timo Kokkonen or Remi Mercier.
## Supported boards

- FANPICO-0804 / 0804D -- used for development
- FANPICO-0401D -- untested
- FANPICO-0200 -- untested

[Where to buy a FanPico](https://github.com/tjko/fanpico/discussions/12)


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
