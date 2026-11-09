<#
    .SYNOPSIS
    Moves and packs the primary BX.SJ solution into another place.

    .DESCRIPTION
    This is assumed to be ran from the development branch worktree, from the root directory.
    It is essentially an automation of moving the relevant SJ files + examples into the 
    other worktree that I have for the stable, non-development branch.
    (It is pretty much overkill for SJ, but I will adapt it further for different projects using similar work style,
     like BXSave and others, that may have more complicated file structures)

    .PARAMETER MoveDirs
    Directories to move matching folders, alongside their postprocessor script blocks called for each file that has been moved.
    Only their inner content is moved (via glob) rather than the entire folder.
    This can contain three types of objects :
    [string]Path | [string]Directory | ([string]Directory, [ScriptBlock]PostProcessor,
      [Parameter(Mandatory=$false)][bool]CopyWithPath, [Parameter(Mandatory=$false)][string[]]Exclude)

    .PARAMETER TargetDir
    Target worktree directory. This must be a valid git worktree.

    .PARAMETER TargetBranch
    Branch of the target dir's worktree. If unsure, leave as "master|main". These names are picked for fetching and checks.

    .PARAMETER DryRun
    Do not do anything but output the results, outside of validating that the worktree exists.
    (A choice is given for that - a valid $TargetDir must exist for dry run)

    .EXAMPLE
    # Run it from the root of the project:
    pwsh Tools/BuildPrimaryBranch.ps1
