param (
    [string]$dotnet = "4.8",
    [string]$platform = "windows",
    [string]$steam_appid = "0",
    [string]$steam_branch = "public",
    [string]$steam_depot = "",
    [string]$steam_access = "anonymous",
    [string]$deps_dir = $null
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Check PowerShell version
$ps_version = $PSVersionTable.PSVersion.Major
if ($ps_version -le 5) {
    Write-Host "Error: PowerShell version 6 or higher required to continue, $ps_version currently installed"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode) }
    exit 1
}

# Format project name and set depot ID if provided
if ($steam_depot) { $steam_depot = "-depot $steam_depot" }

# Set directory/file variables and create directories
$root_dir = $PSScriptRoot
$temp_dir = Join-Path $root_dir "../temp"
$tools_dir = Join-Path $temp_dir "tools"
if ($null -eq $deps_dir) {
    $deps_dir = Join-Path $temp_dir "raw-deps"
}
else {
    $deps_dir = Join-Path $PSScriptRoot $deps_dir
}
$platform_dir = Join-Path $deps_dir $platform
if (!(Test-Path $temp_dir)) {
    New-Item "$temp_dir" -ItemType Directory -Force | Out-Null
}
if (!(Test-Path $tools_dir)) {
    New-Item "$tools_dir" -ItemType Directory -Force | Out-Null
}

# Set URLs of dependencies and tools to download
$steam_depotdl_url = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_2.4.6/depotdownloader-2.4.6.zip"

function Get-Downloader {
    # Check if DepotDownloader is already downloaded
    $steam_depotdl_dll = Join-Path $tools_dir "DepotDownloader.dll"
    $steam_depotdl_zip = Join-Path $tools_dir "DepotDownloader.zip"
    if (!(Test-Path $steam_depotdl_dll) -or (Get-Item $steam_depotdl_dll).LastWriteTime -lt (Get-Date).AddDays(-7)) {
        # Download and extract DepotDownloader
        Write-Host "Downloading latest version of DepotDownloader"
        try {
            Invoke-WebRequest $steam_depotdl_url -OutFile $steam_depotdl_zip -UseBasicParsing
        }
        catch {
            Write-Host "Error: Could not download DepotDownloader"
            Write-Host $_.Exception | Format-List -Force
            if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode) }
            exit 1
        }

        # TODO: Compare size and hash of .zip vs. what GitHub has via API
        Write-Host "Extracting DepotDownloader release files"
        Expand-Archive $steam_depotdl_zip -DestinationPath $tools_dir -Force
        (Get-Item $steam_depotdl_dll).LastWriteTime = (Get-Date)

        if (!(Test-Path $steam_depotdl_zip)) {
            Get-Downloader # TODO: Add infinite loop prevention
            return
        }

        # Cleanup downloaded .zip file
        Remove-Item $steam_depotdl_zip
    }
    else {
        Write-Host "Recent version of DepotDownloader already downloaded"
    }

    Get-Dependencies
}

function Get-Dependencies {
    # Cleanup existing game files, else they are not always the latest
    # Remove-Item $managed_dir -Recurse -Force
    $fileListPath = Join-Path $tools_dir "filelist" 
    Set-Content -Path $fileListPath -Value "regex:RustDedicated_Data/Managed/.+\.dll"

    # Attempt to run DepotDownloader to get game DLLs
    try {
        $depoArgsList = "`"$steam_depotdl_dll`" $steam_access -app $steam_appid -branch $steam_branch $steam_depot -os $platform -dir `"$platform_dir`" -filelist `"$fileListPath`""
        Write-Host $depoArgsList
        Start-Process dotnet -WorkingDirectory "$tools_dir" -ArgumentList $depoArgsList -NoNewWindow -Wait
    }
    catch {
        Write-Host "Error: Could not start or complete getting dependencies"
        Write-Host $_.Exception | Format-List -Force
        if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode) }
        exit 1
    }
}

Get-Downloader