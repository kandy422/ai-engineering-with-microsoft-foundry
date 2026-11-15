# Module 01 · Run the C# starter

Use Windows and PowerShell 7. Install the .NET 10 SDK for your processor, then reopen PowerShell. The .NET runtime alone cannot build the starter. The lab pins the 10.0.4xx SDK feature band with 10.0.403 as the minimum and allows current patches in that band. Install the latest patch in the band. The lab has no external NuGet dependencies. [Download .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) · [Install PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/install-powershell-on-windows)

Extract the learner package and open its `START-HERE.html`. Follow the Module 01 entry and open PowerShell in the folder containing this guide and the `lab` folder. Check the working directory and toolchain.

```powershell
Get-Location
Test-Path ./lab/Starter/Starter.csproj
$PSVersionTable.PSVersion
dotnet --version
```

The path check should print `True`. Run the checkpoint from the same folder.

```powershell
pwsh -NoProfile -File ./lab/run-starter.ps1
```

The script builds the project, starts the mock API on `127.0.0.1:8401`, runs the checks, and stops its own server. Look for `PASS: local starter smoke checks`. The results and logs appear under `qa/`, which the script creates.

| Check | Expected result |
|---|---|
| Known order `ORD-1042` | HTTP 200 with JSON status `in_transit` |
| Unknown order `ORD-9999` | HTTP 404 and `order not found` |
| Empty order ID | HTTP 400 and `orderId is required` |
| `ORDER_API_BASE=missing-scheme` | Process exit 2 and a configuration error before any request |
| Restore the localhost URL | The same three HTTP responses in the same order |

To see the request yourself, use two PowerShell terminals in this folder. In terminal one, build and start the server.

```powershell
dotnet build ./lab/Starter/Starter.csproj --nologo --configfile ./lab/Starter/NuGet.Config
dotnet ./lab/Starter/bin/Debug/net10.0/Starter.dll
```

Once it prints `Now listening on: http://127.0.0.1:8401`, use terminal two.

```powershell
$env:ORDER_API_BASE = 'http://127.0.0.1:8401'
dotnet ./lab/Starter/bin/Debug/net10.0/Starter.dll --check
$env:ORDER_API_BASE = 'missing-scheme'
dotnet ./lab/Starter/bin/Debug/net10.0/Starter.dll --check
$LASTEXITCODE
$env:ORDER_API_BASE = 'http://127.0.0.1:8401'
dotnet ./lab/Starter/bin/Debug/net10.0/Starter.dll --check
Remove-Item Env:ORDER_API_BASE
```

Stop terminal one's server with Ctrl+C. Open `lab/Starter/Program.cs` to trace how the address is validated and how the client sends `POST /lookup`. No Azure credentials, Foundry deployment, or paid resource is needed. Lesson 7.4 covers the authenticated Foundry adapter.

- If the path check is false, open the Module 01 folder from the package and retry.
- If the SDK is missing, reopen PowerShell after installation and check `dotnet --list-sdks`. Install a current .NET 10 SDK from the 10.0.4xx band.
- If port 8401 is occupied, stop the earlier lab server before running the checkpoint. For a startup failure, inspect `qa/starter-server-errors.txt`.
- If the downloaded script is blocked, use the manual two-terminal commands after reviewing the source.
- For the optional ticket-routing experiment, install Python 3.12 and run `py -3.12 lab/experiments.py`. It uses the standard library and `lab/tickets.json`.
