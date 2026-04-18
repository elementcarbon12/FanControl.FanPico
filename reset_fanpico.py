#!/usr/bin/env python3
"""
Resets a FanPico device to factory defaults or restores a saved backup.

Factory reset: erases flash config and reboots (CONF:DEL + *RST).
Restore:       uploads a JSON backup and saves to flash.

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


def main():
    parser = argparse.ArgumentParser(
        description="Reset a FanPico to factory defaults or restore a backup."
    )
    parser.add_argument("--port", default="", help="COM port (auto-detect if omitted)")
    parser.add_argument("--backup-file", default="", help="Restore this JSON config instead of factory resetting")
    parser.add_argument("--force", action="store_true", help="Skip confirmation prompt")
    args = parser.parse_args()

    is_restore = bool(args.backup_file)

    if is_restore and not os.path.isfile(args.backup_file):
        log(f"ERROR: Backup file not found: {args.backup_file}")
        sys.exit(1)

    com_port = args.port or resolve_com_port()

    if is_restore:
        action = f"Restore configuration from: {args.backup_file}"
    else:
        action = "Factory reset (erase saved config and reboot)"

    print(f"\n  FanPico on {com_port}")
    print(f"  Action: {action}\n")

    if not args.force:
        answer = input("Proceed? (y/N): ").strip().lower()
        if answer != "y":
            log("Aborted.")
            sys.exit(0)

    ser = None
    try:
        log(f"Opening {com_port}...")
        ser = open_port(com_port)

        idn = send_command(ser, "*IDN?", delay=0.3)
        if idn:
            log(f"Connected to: {idn}")
        else:
            log("WARNING: No response to *IDN? -- continuing anyway")

        if is_restore:
            log(f"Uploading configuration from {args.backup_file}...")
            with open(args.backup_file, "r", encoding="utf-8") as f:
                json_data = f.read()

            ser.write(b"CONF:UPLOAD\n")
            time.sleep(0.5)
            ser.write(json_data.encode())
            ser.write(b"\n\n")
            time.sleep(2)
            resp = ser.read(ser.in_waiting).decode(errors="replace")
            log(f"Upload response: {resp}")

            log("Saving to flash...")
            send_command(ser, "CONF:SAVE", delay=1.0)
            log("Configuration restored and saved to flash.")
        else:
            log("Deleting saved configuration from flash (CONF:DEL)...")
            send_command(ser, "CONF:DEL", delay=0.5)

            log("Rebooting device (*RST)...")
            ser.write(b"*RST\n")
            time.sleep(0.2)

            log("")
            log("=== Factory reset complete ===")
            log("The device is rebooting with default configuration.")
            log("Fan sources will default to following MBFAN inputs.")

    except Exception as e:
        log(f"ERROR: {e}")
        sys.exit(1)
    finally:
        if ser and ser.is_open:
            ser.close()


if __name__ == "__main__":
    main()
