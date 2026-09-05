[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectDirectory,
    [string]$Tag = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$project = Join-Path $projectRoot 'DNFProcessManager.csproj'
if ($Tag) {
    if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$') {
        throw 'Use a version tag such as v1.2.0 or v1.2.0-beta.1.'
    }
    $version = $Tag.Substring(1)
} else {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    $Tag = "v$version"
}

$packageName = "DNFProcessManager-$Tag-win-x64"
$runRoot = Join-Path $projectRoot ('artifacts\github-release\' + [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $runRoot $packageName
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $projectRoot
try {
    & dotnet restore $project --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build $project --configuration Release --runtime win-x64 --no-restore "-p:Version=$version"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --no-build --no-restore "-p:Version=$version" '-p:PublishSingleFile=true' "-p:PublishDir=$publishDirectory\"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $publishDirectory

    $required = @('DNFProcessManager.exe', 'appsettings.json', 'DNFAutoFire.exe', 'config.ini', 'README.md', 'LICENSE.txt', '服务管理.bat', 'DNF专用工具箱8.0.bat', 'docs\images\quick-start.png')
    foreach ($relativePath in $required) {
        $file = Join-Path $publishDirectory $relativePath
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Missing or empty release file: $relativePath"
        }
    }
    foreach ($script in @('服务管理.bat', 'DNF专用工具箱8.0.bat')) {
        & (Join-Path $publishDirectory $script) --self-test
        if ($LASTEXITCODE -ne 0) { throw "Self-test failed: $script" }
    }

    $archive = Join-Path $runRoot "$packageName.zip"
    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archive -CompressionLevel Optimal
    # Verify the actual archive, including the Chinese filenames and companion files.
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($relativePath in $required) {
            $expected = $packageName + '/' + $relativePath.Replace('\', '/')
            if ($expected -notin $entries) { throw "File missing from ZIP: $expected" }
        }
    } finally { $zip.Dispose() }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = "$archive.sha256"
    "$hash  $packageName.zip" | Set-Content -LiteralPath $checksum -Encoding ascii
    if ($env:GITHUB_OUTPUT) {
        "archive=$archive" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "checksum=$checksum" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
    [pscustomobject]@{Version=$version;Archive=$archive;Checksum=$checksum;SHA256=$hash}
} finally { Pop-Location }
