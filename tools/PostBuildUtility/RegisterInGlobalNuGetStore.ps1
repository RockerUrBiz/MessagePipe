param (
    [Parameter(Mandatory=$true)]
    [string]$RepoName
)

# Retrieve the system environment variable for the NuGet executable folder
$nugetDir = [Environment]::GetEnvironmentVariable("Nuget_Exe", "Machine")

# Append the executable name to the folder path
$nugetPath = Join-Path -Path $nugetDir -ChildPath "nuget.exe"

# Check if nuget.exe is specified in the system variables
if ([string]::IsNullOrWhiteSpace($nugetDir)) {
    Write-Error "The NuGet directory is not set in the system variables."
    exit 1
}

# Check if the nuget.exe path is valid
if (-not (Test-Path $nugetPath)) {
    Write-Error "The path to nuget.exe does not exist: $nugetPath"
    exit 1
}

# Determine global-packages location
$globalPackagesLocation = & $nugetPath config globalPackagesFolder
if (-not $globalPackagesLocation) {
    $globalPackagesLocation = "$env:USERPROFILE\.nuget\packages"
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
