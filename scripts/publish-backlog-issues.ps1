param(
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
$repository = 'manoir-app/Manoir.HomeDocs'
$baseUrl = "https://github.com/$repository"
$backlogPath = Join-Path $PSScriptRoot '..\specs\backlog.md'
$marker = '<!-- homedocs-backlog-publisher -->'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is not installed or is not in PATH.'
}

$backlog = Get-Content -LiteralPath $backlogPath -Raw -Encoding UTF8
$commonMatch = [regex]::Match(
    $backlog,
    '(?ms)^## Consignes communes[^\r\n]*\r?\n(?<common>.*?)(?=^---\s*$)'
)
if (-not $commonMatch.Success) {
    throw 'The common instructions could not be found in the backlog.'
}
$common = $commonMatch.Groups['common'].Value.Trim()
$matches = [regex]::Matches(
    $backlog,
    '(?ms)^### (?<id>HD-\d{2})\s+[^\w\s]+\s+(?<title>[^\r\n]+)\r?\n(?<body>.*?)(?=^### HD-\d{2}\s|^## |\z)'
)
if ($matches.Count -eq 0) {
    throw 'No issue entries were found in the backlog.'
}

$entries = [ordered]@{}
foreach ($match in $matches) {
    $id = $match.Groups['id'].Value
    if ($entries.Contains($id)) {
        throw "Duplicate backlog identifier: $id"
    }
    $body = $match.Groups['body'].Value.Trim()
    $body = [regex]::Replace($body, '(?m)\r?\n---\s*$', '').Trim()
    $body = [regex]::Replace(
        $body,
        '(?m)^(\*\*Brief :\*\*\s*)([^\r\n]*\S)([ \t]*)$',
        { param($m) $m.Groups[1].Value + '[' + $m.Groups[2].Value + "]($baseUrl/blob/main/specs/brief.md)" + $m.Groups[3].Value }
    )
    $heading = $match.Value.Substring(4, $match.Value.IndexOf("`n") - 4).Trim()
    $entries[$id] = @{
        Title = $heading
        Body = "$body`n`n## Consignes communes`n`n$common`n`n[Backlog]($baseUrl/blob/main/specs/backlog.md)`n`n$marker"
    }
}

$response = & gh api --paginate --slurp "repos/$repository/issues?state=all&per_page=100"
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to list issues. Check gh auth status and repository access.'
}
$pages = ConvertFrom-Json -InputObject ($response -join "`n")
$issues = @{}
foreach ($page in $pages) {
    foreach ($issue in $page) {
        if ($null -ne $issue.pull_request) {
            continue
        }
        if ($issue.title -match '^(HD-\d{2})\s') {
            $id = $Matches[1]
            if ($entries.Contains($id)) {
                if ($issues.ContainsKey($id)) {
                    throw "Multiple issues have identifier $id. Resolve the duplicate before continuing."
                }
                $issues[$id] = $issue
            }
        }
    }
}

Write-Host "Backlog: $($entries.Count) entries; existing issues: $($issues.Count)."
if ($Preview) {
    foreach ($id in $entries.Keys) {
        if ($issues.ContainsKey($id)) {
            Write-Host "Existing: $id #$($issues[$id].number)"
        } else {
            Write-Host "To create: $($entries[$id].Title)"
        }
    }
    return
}

$tempFile = [System.IO.Path]::GetTempFileName()
$utf8 = New-Object System.Text.UTF8Encoding($false)
try {
    foreach ($id in $entries.Keys) {
        if ($issues.ContainsKey($id)) {
            Write-Host "Skipping $id (#$($issues[$id].number))."
            continue
        }
        [System.IO.File]::WriteAllText($tempFile, $entries[$id].Body, $utf8)
        $result = & gh issue create --repo $repository --title $entries[$id].Title --body-file $tempFile
        if ($LASTEXITCODE -ne 0) {
            throw "Creation failed for $id. Re-run the script to resume."
        }
        $url = ($result | Select-Object -Last 1).Trim()
        $pattern = '^' + [regex]::Escape($baseUrl) + '/issues/(?<number>\d+)$'
        if ($url -notmatch $pattern) {
            throw "Unexpected response while creating $id. Check GitHub before re-running."
        }
        $issues[$id] = [pscustomobject]@{
            number = [int]$Matches['number']
            body = $entries[$id].Body
        }
        Write-Host "Created $id : $url"
    }

    foreach ($id in $entries.Keys) {
        $issue = $issues[$id]
        if ($null -eq $issue.body -or -not $issue.body.Contains($marker)) {
            continue
        }
        $body = [regex]::Replace(
            $issue.body,
            '(?<![\w\[/])HD-\d{2}(?![\w\]])',
            {
                param($m)
                $target = $m.Value
                if ($target -eq $id -or -not $issues.ContainsKey($target)) {
                    return $target
                }
                return "[$target]($baseUrl/issues/$($issues[$target].number))"
            }
        )
        if ($body -ne $issue.body) {
            [System.IO.File]::WriteAllText($tempFile, $body, $utf8)
            & gh issue edit $issue.number --repo $repository --body-file $tempFile | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Link update failed for $id. Re-run the script to resume."
            }
            Write-Host "Linked references in $id (#$($issue.number))."
        }
    }
} finally {
    Remove-Item -LiteralPath $tempFile -Force
}

Write-Host 'Done. Existing issues without the publisher marker were not modified.'
