# Contributing to ProximityD

Thank you for your interest in contributing to ProximityD!

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/your-username/proximityd.git`
3. Create a feature branch: `git checkout -b feature/your-feature`

## Development Setup

### Prerequisites
- Windows 10/11 (for full app development)
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- Bluetooth 4.0+ adapter

### Building

```bash
# Build the app (Windows only)
dotnet build src/ProximityD/ProximityD.csproj

# Run tests (cross-platform)
dotnet test tests/ProximityD.Tests/ProximityD.Tests.csproj
```

## Code Style

- Follow C# conventions and the `.editorconfig` rules
- Use `var` when the type is apparent
- Always use braces for control flow
- Add XML doc comments to all public APIs

## Testing

- Tests run cross-platform on Linux (CI) and Windows
- WPF-specific code lives in `.xaml.cs` files only — do NOT test WPF code
- Tests use xUnit + FluentAssertions + Moq
- New features must include tests

## Pull Request Guidelines

1. Keep PRs focused on a single change
2. Write descriptive commit messages
3. Ensure all tests pass: `dotnet test tests/ProximityD.Tests/ProximityD.Tests.csproj`
4. Update docs if relevant

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
