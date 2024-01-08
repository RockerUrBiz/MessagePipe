<#
.SYNOPSIS
    Downloads the latest nuget.exe to a specified directory, registers a NuGet repository at the global-packages location, and cleans up old package versions.

.DESCRIPTION
    This script downloads the latest version of nuget.exe to a directory specified by an environment variable, then uses it to manage NuGet package sources and remove older versions of a package from the global NuGet repository.

.PARAMETER RepoName
    The name of the repository to register. This is also used to identify the package for cleanup.

.EXAMPLE
    .\RegisterNuGetRepoV2.ps1 -RepoName "YourProjectName"

.NOTES
    Requires an environment variable 'NuGet_Path' that points to the directory where 'nuget.exe' should be downloaded and stored.
    The 'versionsToKeep' variable controls how many recent versions of the package to retain.
#>

param (
    [Parameter(Mandatory=$true)]
    [string]$RepoName
)

# Retrieve the environment variable for the NuGet executable directory
$nugetDir = [Environment]::GetEnvironmentVariable("NuGet_Path", "Machine")

# Validate the directory path from the environment variable
if ([string]::IsNullOrWhiteSpace($nugetDir)) {
    Write-Error "The NuGet directory is not set in the system variables."
    exit 1
}

# Ensure the directory exists
if (-not (Test-Path $nugetDir)) {
    New-Item -ItemType Directory -Path $nugetDir
}

# NuGet executable URL
$nugetUrl = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"

# Full path to nuget.exe
$nugetPath = Join-Path -Path $nugetDir -ChildPath "nuget.exe"

# Download the latest nuget.exe
Invoke-WebRequest -Uri $nugetUrl -OutFile $nugetPath

# Determine global-packages location from the NuGet config
$globalPackagesLocation = & $nugetPath config globalPackagesFolder -Force
if (-not $globalPackagesLocation) {
    $globalPackagesLocation = "$env:USERPROFILE\.nuget\packages"
}

# Define the path to the package within the global-packages location
$packagePath = Join-Path -Path $globalPackagesLocation -ChildPath $RepoName

# Check if the package directory exists
if (Test-Path $packagePath) {
    # Get all versions of the package
    $packageVersions = Get-ChildItem -Path $packagePath

    # Specify the number of versions to keep
    $versionsToKeep = 3

    # Select the most recent versions to keep
    $packageVersionsToKeep = $packageVersions | Sort-Object CreationTime -Descending | Select-Object -First $versionsToKeep

    # Remove older versions
    $packageVersions | Where-Object { $_ -notin $packageVersionsToKeep } | ForEach-Object {
        Write-Host "Removing old package version: $($_.Name)"
        Remove-Item $_.FullName -Recurse -Force
    }
}

# Register the NuGet global-packages repository
Write-Host "Registering NuGet repository at the global-packages location: $globalPackagesLocation"
& $nugetPath sources Remove -Name $RepoName -Force
& $nugetPath sources Add -Name $RepoName -Source $globalPackagesLocation -Force

# Check for errors in adding the source
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to add NuGet source. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "NuGet source $RepoName registered successfully at global-packages location."


