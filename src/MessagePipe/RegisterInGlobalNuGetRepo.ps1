param (
    [Parameter(Mandatory=$true)]
    [string]$RepoName,
    
    [Parameter(Mandatory=$true)]
    [string]$SolutionDir
)

# Trim trailing backslash
$SolutionDir = $SolutionDir.TrimEnd('\')

# Construct the path to nuget.exe
$nugetPath = Join-Path $SolutionDir -ChildPath "nuget.exe"

# Check if nuget.exe exists at the specified location
if (-not (Test-Path $nugetPath)) {
    Write-Error "nuget.exe not found at the path: $nugetPath"
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
