[CmdletBinding()]
param([switch]$Persist)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseNotes.ps1')
$path = Join-Path $PSScriptRoot '../../Directory.Build.props'
$document = [xml]::new()
$document.PreserveWhitespace = $true
$document.Load($path)
$versions = @{}
foreach ($id in $diagramEnginePackageIds) { $versions[$id] = @(Get-PublishedPackageVersions $id) }
$baseline = @(Resolve-PublicationBaseline $versions)
Assert-PublicationBaseline ([string]$document.Project.PropertyGroup.Version) $baseline
$verifiedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
if ($Persist) {
    $timeNode = $document.SelectSingleNode('/Project/PropertyGroup/PublicationBaselineVerifiedUtc')
    $packages = @($document.SelectNodes('/Project/ItemGroup/PublicationBaselinePackage'))
    if ($null -eq $timeNode -or
        (@($packages.Include | Sort-Object) -join ',') -cne (@($diagramEnginePackageIds | Sort-Object) -join ',')) {
        throw 'Directory.Build.props does not contain the expected publication-baseline configuration.'
    }
    $timeNode.InnerText = $verifiedUtc
    foreach ($package in $packages) {
        $record = $baseline | Where-Object { $_.PackageId -ceq $package.Include }
        $package.SetAttribute('State', $record.State)
        if ($record.State -ceq 'Published') { $package.SetAttribute('PublishedVersion', $record.PublishedVersion) }
        else { $package.RemoveAttribute('PublishedVersion') }
    }
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.OmitXmlDeclaration = $true
    $writer = [Xml.XmlWriter]::Create($path, $settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
}
[pscustomobject]@{
    VerifiedUtc = $verifiedUtc
    Source = 'https://api.nuget.org/v3-flatcontainer/'
    Persisted = [bool]$Persist
    Packages = $baseline
} | ConvertTo-Json -Depth 4
