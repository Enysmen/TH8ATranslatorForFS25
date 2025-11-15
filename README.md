# TH8ATranslatorFS25AxesCal

# TH8A → vJoy Axis Translator for Farming Simulator 25

A small console utility for Windows that turns the **Thrustmaster TH8A** shifter into a **virtual joystick axis** using **vJoy**, so that **Farming Simulator 25** can see and use it as a wheel/axis device even if the game does not handle the TH8A shifter directly.

The tool supports two modes:

- **H-pattern** (7 gears + Reverse): each gear is mapped to a fixed value of the vJoy X axis.
- **Sequential** (+ / –): pushing the lever forward/back produces a short axis deflection, returning to neutral when released.

You start the program manually before launching FS25, leave it running in the background, and close it when you finish playing.

---

## Features

- Reads TH8A via **DirectInput** (`SharpDX.DirectInput`).
- Automatically detects mode:
  - **H-pattern** – uses “gear buttons” (1–7, R).
  - **Sequential** – uses + / – buttons on the shifter.
- Emulates **vJoy Device 1**:
  - uses **X axis** to represent gear/shift state;
  - neutral always maps to the center of the axis.
- Console debug output:
  - H-pattern:
    - `[H] Gear => 1 / 2 / ... / R / N`
  - Sequential:
    - `[SEQ] State => + / - / N`

---

## Status & Limitations

Current state:

- Primary goal: working bridge **TH8A → vJoy → FS25**.
- Mapping of TH8A buttons to gears is hard-coded (no config file yet).
- Only **Windows** is supported.
- TH8A must be connected via **USB to PC**, not through a wheel base.
- Target runtime: **.NET Framework 4.8**, **x86** build.

Planned improvements:

- External config file for:
  - vJoy device ID;
  - axis selection (X/Y/Z/etc.);
  - custom mapping: “TH8A button → gear number”.
- Better diagnostics (live dump of button/axis values).
- Optional GUI for configuration.

---

## Requirements

### Hardware

- Windows PC.
- **Thrustmaster TH8A** shifter (USB mode).
- **Farming Simulator 25**.
- Installed **vJoy** driver.

### Software / Libraries

- **.NET Framework 4.8** (for building the project).
- Visual Studio 2022 / 2026 (or any IDE that supports .NET Framework).
- NuGet package:
  - `SharpDX.DirectInput`
- vJoy SDK binaries:
  - `vJoyInterface.dll` (native x86 DLL),
  - `vJoyInterfaceWrap.dll` (managed wrapper for .NET Framework x86).

---

## Installation & Build

### 1. Install and configure vJoy

1. Download and install vJoy from the official source (SourceForge / vjoystick site).
2. Open **Configure vJoy**.
3. Select **Device 1**:
   - enable the device (checkbox *Enable vJoy Device*);
   - enable **X** axis (you may disable other axes if you want a “clean” device);
   - button count may be left at default (or e.g. 8).
4. Press **Apply**.
5. Verify that **vJoy Virtual Joystick** appears in `joy.cpl`:
   - `Win + R` → `joy.cpl` → Enter.

### 2. Clone / open the project

1. Clone or download this repository to any folder.
2. Open the `.sln` file in Visual Studio.

Make sure the project is a **Console App (.NET Framework)** targeting **.NET Framework 4.8**.

### 3. Set platform target to x86

1. Right-click the project → **Properties**.
2. **Build** tab:
   - **Platform target** = `x86`.
3. Save the changes.

### 4. Install SharpDX.DirectInput

1. Right-click the project → **Manage NuGet Packages…**
2. **Browse** tab → search for `SharpDX.DirectInput`.
3. Install the package into the project.

### 5. Add vJoy DLLs

1. From the vJoy SDK, locate the **x86** versions of:
   - `vJoyInterface.dll`
   - `vJoyInterfaceWrap.dll`

   Typically they are under something like  
   `C:\Program Files\vJoy\SDK\C#\x86\` or similar.

2. Copy both DLLs into a folder inside the project, e.g. `.\libs\`.

3. Add the files to the project:
   - Right-click the project → **Add → Existing Item…**
   - Select both DLLs.
   - Confirm.

4. For both DLL files, set properties:
   - **Build Action** = `None`
   - **Copy to Output Directory** = `Copy if newer`

5. Add a **Reference** only to `vJoyInterfaceWrap.dll`:
   - Right-click **References** → **Add Reference…**
   - Choose the **Browse** section → **Browse…**
   - Select `vJoyInterfaceWrap.dll` from your `libs` folder.
   - Confirm.

6. In your main code file (`Program.cs`), add:
   ```csharp
   using SharpDX.DirectInput;
   using vJoyInterfaceWrap;


