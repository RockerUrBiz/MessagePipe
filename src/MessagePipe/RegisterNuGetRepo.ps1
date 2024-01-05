param (
    [Parameter(Mandatory=$true)]
    [string]$RepoName,
    
    [Parameter(Mandatory=$true)]
    [string]$SolutionDir,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetDir
)

# Trim trailing backslashes that might cause issues
$SolutionDir = $SolutionDir.TrimEnd('\')
$TargetDir = $TargetDir.TrimEnd('\')

# Construct the path to nuget.exe
$nugetPath = Join-Path $SolutionDir -ChildPath "nuget.exe"

# Check if nuget.exe exists at the specified location
if (-not (Test-Path $nugetPath)) {
    Write-Error "nuget.exe not found at the path: $nugetPath"
    exit 1
}

# Register the NuGet local repository
Write-Host "Registering NuGet local repository with package name $RepoName at $TargetDir"
& $nugetPath sources Remove -Name $RepoName > $null 2>&1
& $nugetPath sources Add -Name $RepoName -Source $TargetDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to add NuGet source. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "NuGet source $RepoName registered successfully."
