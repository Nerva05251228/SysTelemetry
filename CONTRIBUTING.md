# Contributing to SysTelemetry

Thank you for your interest in contributing to **SysTelemetry**!

## Build Environment Constraint

To keep **SysTelemetry** zero-dependency to build on any stock Windows 10/11 machine without requiring Visual Studio or the .NET SDK, `build.ps1` compiles directly using the built-in .NET Framework 4.x compiler:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

Because that compiler targets **C# 5**, please avoid C# 6+ syntax in pull requests:
- Use `string.Format(...)` instead of `$""` interpolated strings
- Use explicit null checks instead of `?.` / `?[]`
- Use `if (Event != null) Event()` instead of `Event?.Invoke()`
- Avoid expression-bodied members (`=>` on properties/methods), `out var`, and pattern matching (`is Type x`)

## Local Build & Verification

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Compiled binaries are output to `dist\`.

## UI & Layout Guidelines

- Preserve fixed column widths in `UI\WidgetVisualBuilder.cs` so dynamic metrics (CPU/GPU percentage, temperature, and network speed) never cause horizontal jitter on the taskbar or desktop overlay.
- Keep all interactive surfaces cleanly styled for both Windows Dark and Light themes via `Services\ThemeHelper.cs`.
