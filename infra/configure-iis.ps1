# ============================================================
# configure-iis.ps1
# Installs IIS, .NET 10 Hosting Bundle, and creates an IIS site
# for either the Publisher or Subscriber ASP.NET application.
#
# Parameters:
#   AppRole - "publisher" or "subscriber"
# ============================================================
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("publisher", "subscriber")]
    [string]$AppRole
)

$ErrorActionPreference = "Stop"

Write-Host "==> Configuring IIS for role: $AppRole"

# ============================================================
# Install IIS and required features
# ============================================================
Write-Host "==> Installing IIS features..."

$features = @(
    "Web-Server",
    "Web-WebServer",
    "Web-Common-Http",
    "Web-Default-Doc",
    "Web-Dir-Browsing",
    "Web-Http-Errors",
    "Web-Static-Content",
    "Web-Health",
    "Web-Http-Logging",
    "Web-Performance",
    "Web-Stat-Compression",
    "Web-Security",
    "Web-Filtering",
    "Web-App-Dev",
    "Web-Net-Ext45",
    "Web-Asp-Net45",
    "Web-ISAPI-Ext",
    "Web-ISAPI-Filter",
    "Web-Mgmt-Tools",
    "Web-Mgmt-Console"
)

foreach ($feature in $features) {
    Install-WindowsFeature -Name $feature -IncludeManagementTools -ErrorAction SilentlyContinue | Out-Null
}

Write-Host "    IIS features installed."

# ============================================================
# Install .NET 10 Windows Hosting Bundle
# ============================================================
Write-Host "==> Downloading .NET 10 Hosting Bundle..."

$dotnetInstallerUrl = "https://download.visualstudio.microsoft.com/download/pr/dotnet-hosting-10.0-win.exe"
$installerPath = "$env:TEMP\dotnet-hosting-10.exe"

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    Invoke-WebRequest -Uri $dotnetInstallerUrl -OutFile $installerPath -UseBasicParsing
}
catch {
    # Fallback: use winget if available
    Write-Host "    Direct download failed, trying winget..."
    winget install Microsoft.DotNet.HostingBundle.10 --silent --accept-package-agreements --accept-source-agreements
    $installerPath = $null
}

if ($installerPath -and (Test-Path $installerPath)) {
    Write-Host "==> Installing .NET 10 Hosting Bundle..."
    Start-Process -FilePath $installerPath -ArgumentList "/quiet /norestart" -Wait
    Remove-Item $installerPath -Force
    Write-Host "    .NET 10 Hosting Bundle installed."
}

# ============================================================
# Create application directory
# ============================================================
$appName = $AppRole.Substring(0, 1).ToUpper() + $AppRole.Substring(1)   # Publisher / Subscriber
$appPath = "C:\inetpub\apps\$appName"

Write-Host "==> Creating application directory: $appPath"
New-Item -ItemType Directory -Path $appPath -Force | Out-Null

# Grant IIS_IUSRS read/execute on the app folder
icacls $appPath /grant "IIS_IUSRS:(OI)(CI)RX" /T | Out-Null

# ============================================================
# Create IIS Application Pool (.NET CLR "No Managed Code" for ASP.NET Core)
# ============================================================
Import-Module WebAdministration

$poolName = "AppPool-$appName"
Write-Host "==> Creating IIS Application Pool: $poolName"

if (-not (Test-Path "IIS:\AppPools\$poolName")) {
    New-WebAppPool -Name $poolName | Out-Null
}

Set-ItemProperty "IIS:\AppPools\$poolName" -Name "managedRuntimeVersion" -Value ""
Set-ItemProperty "IIS:\AppPools\$poolName" -Name "enable32BitAppOnWin64" -Value $false
Set-ItemProperty "IIS:\AppPools\$poolName" -Name "startMode" -Value "AlwaysRunning"

# Run the app pool as LocalSystem so it can use the VM's Managed Identity
Set-ItemProperty "IIS:\AppPools\$poolName" -Name "processModel.identityType" -Value 0  # LocalSystem

Write-Host "    App pool created."

# ============================================================
# Create IIS Website
# ============================================================
$siteName = "Demo-$appName"
$port = 80

Write-Host "==> Creating IIS site: $siteName on port $port"

# Remove Default Web Site if it occupies port 80
$defaultSite = Get-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
if ($defaultSite -and ($defaultSite.Bindings.Collection | Where-Object { $_.bindingInformation -like "*:80:*" })) {
    Write-Host "    Stopping and removing Default Web Site..."
    Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
    Remove-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
}

if (-not (Get-Website -Name $siteName -ErrorAction SilentlyContinue)) {
    New-Website `
        -Name $siteName `
        -PhysicalPath $appPath `
        -ApplicationPool $poolName `
        -Port $port `
        -Force | Out-Null
}

Start-Website -Name $siteName -ErrorAction SilentlyContinue
Write-Host "    IIS site '$siteName' created and started."

# ============================================================
# Configure Windows Firewall
# ============================================================
Write-Host "==> Opening firewall for HTTP (80) and HTTPS (443)..."
New-NetFirewallRule -DisplayName "Allow HTTP" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "Allow HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow -ErrorAction SilentlyContinue | Out-Null

# ============================================================
# Enable ANCM (ASP.NET Core Module) logging for troubleshooting
# ============================================================
$webConfigPath = "$appPath\web.config"
if (-not (Test-Path $webConfigPath)) {
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\$appName.exe"
                  stdoutLogEnabled="true"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
"@ | Out-File -FilePath $webConfigPath -Encoding utf8
}

New-Item -ItemType Directory -Path "$appPath\logs" -Force | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host " IIS Configuration Complete for: $appName"
Write-Host "   App path    : $appPath"
Write-Host "   App pool    : $poolName"
Write-Host "   Site name   : $siteName"
Write-Host "   Port        : $port"
Write-Host ""
Write-Host " Next: Copy published app files to $appPath"
Write-Host "       then browse to http://localhost"
Write-Host "============================================================"
