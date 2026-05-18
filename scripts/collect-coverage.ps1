param(
    [string]$Solution = "VB6Converter.slnx"
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProjects = @(
    "VB6Converter.Tests/VB6Converter.Tests.csproj",
    "VB6Parser.Tests/VB6Parser.Tests.csproj"
)
$coverageRoot = Join-Path $repoRoot "TestResults/Coverage"
$overallExitCode = 0

foreach ($project in $testProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    $projectResults = Join-Path $coverageRoot $projectName

    if (Test-Path $projectResults) {
        Remove-Item $projectResults -Recurse -Force
    }

    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null

    Push-Location $repoRoot
    try {
        dotnet test $project --collect "XPlat Code Coverage" --results-directory $projectResults
        $exitCode = $LASTEXITCODE

        if ($exitCode -ne 0 -and $overallExitCode -eq 0) {
            $overallExitCode = $exitCode
        }

        $report = Get-ChildItem $projectResults -Recurse -Filter coverage.cobertura.xml |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($report) {
            Copy-Item $report.FullName (Join-Path $projectResults "coverage.cobertura.xml") -Force
            Write-Host "Coverage report: $(Join-Path $projectResults 'coverage.cobertura.xml')"
        }
        else {
            Write-Warning "No coverage report found for $project"

            if ($overallExitCode -eq 0) {
                $overallExitCode = 1
            }
        }
    }
    finally {
        Pop-Location
    }
}

exit $overallExitCode