#!/usr/bin/env python3
"""
Sets all FanPico fan outputs to a fixed duty cycle and saves to flash.

Ensures fans spin at a safe speed the instant the device gets power,
before any computer or software is running. Backs up the current
configuration first so it can be restored later.

Requires: pyserial  (pip install pyserial)
"""

import argparse
import os
import re
import sys
import time
from datetime import datetime

try:
    import serial
    import serial.tools.list_ports
except ImportError:
    print("ERROR: pyserial is required.  Install with:  pip install pyserial")
    sys.exit(1)


PICO_VID = 0x2E8A
PICO_PIDS = (0x000A, 0x0005)


def log(msg: str):
    ts = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"[{ts}] {msg}")


def find_fanpico_ports() -> list[str]:
    """Scan USB serial ports for Raspberry Pi Pico VID/PID."""
    found = []
    for port in serial.tools.list_ports.comports():
        if port.vid == PICO_VID and port.pid in PICO_PIDS:
            found.append(port.device)
    return sorted(set(found))


def resolve_com_port() -> str:
    """Auto-detect or interactively select a COM port."""
    detected = find_fanpico_ports()

    if len(detected) == 1:
        print(f"Auto-detected FanPico on {detected[0]}")
        return detected[0]

    if len(detected) > 1:
        print(f"Multiple FanPico candidates found: {', '.join(detected)}")
    else:
        print("Could not auto-detect a FanPico device.")

    all_ports = sorted(p.device for p in serial.tools.list_ports.comports())
    if not all_ports:
        print("ERROR: No COM ports available on this system.")
        sys.exit(1)

    print("\nAvailable COM ports:")
    for i, port in enumerate(all_ports, 1):
        marker = " (Pico)" if port in detected else ""
        print(f"  [{i}] {port}{marker}")
    print()

    while True:
        choice = input(f"Select a port number (1-{len(all_ports)}) or type a COM port name: ").strip()
        if choice.isdigit():
            idx = int(choice) - 1
            if 0 <= idx < len(all_ports):
                return all_ports[idx]
        elif re.match(r"^COM\d+$", choice, re.IGNORECASE):
            return choice.upper()
        print("Invalid selection. Try again.")


def open_port(port_name: str) -> serial.Serial:
    ser = serial.Serial(port_name, baudrate=115200, timeout=3, write_timeout=2)
    ser.dtr = True
    time.sleep(0.5)
    ser.reset_input_buffer()
    return ser


def send_command(ser: serial.Serial, command: str, delay: float = 0.2) -> str:
    ser.reset_input_buffer()
    ser.write(f"{command}\n".encode())
    time.sleep(delay)
    return ser.read(ser.in_waiting).decode(errors="replace").strip()


def read_full_config(ser: serial.Serial) -> str:
    ser.reset_input_buffer()
    ser.write(b"CONF:READ?\n")

    config = ""
    deadline = time.time() + 10
    brace_depth = 0
    started = False

    while time.time() < deadline:
        chunk = ser.read(ser.in_waiting).decode(errors="replace")
        if chunk:
            config += chunk
            for ch in chunk:
                if ch == "{":
                    brace_depth += 1
                    started = True
                elif ch == "}":
                    brace_depth -= 1
            if started and brace_depth <= 0:
                break
        time.sleep(0.1)

    return config.strip()


def main():
    parser = argparse.ArgumentParser(
        description="Save a fixed fan duty cycle into FanPico flash."
    )
    parser.add_argument("--port", default="", help="COM port (auto-detect if omitted)")
    parser.add_argument("--duty", type=int, default=80, help="Duty cycle %% (0-100, default 80)")
    parser.add_argument("--fans", type=int, default=8, help="Number of fan outputs (default 8)")
    parser.add_argument("--backup-file", default="", help="Backup file path (default: fanpico_backup.json)")
    parser.add_argument("--skip-backup", action="store_true", help="Skip config backup")
    parser.add_argument("--restore", action="store_true", help="Restore a previously saved backup")
    args = parser.parse_args()

    if not 0 <= args.duty <= 100:
        print("ERROR: --duty must be between 0 and 100")
        sys.exit(1)

    backup_file = args.backup_file or os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "fanpico_backup.json"
    )
    com_port = args.port or resolve_com_port()

    ser = None
    try:
        log(f"Opening {com_port}...")
        ser = open_port(com_port)

        idn = send_command(ser, "*IDN?", delay=0.3)
        if idn:
            log(f"Connected to: {idn}")
        else:
            log("WARNING: No response to *IDN? -- continuing anyway")

        # --- Restore mode ---
        if args.restore:
            if not os.path.isfile(backup_file):
                log(f"ERROR: Backup file not found: {backup_file}")
                sys.exit(1)

            log(f"Restoring configuration from {backup_file}...")
            with open(backup_file, "r", encoding="utf-8") as f:
                json_data = f.read()

            ser.write(b"CONF:UPLOAD\n")
            time.sleep(0.5)
            ser.write(json_data.encode())
            ser.write(b"\n\n")
            time.sleep(2)
            resp = ser.read(ser.in_waiting).decode(errors="replace")
            log(f"Upload response: {resp}")

            log("Saving restored configuration to flash...")
            send_command(ser, "CONF:SAVE", delay=1.0)
            log("Configuration restored and saved.")
            return

        # --- Backup current config ---
        if not args.skip_backup:
            log("Backing up current configuration...")
            config_json = read_full_config(ser)

            if config_json and config_json.startswith("{"):
                with open(backup_file, "w", encoding="utf-8") as f:
                    f.write(config_json)
                log(f"Backup saved to: {backup_file}")
            else:
                log(f"WARNING: Could not read config. Response: {config_json}")
                answer = input("Continue without backup? (y/N): ").strip().lower()
                if answer != "y":
                    log("Aborted.")
                    sys.exit(1)

        # --- Set all fans to fixed duty cycle ---
        log(f"Setting all {args.fans} fans to FIXED {args.duty}%...")
        for i in range(1, args.fans + 1):
            cmd = f"CONF:FAN{i}:SOURCE FIXED,{args.duty}"
            send_command(ser, cmd, delay=0.1)
            log(f"  Fan {i} -> FIXED,{args.duty}")

        # --- Save to flash ---
        log("Saving configuration to flash (CONF:SAVE)...")
        send_command(ser, "CONF:SAVE", delay=1.0)
        log("Configuration saved to flash.")

        # --- Verify ---
        time.sleep(0.5)
        log("Verifying...")
        for i in range(1, args.fans + 1):
            resp = send_command(ser, f"CONF:FAN{i}:SOU?", delay=0.3)
            log(f"  Fan {i} source: {resp}")

        log("")
        log("=== Done ===")
        log(f"The FanPico will now boot all fans at {args.duty}% whenever it gets power.")
        log("FanControl will override this at runtime when it connects.")
        log("")
        log(f"Backup of previous config: {backup_file}")
        log("To restore:  python set_fanpico80.py --restore")

    except Exception as e:
        log(f"ERROR: {e}")
        sys.exit(1)
    finally:
        if ser and ser.is_open:
            ser.close()


if __name__ == "__main__":
    main()
