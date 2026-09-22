<#
.SYNOPSIS
  Creates (or updates) the Azure App Service that hosts the bot and deploys the current code to it.

.DESCRIPTION
  Safe to rerun: existing resources are updated, not recreated. The first run creates everything;
  later runs mostly just publish and deploy.

  Bot credentials are taken from parameters, or, when omitted, from the project's user secrets
  (dotnet user-secrets, see README step 2), so nothing has to be typed on the command line.

  The Azure Bot registration itself (README step 1) must already exist; pass its name with -BotName
  and the script points its messaging endpoint at the new site.

.EXAMPLE
  ./scripts/Deploy-Azure.ps1
  Uses the defaults and user secrets. Site becomes https://napoleonskake-bot.azurewebsites.net

.EXAMPLE
  ./scripts/Deploy-Azure.ps1 -AppName kake-vitec -Location westeurope -SkipInfrastructure
  Only publishes and deploys to an existing site.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = "rg-napoleonskake",
    [string] $AppName = "napoleonskake-bot",       # becomes https://<AppName>.azurewebsites.net
    [string] $PlanName = "plan-napoleonskake",
    [string] $Location = "norwayeast",
    [string] $Sku = "B1",
    [string] $BotName = "napoleonskake-bot",       # Azure Bot resource from README step 1; "" to skip updating its endpoint

    [string] $AppId,
    [string] $AppPassword,
    [string] $TenantId,

    # Skip plan/site creation and configuration; just publish and deploy.
    [switch] $SkipInfrastructure
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/NapoleonBot"
$siteUrl = "https://$AppName.azurewebsites.net"

function Invoke-Az {
    # az writes warnings to stderr; only treat a non-zero exit code as failure.
    $output = & az @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az $($args -join ' ') failed:`n$output"
    }
    return $output
}

# ---- Credentials -------------------------------------------------------------------------------

if (-not $AppId -or -not $AppPassword -or -not $TenantId) {
    Write-Host "Reading bot credentials from user secrets..."
    $secrets = @{}
    dotnet user-secrets list --project $project | ForEach-Object {
        if ($_ -match '^\s*([^=]+?)\s*=\s*(.*)$') { $secrets[$Matches[1]] = $Matches[2] }
    }
    if (-not $AppId)       { $AppId = $secrets["MicrosoftAppId"] }
    if (-not $AppPassword) { $AppPassword = $secrets["MicrosoftAppPassword"] }
    if (-not $TenantId)    { $TenantId = $secrets["MicrosoftAppTenantId"] }
}
$required = @(
    @{ Param = "AppId";       Secret = "MicrosoftAppId";       Value = $AppId },
    @{ Param = "AppPassword"; Secret = "MicrosoftAppPassword"; Value = $AppPassword },
    @{ Param = "TenantId";    Secret = "MicrosoftAppTenantId"; Value = $TenantId }
)
foreach ($item in $required) {
    if (-not $item.Value) {
        throw "Missing $($item.Param). Pass -$($item.Param) or run 'dotnet user-secrets set $($item.Secret) <value> --project src/NapoleonBot' (see README step 2)."
    }
}

Invoke-Az account show | Out-Null   # fails with a clear message if not logged in

# ---- Runtime -----------------------------------------------------------------------------------

$targetFramework = ([xml](Get-Content (Join-Path $project "NapoleonBot.csproj"))).Project.PropertyGroup.TargetFramework | Select-Object -First 1
$dotnetVersion = $targetFramework -replace '^net', ''       # net10.0 -> 10.0
$runtime = "DOTNETCORE:$dotnetVersion"
$selfContained = $false

if (-not $SkipInfrastructure) {
    $available = Invoke-Az webapp list-runtimes --os linux -o tsv
    if ($available -notcontains $runtime) {
        $fallback = ($available | Where-Object { $_ -like "DOTNETCORE:*" } | Sort-Object -Descending | Select-Object -First 1)
        Write-Warning "App Service does not offer $runtime yet. Using $fallback with a self-contained publish instead."
        $runtime = $fallback
        $selfContained = $true
    }
}

# ---- Infrastructure ----------------------------------------------------------------------------

if (-not $SkipInfrastructure) {
    Write-Host "Ensuring resource group $ResourceGroup in $Location..."
    Invoke-Az group create -n $ResourceGroup -l $Location -o none

    Write-Host "Ensuring App Service plan $PlanName ($Sku, Linux)..."
    Invoke-Az appservice plan create -g $ResourceGroup -n $PlanName --is-linux --sku $Sku -l $Location -o none

    $exists = (& az webapp show -g $ResourceGroup -n $AppName --query name -o tsv 2>$null)
    if ($exists) {
        Write-Host "Web app $AppName exists; updating."
        Invoke-Az webapp config set -g $ResourceGroup -n $AppName --linux-fx-version $runtime -o none
    }
    else {
        Write-Host "Creating web app $AppName ($runtime)..."
        Invoke-Az webapp create -g $ResourceGroup -n $AppName --plan $PlanName --runtime $runtime -o none
    }

    Write-Host "Configuring: HTTPS only, Always On, app settings..."
    Invoke-Az webapp update -g $ResourceGroup -n $AppName --https-only true -o none
    $config = @("--always-on", "true")
    if ($selfContained) { $config += @("--startup-file", "./NapoleonBot") }
    Invoke-Az webapp config set -g $ResourceGroup -n $AppName @config -o none

    Invoke-Az webapp config appsettings set -g $ResourceGroup -n $AppName -o none --settings `
        "MicrosoftAppType=SingleTenant" `
        "MicrosoftAppId=$AppId" `
        "MicrosoftAppPassword=$AppPassword" `
        "MicrosoftAppTenantId=$TenantId" `
        "Storage__ConnectionString=Data Source=/home/db/napoleon.db" `
        "WEBSITES_ENABLE_APP_SERVICE_STORAGE=true"
}

# ---- Publish and deploy ------------------------------------------------------------------------

$publishDir = Join-Path $root "publish"
$zip = Join-Path $root "publish.zip"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Publishing..."
$publishArgs = @("publish", $project, "-c", "Release", "-o", $publishDir, "--nologo", "-v", "quiet")
if ($selfContained) { $publishArgs += @("--self-contained", "-r", "linux-x64") }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip

Write-Host "Deploying to $AppName..."
Invoke-Az webapp deploy -g $ResourceGroup -n $AppName --src-path $zip --type zip -o none

Remove-Item -Recurse -Force $publishDir
Remove-Item -Force $zip

# ---- Point the Azure Bot at the site -----------------------------------------------------------

if ($BotName) {
    Write-Host "Setting messaging endpoint on Azure Bot $BotName..."
    Invoke-Az bot update -g $ResourceGroup -n $BotName --endpoint "$siteUrl/api/messages" -o none
}

# ---- Health check ------------------------------------------------------------------------------

Write-Host "Waiting for $siteUrl/healthz..."
$healthy = $false
for ($i = 0; $i -lt 12 -and -not $healthy; $i++) {
    try {
        $healthy = (Invoke-WebRequest "$siteUrl/healthz" -UseBasicParsing -TimeoutSec 15).StatusCode -eq 200
    }
    catch { Start-Sleep 10 }
}

if ($healthy) {
    Write-Host "Deployed. Bot endpoint: $siteUrl/api/messages" -ForegroundColor Green
}
else {
    Write-Warning "Deployed, but $siteUrl/healthz did not answer yet. Check logs with: az webapp log tail -g $ResourceGroup -n $AppName"
}