#>
Param (
    $MoveDirs = (
        # File | Directory | (Directory, PostProcessor, CopyWithPath, Exclude)
        'README.md',
        'BX.SJ',
        ('BX.SJ.Tests/Examples', {
            Param($dir)
            Get-ChildItem "*.cs" | Foreach-Object {
                $file = $_
                # This could be slow, but it will do for this basic instance.
                # Group captures didn't work, this will do as each group doesn't contain any unique content.
                ((Get-Content $file) -replace "namespace\s+BX\.SJ\.Tests\.Examples", "namespace BX.SJ.Examples") -replace "public\s+static\s+void\s+TMain", "static void Main" | Set-Content $file
            }
        }, $true, ('README.md'))
    ),
    [string] $TargetDir = '../sj',
    [string] $TargetBranch = 'master|main',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    enum GitRepoErr {
        None
        NotGitRepo
        NotExpectedBranch
    }

    $globalExclude = @('bin', 'obj')
    $originUrlShouldContain = "BX.?SJ"
    Function Check-GitRepo {
        Param (
            [Parameter(Mandatory=$false)][string] $ExpectedBranch,
            [Parameter(Mandatory=$false)][string] $Directory
        )

        if ($Directory) { Push-Location $Directory }
        try {
            if (-not (git rev-parse --is-inside-work-tree 2>$null)) {
                return [GitRepoErr]::NotGitRepo
            }
            if ($ExpectedBranch -and -not ((git rev-parse --abbrev-ref HEAD) -match $ExpectedBranch)) {
                return [GitRepoErr]::NotExpectedBranch
            }
            return [GitRepoErr]::None
        } finally {
            if ($Directory) { Pop-Location }
        }
    }
    Function Get-GitUpstream {
        Param (
            [Parameter(Mandatory=$false)][string] $Directory
        )
        if ($Directory) { Push-Location $Directory }
        try {
            # upstream contains junk like refs/remotes/origin/<branch>
            # Just get the last index of "/", then another last index.
            $upstreamPath = git rev-parse --symbolic-full-name "@{upstream}"
            $upstreamPath = $upstreamPath.Substring(0, $upstreamPath.LastIndexOf("/"))
            $upstreamPath = $upstreamPath.Substring($upstreamPath.LastIndexOf("/") + 1)
            return $upstreamPath
        } finally {
            if ($Directory) { Pop-Location }
        }
    }
    Function Read-Choice {
        Param (
            [Parameter(Mandatory=$true)][string]$Title,
            [Parameter(Mandatory=$true)][string]$Message,
            [Parameter(Mandatory=$false)][int]$DefaultOption = 0,
            [Parameter(Mandatory=$false)][switch]$IncludeAll
        )
    
        $size = if ($IncludeAll) { 3 } else { 2 }
        $Options = [System.Management.Automation.Host.ChoiceDescription[]]::new($size);
        $Options[0] = New-Object System.Management.Automation.Host.ChoiceDescription '&No', 'No'
        $Options[1] = New-Object System.Management.Automation.Host.ChoiceDescription '&Yes', 'Yes'
        if ($IncludeAll) {
            $Options[2] = New-Object System.Management.Automation.Host.ChoiceDescription '&All', 'All'
        }

        return $host.ui.PromptForChoice($Title, $Message, $Options, $DefaultOption)
    }

    if ((Check-GitRepo) -ne [GitRepoErr]::None) {
        throw "Parent is not a git repository, run this from a valid BXSJ worktree."
    }
    
    # Check $TargetDir
    if (-not (Test-Path $TargetDir)) {
        switch ((Read-Choice "Info" "No BXSJ worktree at $((Resolve-Path $TargetDir).Path) that is on branch $Branch was found, would you like to create one?")) {
            0 { throw "No destination $TargetDir exists that is a BXSJ worktree on branch $Branch. Create one yourself and checkout the target branch." }
            1 {
                $branch = $null
                $upstream = Get-GitUpstream
                $branches = (git branch --format='%(refname:short)')
                if (-not ($branches -match $TargetBranch)) {
                    # Should pull it from the remote?, prefer the first "branch" listed
                    # Could also just `git fetch --all`, but whatever the listed basic RegExp branches will do.
                    foreach ($b in $TargetBranch.Split('|')) { git fetch $upstream $b }
                    $branches = (git branch --format='%(refname:short)')
                    $branch = $branches | Select-String -Pattern $TargetBranch
                    if (-not ($branch)) {
                        throw "No branch on local workspace matching $TargetBranch within
$branches
fetch those from remote or create a branch in the remote."
                    }
                    $branch = $branch.Matches[0].Value  # MatchInfo is returned
                }

                New-Item -Path $TargetDir -ItemType "Directory"
                # TODO : This could be invalid, according to my PS-GetReadLineOption history's worktree init code..
                # I think without any of these parameters, it just copies the devel worktree instead.
                git worktree add --track -b $branch $TargetDir "$upstream/$branch"
                Write-Host -ForegroundColor Green "Created git worktree with branch $branch at $TargetDir, tracking $upstream/$branch"
            }
            default { throw "Invalid choice" }
        }
    }
    Push-Location $TargetDir
    try {
        # Give time for spamming ctrl+c before proceeding
        $pwd = ((Get-Location).Path)
        switch ((Check-GitRepo -ExpectedBranch $TargetBranch)) {
            [GitRepoErr]::NotGitRepo {
                throw "No git repository in $pwd"
            }
            [GitRepoErr]::NotExpectedBranch {
                Write-Host "Unexpected branch (not matching $TargetBranch) in $pwd : $(git rev-parse --abbrev-ref HEAD)"
                Start-Sleep 3000
            }
            default {}
        }
        $remote = (git remote get-url (Get-GitUpstream))
        if (-not ($remote -match $originUrlShouldContain)) {
            Write-Host -ForegroundColor Yellow "Remote URL for destination $pwd does not contain BXSJ, this could be copying into a different repos worktree : $remote"
            Start-Sleep 3000
        }
    } finally {
        Pop-Location
    }

    # Start moving
    for ($i = 0; $i -lt $MoveDirs.Length; $i += 1) {
        $dir = $MoveDirs[$i]
        $pp = $null
        $cwp = $false
        if ($dir -is [array]) {
            $dir, $pp, $cwp, $exclude = $dir
        }
        if (-not ($dir)) {
            Write-Error "Empty directory at $i on MoveDirs"
            continue
        }

        $files = git ls-files --cached --others --exclude-standard $dir
        if ($LASTEXITCODE -ne 0) { throw "git ls-files failed, run this from the repository." }
        $files = $files | Where-Object {
            $p = $_; (Test-Path -LiteralPath $p -PathType Leaf) -and -not (($globalExclude + $exclude) | Where-Object { $p -match $_ })
        }

        if (git status --porcelain $dir) {
            Write-Host -ForegroundColor Yellow "Working tree has uncommitted changes on $dir, it is recommended to commit and test here first, before copying."
        }

        # If file or copy with path, copy it literally
        $cwp = if ($cwp) { $true } else { (Test-Path -PathType Leaf $dir) }
        $from = if ($cwp) { $dir } else { (Join-Path $dir "*") }
        $c = if ($DryRun) { "DarkGreen" } else { "Green" }
        # Wait-Debugger
        if (-not ($DryRun)) {
            Copy-Item -Path $from -Destination $TargetDir -Recurse -Force
        }
        if ($pp) {
            if (-not ($DryRun)) {
                # Check if "*" was postfixed
                $fp = if ($cwp) { Split-Path -Leaf $from } else { $from }
                $copiedDir = (Join-Path $TargetDir $fp)
                Push-Location $copiedDir
                try {
                   $pp.Invoke($copiedDir)
                } finally {
                    Pop-Location
                }
            } else {
                # Nothing was copied
                $children = (Get-ChildItem $from -Recurse)
                Write-Host "Will apply postprocessor script block to directory with the following files :"
                Write-Host (($children | Foreach-Object { $_.Name }) -join ", ")
            }
        }

        Write-Host -ForegroundColor $c "Copied $from into $TargetDir"
    }

} finally {
    Pop-Location
}
