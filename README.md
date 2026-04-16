# SwitchNro

A Nintendo Switch homebrew emulator for **macOS ARM64**, built with .NET 10 and native Apple Silicon APIs.

SwitchNro leverages **Hypervisor.framework** (HVF) to execute ARM64 NRO homebrew at near-native speed by mapping guest code directly onto a virtual CPU — no JIT compilation or instruction translation needed.

## Architecture

```
┌──────────────────────────────────────────────────┐
│                    UI (Avalonia)                  │
├──────────────────────────────────────────────────┤
│  Debugger  │  Input  │  Audio (SDL2 / CoreAudio) │
├──────────────────────────────────────────────────┤
│             Horizon OS Emulation Core             │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────┐ │
│  │ SVC Dispatch │  │ IPC Services │  │ Process  │ │
│  └─────────────┘  │ sm · fs · vi │  │ Manager  │ │
│                   │ hid · …      │  └─────────┘ │
│  ┌─────────────────────────────────────────────┐ │
│  │          HLE Service Layer                   │ │
│  └─────────────────────────────────────────────┘ │
├──────────────────────────────────────────────────┤
│              NRO Loader (ASLR · Relocation)      │
├──────────────────────────────────────────────────┤
│  CPU Engine (HVF)  │  Graphics (Metal / Vulkan)  │
├──────────────────────────────────────────────────┤
│  Virtual Memory Manager  │  Shader Compiler      │
│  Page Table · TLB        │  Texture Manager       │
└──────────────────────────────────────────────────┘
```

## Project Structure

| Project | Description |
|---|---|
| `SwitchNro.Common` | Shared utilities, logging, configuration, `ResultCode` |
| `SwitchNro.Cpu` | CPU execution interface + HVF hypervisor backend |
| `SwitchNro.Memory` | Virtual memory manager, page table, TLB, physical allocator |
| `SwitchNro.NroLoader` | NRO parser, ELF segment mapper, ASLR, relocation |
| `SwitchNro.Horizon` | Horizon OS core — process lifecycle, SVC dispatch loop |
| `SwitchNro.HLE` | High-level emulation: IPC framework, service implementations |
| `SwitchNro.Graphics.GAL` | GPU abstraction layer (command buffers, pipelines) |
| `SwitchNro.Graphics.Metal` | Metal GPU backend |
| `SwitchNro.Graphics.Vulkan` | Vulkan GPU backend (MoltenVK) |
| `SwitchNro.Graphics.Shader` | Shader compiler + disk cache |
| `SwitchNro.Graphics.Texture` | Texture manager, format conversion |
| `SwitchNro.Input` | Input manager, keyboard-to-JoyCon mapping |
| `SwitchNro.Audio` | Audio backend interface |
| `SwitchNro.Audio.SDL2` | SDL2 audio output |
| `SwitchNro.Audio.CoreAudio` | CoreAudio low-latency output |
| `SwitchNro.Debugger` | Debugger service (breakpoints, stepping) |
| `SwitchNro.UI` | Avalonia UI main window |
| `SwitchNro.Tests` | Unit tests |

## Prerequisites

- **macOS 15+** (Sequoia or later) on Apple Silicon
- **.NET 10 SDK** (Preview)
- **Xcode Command Line Tools** (for HVF and Metal)
- **SDL2** (for audio): `brew install sdl2`

## Build

```bash
# Clone
git clone https://github.com/skybuleli/SwitchNro.git
cd SwitchNro

# Restore & Build
dotnet build SwitchNro.sln

# Run Tests
dotnet test SwitchNro.sln
```

The solution defaults to `osx-arm64` via `Directory.Build.props`. No additional flags are needed.

## Run

```bash
dotnet run --project src/SwitchNro.UI
```

Then open an `.nro` file via the UI or drag-and-drop it onto the window.

## Key Design Decisions

- **HVF over JIT** — Apple Silicon's Hypervisor.framework lets us run ARM64 guest code directly on the CPU, eliminating the complexity and overhead of a JIT compiler.
- **Page-table-based memory** — Guest virtual addresses are translated through a software page table + TLB, then mapped into HVF's physical address space. This mirrors the Switch's own memory model.
- **SVC dispatch loop** — The CPU execution loop intercepts `SVC` instructions, dispatches them to the HLE service layer, and resumes — matching how Horizon OS actually works.
- **ASLR for NRO** — Homebrew modules are loaded at randomized base addresses (25-bit entropy, 4KB-aligned) just like the real Horizon loader.

## Status

This project is in early development. Core subsystems (CPU, memory, NRO loading, Horizon process management) are functional. Many HLE services and GPU features are stubbed or in progress.

## License

MIT
