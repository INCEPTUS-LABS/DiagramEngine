# Shared release-preparation and inspection functions. Loading this file performs no network access.
$diagramEnginePackageIds = @(
    'Inceptus.DiagramEngine.Contracts', 'Inceptus.DiagramEngine.Runtime', 'Inceptus.DiagramEngine.Bpmn',
    'Inceptus.DiagramEngine.Organizational', 'Inceptus.DiagramEngine.Canvas2D',
    'Inceptus.DiagramEngine.Bpmn.Blazor', 'Inceptus.Bpmn.Integration'
)
function ConvertTo-ReleaseVersion([string]$Value) {
    $parsed = $null
    if ($Value -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or -not [version]::TryParse($Value, [ref]$parsed)) {
        throw "Invalid stable release version: $Value"
    }
    return $parsed
}
function Assert-PublicationBaseline([string]$Version, $Packages) {
    $current = ConvertTo-ReleaseVersion $Version
    if ($current -lt [version]'1.0.0' -or
        (@($Packages.PackageId | Sort-Object) -join ',') -cne (@($diagramEnginePackageIds | Sort-Object) -join ',')) {
        throw "Invalid or inconsistent publication baseline for $Version."
    }
    $initial = @($Packages | Where-Object { $_.State -ceq 'NeverPublished' }).Count -gt 0
    $published = @()
    foreach ($package in $Packages) {
        if ($package.State -ceq 'NeverPublished') {
            if ($Version -cne '1.0.0' -or $package.PackageId -ceq 'Inceptus.Bpmn.Integration' -or
                -not [string]::IsNullOrEmpty($package.PublishedVersion)) {
                throw "Invalid initial publication baseline: $($package.PackageId)."
            }
        } elseif ($package.State -ceq 'Published') {
            $previous = ConvertTo-ReleaseVersion $package.PublishedVersion
            if ($previous -ge $current -or ($initial -and
                    ($package.PackageId -cne 'Inceptus.Bpmn.Integration' -or $previous -ge [version]'1.0.0')) -or
                (-not $initial -and $previous -lt [version]'1.0.0')) {
                throw "Invalid publication baseline for $($package.PackageId): $($package.PublishedVersion)."
            }
            $published += $package.PublishedVersion
        } else { throw "Invalid publication baseline state: $($package.PackageId)." }
    }
    if (($initial -and $published.Count -ne 1) -or (-not $initial -and @($published | Select-Object -Unique).Count -ne 1)) {
        throw "Invalid or inconsistent publication baseline for $Version."
    }
}
function Get-CumulativeReleaseNotes([string]$Text, [string]$Version, $PackageBaseline) {
    $current = ConvertTo-ReleaseVersion $Version
    $initial = $PackageBaseline.State -ceq 'NeverPublished'
    if ($initial) {
        if ($Version -cne '1.0.0' -or -not [string]::IsNullOrEmpty($PackageBaseline.PublishedVersion)) {
            throw 'Invalid initial publication baseline.'
        }
        $baseline = $null
    } elseif ($PackageBaseline.State -ceq 'Published') {
        $baseline = ConvertTo-ReleaseVersion $PackageBaseline.PublishedVersion
        if ($baseline -ge $current) { throw 'Invalid publication baseline: predecessor must precede the candidate.' }
    } else { throw 'Invalid publication baseline state.' }
    $text = $Text.Replace([string][char]13 + [char]10, [string][char]10).Replace([char]13, [char]10)
    $headings = [regex]::Matches($text, '(?m)^##[ \t]+(?<version>[0-9]+\.[^\s]+)(?:[ \t]+[^\n]*)?$')
    $selected = [Collections.Generic.HashSet[version]]::new()
    $notes = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $headings.Count; $index++) {
        $heading = $headings[$index]
        $sectionVersion = ConvertTo-ReleaseVersion $heading.Groups['version'].Value
        if ($sectionVersion -gt $current -or ($initial -and $sectionVersion -ne $current) -or
            (-not $initial -and $sectionVersion -le $baseline)) { continue }
        $end = if ($index + 1 -lt $headings.Count) { $headings[$index + 1].Index } else { $text.Length }
        $bodyStart = $heading.Index + $heading.Length
        if (-not $selected.Add($sectionVersion) -or [string]::IsNullOrWhiteSpace($text.Substring($bodyStart, $end - $bodyStart))) {
            throw "Missing, empty or ambiguous release notes section: $sectionVersion."
        }
        [void]$notes.Append($text.Substring($heading.Index, $end - $heading.Index))
    }
    $result = $notes.ToString().TrimEnd()
    if (-not $selected.Contains($current) -or $result.Length -eq 0 -or $result.Length -gt 35000) {
        throw "Cumulative release notes must include version $Version and contain between 1 and 35000 characters; found $($result.Length)."
    }
    return $result
}
function Get-PublishedPackageVersions([string]$PackageId) {
    if ($diagramEnginePackageIds -cnotcontains $PackageId) { throw "Unknown package: $PackageId." }
    $uri = 'https://api.nuget.org/v3-flatcontainer/' + $PackageId.ToLowerInvariant() + '/index.json'
    try { $response = Invoke-RestMethod -Uri $uri -ErrorAction Stop }
    catch {
        # Only a real HTTP 404 for a new identity means never published. DNS/auth/server errors fail closed.
        if ($PackageId -cne 'Inceptus.Bpmn.Integration' -and $null -ne $_.Exception.Response -and
            [int]$_.Exception.Response.StatusCode -eq 404) { return }
        throw
    }
    if ($null -eq $response.versions -or @($response.versions).Count -eq 0) { throw "Empty or invalid version index: $PackageId." }
    return $response.versions
}
function Resolve-PublicationBaseline($PackageVersions) {
    if ((@($PackageVersions.Keys | Sort-Object) -join ',') -cne (@($diagramEnginePackageIds | Sort-Object) -join ',')) {
        throw 'Publication baseline requires exactly the seven package IDs.'
    }
    $parsed = @{}
    foreach ($id in $diagramEnginePackageIds) {
        $versions = @($PackageVersions[$id] | ForEach-Object { ConvertTo-ReleaseVersion $_ } | Sort-Object)
        if (@($versions | Select-Object -Unique).Count -ne $versions.Count -or
            ($id -ceq 'Inceptus.Bpmn.Integration' -and $versions.Count -eq 0) -or
            ($id -cne 'Inceptus.Bpmn.Integration' -and @($versions | Where-Object { $_ -lt [version]'1.0.0' }).Count -gt 0)) {
            throw "Missing, invalid or ambiguous published versions: $id"
        }
        $parsed[$id] = $versions
    }
    # The renamed coordinated family begins at 1.0.0. Integration's earlier history remains its own.
    $versionsToCheck = @($parsed.Values | ForEach-Object { $_ } | Where-Object { $_ -ge [version]'1.0.0' } | Sort-Object -Unique)
    foreach ($version in $versionsToCheck) {
        $missing = @($diagramEnginePackageIds | Where-Object { $parsed[$_] -notcontains $version })
        if ($missing.Count -gt 0) {
            throw "Partial or inconsistent publication at $version; missing packages: $($missing -join ', '). No baseline was persisted."
        }
    }
    if ($versionsToCheck.Count -gt 0 -and $versionsToCheck[0] -ne [version]'1.0.0') {
        throw 'The coordinated family must begin at 1.0.0.'
    }
    foreach ($id in $diagramEnginePackageIds) {
        $versions = $parsed[$id]
        [pscustomobject]@{
            PackageId = $id
            State = if ($versions.Count -eq 0) { 'NeverPublished' } else { 'Published' }
            PublishedVersion = if ($versions.Count -eq 0) { '' } else { $versions[-1].ToString() }
        }
    }
}
