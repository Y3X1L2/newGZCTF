[CmdletBinding()]
param([string]$Source)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($Source) { Copy-Item -LiteralPath $Source -Destination (Join-Path $PSScriptRoot 'openapi.yaml') }
[xml]$packages = Get-Content (Join-Path $repoRoot 'src/Directory.Packages.props')
$version = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'NSwag.MSBuild').Version
$packageRoot = (& dotnet msbuild (Join-Path $repoRoot 'src/GZCTF/GZCTF.csproj') -nologo -getProperty:NuGetPackageRoot).Trim()
if ($LASTEXITCODE -ne 0) { throw '无法读取 NuGet 路径。' }
$generator = Join-Path $packageRoot "nswag.msbuild/$version/tools/Net100/dotnet-nswag.dll"
& dotnet $generator openapi2csclient `
    "/input:$(Join-Path $PSScriptRoot 'openapi.yaml')" `
    "/output:$(Join-Path $repoRoot 'src/GZCTF/Modules/TeamLab/Infrastructure/Netlab/NetlabContract.g.cs')" `
    /namespace:GZCTF.Modules.TeamLab.Infrastructure.Netlab /ClassName:NetlabApi `
    /JsonLibrary:NewtonsoftJson /GenerateNullableReferenceTypes:true `
    /GenerateClientClasses:false /GenerateDtoTypes:true /GenerateDataAnnotations:false `
    /GenerateOptionalPropertiesAsNullable:true `
    /ArrayType:System.Collections.Generic.List /ArrayInstanceType:System.Collections.Generic.List `
    /DictionaryType:System.Collections.Generic.Dictionary /DictionaryInstanceType:System.Collections.Generic.Dictionary
if ($LASTEXITCODE -ne 0) { throw 'Netlab 契约生成失败。' }
