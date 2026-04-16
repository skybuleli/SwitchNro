# Contributing to SwitchNro

Thanks for your interest in contributing! This guide covers how to set up, develop, and submit changes.

## Prerequisites

- macOS 15+ on Apple Silicon
- .NET 10 SDK (Preview)
- Xcode Command Line Tools
- SDL2 (`brew install sdl2`)

## Getting Started

```bash
git clone https://github.com/skybuleli/SwitchNro.git
cd SwitchNro
dotnet build SwitchNro.sln
dotnet test SwitchNro.sln
```

All 17 tests should pass before you start making changes.

## Development Workflow

1. **Create a branch** — Use a descriptive name: `feature/shader-cache`, `fix/svc-dispatch`, etc.
2. **Make your changes** — Follow the coding conventions below.
3. **Build & test** — Ensure `dotnet build` and `dotnet test` both succeed with zero errors/warnings.
4. **Commit** — Use [Conventional Commits](https://www.conventionalcommits.org/):
   - `feat: add Vulkan swapchain support`
   - `fix: resolve page fault on unaligned access`
   - `docs: update README with Vulkan prerequisites`
   - `refactor: extract TLB into separate class`
   - `test: add NroLoader edge case tests`
5. **Push & open a Pull Request** — Describe what changed and why.

## Coding Conventions

- **Language**: C# 13 / .NET 10 preview features are fine.
- **Style**: Follow the existing code style in the project:
  - File-scoped namespaces
  - XML doc comments on public APIs (Chinese comments are welcome)
  - `nullable enable` — treat warnings as errors
  - `latest-recommended` analysis level — fix all CA warnings
- **Naming**:
  - PascalCase for types, methods, properties, and public fields
  - camelCase for local variables and parameters
  - `_camelCase` for private fields
  - `I` prefix for interfaces (`IExecutionEngine`)
- **Project structure**: One namespace per project (e.g., `SwitchNro.Memory`).
- **Unsafe code**: Allowed globally (`AllowUnsafeBlocks` is on). Prefer safe code when possible; use `unsafe` only for interop or performance-critical paths.
- **Dependencies**: Avoid adding NuGet packages without discussing first. Check `Directory.Build.props` for shared settings.

## Architecture Notes

SwitchNro is organized as a layered emulator:

```
UI (Avalonia)
  → Horizon OS (process management, SVC dispatch)
    → HLE Services (IPC: sm, fs, vi, hid, …)
      → CPU Engine (HVF hypervisor)
        → Virtual Memory Manager (page table + TLB)
          → NRO Loader (ASLR + relocation)
```

- **Don't break the HVF interface** — `IExecutionEngine` is the contract; `HvfExecutionEngine` is the macOS implementation.
- **HLE services** go in `SwitchNro.HLE/Services/`. Implement `IIpcService` and register in `MainWindow.axaml.cs`.
- **New GPU backends** should implement `IRenderer` from `SwitchNro.Graphics.GAL`.

## Testing

- Tests live in `src/SwitchNro.Tests/`.
- Run with `dotnet test SwitchNro.sln`.
- Add tests for bug fixes and new features when practical.
- Test method naming: `MethodName_Scenario_ExpectedResult` (CA1707 is suppressed in the test project).

## Reporting Issues

- Use [GitHub Issues](https://github.com/skybuleli/SwitchNro/issues).
- Include: macOS version, .NET SDK version, reproduction steps, and relevant log output.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
