#requires -version 4
[CmdletBinding(SupportsShouldProcess = $true)]
param (
    [string]$ResolverPath = $null,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

Import-Module "$PSScriptRoot/KoreBuild.psm1"

if (!$ResolverPath) {
    $ResolverPath = Join-Paths $PSScriptRoot ('..', 'tools', 'KoreBuild.SdkResolver', 'net46')
}

if (!(Test-Path $ResolverPath)) {
    Write-Error "Could not find the KoreBuild SDK resolver in $ResolverPath"
}

$ResolverPath = Resolve-Path $ResolverPath

try {
    $vswhere = Join-Paths ${env:ProgramFiles(x86)} ('Microsoft Visual Studio', 'Installer', 'vswhere.exe')
    if (!(Test-Path $vswhere)) {
        Write-Warning "vswhere.exe not found. No installations of VS could be detected."
        exit 0
    }
    $vsInstalls = & $vswhere -all -prerelease -format json | ConvertFrom-Json

    if (!$vsInstalls) {
        Write-Warning "No installation of VS detected in '$vsBasePath'. Skipping install."
        exit 0
    }

    Write-Host ""
    Write-Host "Found the following installations of VS:"
    Write-Host ""
    $vsInstalls | % {
        Write-Host "  - $($_.displayName)"
        Write-Host "        path    = $($_.installationPath)"
        Write-Host "        version = $($_.installationVersion)"
    }

    Write-Host ""

    $yesToAll = $false
    $noToAll = $false

    $vsInstalls | % {
        [string] $msbuildToolsFolder = Resolve-Path (Join-Paths $_.installationPath ('MSBuild', '*', 'Bin')) | Select-Object -First 1
        if ($Force -or $PSCmdlet.ShouldContinue($_.installationPath, "Install KoreBuild SDK Resolver?", [ref]$yesToAll, [ref]$noToAll)) {
            Write-Verbose "Installed to $msbuildToolsFolder"
            $installed = __install_sdk_resolver -MSBuildToolsFolder $msbuildToolsFolder -ResolverPath $ResolverPath -Force:$Force
            if ($installed) {
                Write-Host -ForegroundColor Green "Installed."
            } else {
                Write-Host "Already up to date. Skipping."
            }
        }
    }
}
catch {
    [bool]$IsAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    if (!$IsAdmin) {
        if ($PSCmdlet.ShouldContinue("Re-launching this script as an admin?", "Installation failed")) {
            $thisFile = Join-Path $PSScriptRoot $MyInvocation.MyCommand.Name

            # Intentionally leaving of -Force
            Start-Process `
                -Verb runas `
                -FilePath "powershell.exe" `
                -ArgumentList ('-File', $thisFile, $ResolverPath)
        }
        else {
            throw
        }
    }
    else {
        $Error | Write-Host -ForegroundColor Red
        Read-Host -Prompt "(Press ENTER to exit)"
        exit 1
    }
}
