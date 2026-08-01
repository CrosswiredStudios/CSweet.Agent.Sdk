[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sdkRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$contractsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $sdkRoot '../CSweet.WorkManagement.Contracts'))
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("csweet-agent-template-" + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $tempRoot 'feed'
$generated = Join-Path $tempRoot 'VerifiedAgent'
$previousCliHome = $env:DOTNET_CLI_HOME

try {
    New-Item -ItemType Directory -Force $feed | Out-Null

    & dotnet pack (Join-Path $contractsRoot `
            'src/CSweet.WorkManagement.Contracts/CSweet.WorkManagement.Contracts.csproj') `
        --configuration Release `
        --output $feed `
        -p:PackageVersion=1.3.0
    if ($LASTEXITCODE -ne 0) {
        throw 'Work Management Contracts package creation failed.'
    }

    & dotnet pack (Join-Path $sdkRoot 'src/CSweet.Agent.SDK/CSweet.Agent.SDK.csproj') `
        --configuration Release `
        --output $feed `
        -p:UseLocalCSweetWorkManagementContracts=false `
        -p:CSweetAgentSdkPackageVersion=2.5.1
    if ($LASTEXITCODE -ne 0) { throw 'SDK package creation failed.' }

    $env:DOTNET_CLI_HOME = Join-Path $tempRoot 'dotnet-home'
    & dotnet new install (Join-Path $sdkRoot 'templates/CSweet.Agent.Template')
    if ($LASTEXITCODE -ne 0) { throw 'Template installation failed.' }

    & dotnet new csweet-agent `
        --output $generated `
        --name VerifiedAgent `
        --AgentId com.example.verified-agent `
        --DisplayName 'Verified Agent' `
        --AgentPurpose 'Verifies the standalone C-Sweet agent template.' `
        --PublisherId org.publisher `
        --PublisherName 'Example Publisher' `
        --AgentVersion 0.1.0 `
        --PrimaryCapability example.verify.v1 `
        --SdkVersion 2.5.1
    if ($LASTEXITCODE -ne 0) { throw 'Template generation failed.' }

    $escapedFeed = [System.Security.SecurityElement]::Escape($feed)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="authoring-test" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $generated 'NuGet.config') -Encoding utf8

    $generatedProject = Get-Content `
        -LiteralPath (Join-Path $generated 'src/VerifiedAgent/VerifiedAgent.csproj') `
        -Raw
    if ($generatedProject -notmatch 'PackageReference Include="CSweet.Agent.SDK" Version="2.5.1"') {
        throw 'Generated agent does not use the pinned SDK package.'
    }
    if ($generatedProject -match 'ProjectReference') {
        throw 'Generated agent contains a source-tree project reference.'
    }

    $manifest = Get-Content -LiteralPath (Join-Path $generated 'csweet-plugin.json') -Raw |
        ConvertFrom-Json
    if ($manifest.id -ne 'com.example.verified-agent' -or
        $manifest.publisher.id -ne 'org.publisher' -or
        $manifest.provides[0].name -ne 'example.verify.v1') {
        throw 'Template parameters were not substituted exactly.'
    }

    & dotnet test (Join-Path $generated 'VerifiedAgent.slnx') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Generated repository tests failed.' }

    & dotnet run --project (Join-Path $generated 'src/VerifiedAgent') `
        --configuration Release `
        --no-build `
        -- `
        --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Generated agent self-test failed.' }
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $resolvedTemp = [System.IO.Path]::GetFullPath($tempRoot)
    if ($resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
