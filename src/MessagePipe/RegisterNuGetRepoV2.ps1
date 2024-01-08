param (
    [Parameter(Mandatory=$true)]
    [string]$RepoName
)

# Retrieve the system environment variable for the NuGet executable folder
$nugetDir = [Environment]::GetEnvironmentVariable("Nuget_Exe", "Machine")

# Append the executable name to the folder path
$nugetPath = Join-Path -Path $nugetDir -ChildPath "nuget.exe"

# Determine global-packages location
$globalPackagesLocation = & $nugetPath config globalPackagesFolder
if (-not $globalPackagesLocation) {
    $globalPackagesLocation = "$env:USERPROFILE\.nuget\packages"
}

# Define the package path within the global-packages location
$packagePath = Join-Path -Path $globalPackagesLocation -ChildPath $RepoName

# Check if the package path exists
if (Test-Path $packagePath) {
    # Get all versions of the package
    $packageVersions = Get-ChildItem -Path $packagePath

    # Optional: Specify a criteria for which versions to keep (e.g., keep the latest 3 versions)
    $versionsToKeep = 3
    $packageVersionsToKeep = $packageVersions | Sort-Object CreationTime -Descending | Select-Object -First $versionsToKeep

    # Remove all other versions
    $packageVersions | Where-Object { $_ -notin $packageVersionsToKeep } | ForEach-Object {
        Write-Host "Removing old package version: $($_.Name)"
        Remove-Item $_.FullName -Recurse -Force
    }
}

# Register the NuGet global-packages repository
Write-Host "Registering NuGet repository at the global-packages location: $globalPackagesLocation"
& $nugetPath sources Remove -Name $RepoName > $null 2>&1
& $nugetPath sources Add -Name $RepoName -Source $globalPackagesLocation

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to add NuGet source. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "NuGet source $RepoName registered successfully at global-packages location."

