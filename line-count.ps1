# Count-CsGitLines.ps1

$added = [long]0
$deleted = [long]0

# Ignore lines containing only one curly bracket.
$meaninglessPattern = '^[{}]$'

$patch = git log --all `
    --patch `
    --unified=0 `
    --no-ext-diff `
    --format= `
    -- '*.cs'

foreach ($line in $patch) {
    # Ignore diff file headers.
    if ($line -match '^\+\+\+ ' -or $line -match '^--- ') {
        continue
    }

    if ($line.StartsWith('+')) {
        $content = $line.Substring(1).Trim()

        if ($content -notmatch $meaninglessPattern) {
            $added++
        }
    }
    elseif ($line.StartsWith('-')) {
        $content = $line.Substring(1).Trim()

        if ($content -notmatch $meaninglessPattern) {
            $deleted++
        }
    }
}

[pscustomobject]@{
    LinesAdded   = $added
    LinesDeleted = $deleted
    NetChange    = $added - $deleted
}