# Windows Credential Provider Integration

## Overview

ProximityD currently signals device presence to encourage Windows Hello authentication. For fully automatic unlock (without user interaction), a custom Windows Credential Provider is required.

## What is a Windows Credential Provider?

A Windows Credential Provider (CP) is a COM DLL that plugs into the Windows logon infrastructure (winlogon.exe) to provide alternative authentication methods. Microsoft ships several built-in CPs (password, PIN, Windows Hello) and allows third parties to register their own.

## Architecture

```
ProximityD.exe ──────────► Named Pipe / IPC ──────────► ProximityD.CredentialProvider.dll
  (user session)                                          (winlogon session - SYSTEM)
  
  Detects BT device                                      Receives "device present" signal
  Signal via IPC ──────────────────────────────────────► Auto-submit credentials
```

## Implementation Steps

### 1. Create the COM DLL

```csharp
// ProximityD.CredentialProvider project (separate from main app)
[ComVisible(true)]
[Guid("YOUR-GUID-HERE")]
[ClassInterface(ClassInterfaceType.None)]
public class ProximityDCredentialProvider : ICredentialProvider
{
    // Implement ICredentialProvider interface
    // See: https://docs.microsoft.com/en-us/windows/win32/api/credentialprovider/nn-credentialprovider-icredentialprovider
}
```

### 2. Register the Credential Provider

```reg
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{YOUR-GUID}]
@="ProximityD Credential Provider"
```

### 3. IPC Between App and CP

Use a named pipe or Windows event to signal the credential provider:

```csharp
// In ProximityD main app (when device detected)
using var pipe = new NamedPipeClientStream(".", "ProximityDCP", PipeDirection.Out);
pipe.Connect(timeout: 500);
var writer = new StreamWriter(pipe);
writer.WriteLine("UNLOCK");
writer.Flush();
```

```csharp
// In Credential Provider DLL
var pipe = new NamedPipeServerStream("ProximityDCP", PipeDirection.In);
pipe.WaitForConnection();
var reader = new StreamReader(pipe);
var message = reader.ReadLine();
if (message == "UNLOCK") AutoSubmitCredentials();
```

## Security Considerations

1. **Named pipe security**: Restrict access to the pipe to prevent unauthorized unlock
2. **Credential storage**: Never store plaintext credentials; use DPAPI or Windows Hello keys
3. **Fail-secure**: If IPC fails, the CP must NOT auto-submit — fall back to standard logon
4. **Tamper detection**: Validate the signal source before acting

## References

- [Custom Credential Provider Sample (C++)](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/CredentialProvider)
- [ICredentialProvider interface](https://docs.microsoft.com/en-us/windows/win32/api/credentialprovider/nn-credentialprovider-icredentialprovider)
- [Credential Provider Technical Reference](https://docs.microsoft.com/en-us/windows/security/identity-protection/credential-guard/credential-guard-considerations)
- [Windows Hello companion device framework](https://docs.microsoft.com/en-us/windows-hardware/design/device-experiences/windows-hello-companion-device-framework)

## Current Status

This feature is tracked in the roadmap but not yet implemented. The current approach (showing a notification to prompt manual Windows Hello authentication) is the recommended path for most users.
