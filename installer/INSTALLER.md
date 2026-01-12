# FanTuner Installer Guide

## Building the Installer

### Prerequisites

1. **WiX Toolset v4** - Install via .NET tool:
   ```powershell
   dotnet tool install --global wix
   ```

2. **Build the projects first:**
   ```powershell
   dotnet build -c Release
   ```

### Build MSI

```powershell
cd installer
wix build Product.wxs -o FanTuner-1.0.0.msi
```

### Build with verbose output

```powershell
wix build Product.wxs -o FanTuner-1.0.0.msi -v
```

## Alternative: Manual Installation Script

If you prefer not to use WiX, use this PowerShell script:

```powershell
# Install-FanTuner.ps1
# Run as Administrator

param(
    [string]$InstallPath = "C:\Program Files\FanTuner",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

function Write-Status($message) {
    Write-Host "[*] $message" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host "[+] $message" -ForegroundColor Green
}

function Write-Error($message) {
    Write-Host "[-] $message" -ForegroundColor Red
}

# Check admin
if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "Please run as Administrator"
    exit 1
}

if ($Uninstall) {
    Write-Status "Uninstalling FanTuner..."

    # Stop and remove service
    Write-Status "Stopping service..."
    sc.exe stop FanTuner 2>$null
    Start-Sleep -Seconds 2

    Write-Status "Removing service..."
    sc.exe delete FanTuner 2>$null

    # Remove files
    if (Test-Path $InstallPath) {
        Write-Status "Removing files..."
        Remove-Item -Path $InstallPath -Recurse -Force
    }

    # Remove start menu shortcut
    $shortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FanTuner.lnk"
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force
    }

    Write-Success "FanTuner uninstalled successfully"
    exit 0
}

# Install
Write-Status "Installing FanTuner to $InstallPath..."

# Create directories
New-Item -ItemType Directory -Path "$InstallPath\Service" -Force | Out-Null
New-Item -ItemType Directory -Path "$InstallPath\UI" -Force | Out-Null

# Copy service files
Write-Status "Copying service files..."
$serviceSource = "..\src\FanTuner.Service\bin\Release\net8.0-windows\*"
Copy-Item -Path $serviceSource -Destination "$InstallPath\Service" -Recurse -Force

# Copy UI files
Write-Status "Copying UI files..."
$uiSource = "..\src\FanTuner.UI\bin\Release\net8.0-windows\*"
Copy-Item -Path $uiSource -Destination "$InstallPath\UI" -Recurse -Force

# Install service
Write-Status "Installing service..."
$servicePath = "$InstallPath\Service\FanTuner.Service.exe"
sc.exe create "FanTuner" binPath="$servicePath" start=auto DisplayName="FanTuner Fan Control Service"
sc.exe description "FanTuner" "Monitors system temperatures and controls fan speeds"

# Start service
Write-Status "Starting service..."
sc.exe start FanTuner

# Create start menu shortcut
Write-Status "Creating shortcut..."
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FanTuner.lnk")
$Shortcut.TargetPath = "$InstallPath\UI\FanTuner.exe"
$Shortcut.WorkingDirectory = "$InstallPath\UI"
$Shortcut.Description = "PC Fan Control Application"
$Shortcut.Save()

Write-Success "FanTuner installed successfully!"
Write-Host ""
Write-Host "Service status:"
sc.exe query FanTuner | Select-String "STATE"
Write-Host ""
Write-Host "To start the UI, run: $InstallPath\UI\FanTuner.exe"
Write-Host "Or find 'FanTuner' in the Start Menu"
```

### Usage

```powershell
# Install
.\Install-FanTuner.ps1

# Install to custom path
.\Install-FanTuner.ps1 -InstallPath "D:\Apps\FanTuner"

# Uninstall
.\Install-FanTuner.ps1 -Uninstall
```

## MSIX Package (Alternative)

For modern Windows packaging, you can create an MSIX package:

### Prerequisites

1. **Windows SDK** with MSIX tools
2. **Self-signed certificate** for testing

### Create MSIX

1. Create `AppxManifest.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">

  <Identity
    Name="FanTuner"
    Publisher="CN=FanTuner"
    Version="1.0.0.0" />

  <Properties>
    <DisplayName>FanTuner</DisplayName>
    <PublisherDisplayName>FanTuner</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22000.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="App"
      Executable="UI\FanTuner.exe"
      EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="FanTuner"
        Description="PC Fan Control Application"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
      <Extensions>
        <desktop:Extension Category="windows.startupTask" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10">
          <desktop:StartupTask TaskId="FanTunerStartup" Enabled="true" DisplayName="FanTuner" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>

</Package>
```

2. Create the MSIX:

```powershell
# Create certificate (one-time)
New-SelfSignedCertificate -Type Custom -Subject "CN=FanTuner" -KeyUsage DigitalSignature -FriendlyName "FanTuner" -CertStoreLocation "Cert:\CurrentUser\My"

# Package
MakeAppx.exe pack /d .\PackageContents /p FanTuner.msix

# Sign
SignTool.exe sign /fd SHA256 /a /f certificate.pfx /p password FanTuner.msix
```

## Post-Installation Verification

After installation, verify everything is working:

```powershell
# Check service
sc.exe query FanTuner

# Check config directory
dir "C:\ProgramData\FanTuner"

# View service logs
Get-Content "C:\ProgramData\FanTuner\logs\fantuner-service.log" -Tail 20

# Test named pipe
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "FanTuner", [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect(1000)
    Write-Host "Named pipe connection successful"
} catch {
    Write-Host "Named pipe connection failed: $_"
} finally {
    $pipe.Dispose()
}
```

## Troubleshooting Installation

### Service fails to install

1. Check for existing service:
   ```powershell
   sc.exe query FanTuner
   ```

2. If exists, remove first:
   ```powershell
   sc.exe stop FanTuner
   sc.exe delete FanTuner
   ```

### Service starts but crashes

1. Check Event Viewer:
   - Event Viewer > Windows Logs > Application
   - Look for FanTuner or .NET errors

2. Check log file:
   ```powershell
   Get-Content "C:\ProgramData\FanTuner\logs\fantuner-service.log"
   ```

3. Try running service directly:
   ```powershell
   & "C:\Program Files\FanTuner\Service\FanTuner.Service.exe"
   ```

### UI can't find service

1. Verify service is running:
   ```powershell
   Get-Service FanTuner
   ```

2. Check named pipe:
   ```powershell
   [System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -like "*FanTuner*" }
   ```
