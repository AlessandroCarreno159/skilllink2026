param(
    [string]$BaseUrl = 'http://localhost:5099',
    [string]$GatewayUrl = 'http://localhost:3001',
    [string]$Dni = '20000002',
    [string]$Password = 'Demo12345!',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$log = Join-Path $env:TEMP 'skilllink-chatbot-realtime.log'
$err = Join-Path $env:TEMP 'skilllink-chatbot-realtime.err.log'
$output = Join-Path $env:TEMP 'skilllink-chatbot-realtime-events.json'
$ready = "$output.ready"
$appProcess = $null
$nodeProcess = $null
$started = $false

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERT: $Message" }
}

try {
    try {
        $health = Invoke-WebRequest -Uri "$GatewayUrl/health" -UseBasicParsing -TimeoutSec 5
        Assert-True ($health.StatusCode -eq 200) 'El gateway no reporta Redis disponible.'
    }
    catch {
        throw "El gateway de tiempo real no está disponible en $GatewayUrl. Inicia Redis y el gateway (npm start) antes de ejecutar esta prueba."
    }

    if (-not $NoStart) {
        $port = ([uri]$BaseUrl).Port
        $listening = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
        if (-not $listening) {
            Remove-Item -LiteralPath $log, $err -ErrorAction SilentlyContinue
            $env:Redis__Url = 'redis://127.0.0.1:6379'
            $appProcess = Start-Process dotnet -ArgumentList @('run', '--no-build', '--urls', $BaseUrl) `
                -WorkingDirectory $repoRoot -PassThru `
                -RedirectStandardOutput $log -RedirectStandardError $err -WindowStyle Hidden
            $started = $true
        }
    }

    for ($i = 0; $i -lt 40; $i++) {
        try {
            Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -UseBasicParsing -TimeoutSec 2 | Out-Null
            break
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -WebSession $session -UseBasicParsing
    $m = [regex]::Match($loginPage.Content, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
    $loginBody = @{
        Dni = $Dni
        Password = $Password
        RememberMe = 'false'
        __RequestVerificationToken = $m.Groups[1].Value
    }
    Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -Method Post -Body $loginBody -WebSession $session -UseBasicParsing | Out-Null

    $tokenResponse = Invoke-WebRequest -Uri "$BaseUrl/Realtime/Token" -WebSession $session -UseBasicParsing
    $tokenData = $tokenResponse.Content | ConvertFrom-Json
    Assert-True ($tokenData.enabled -eq $true -and -not [string]::IsNullOrWhiteSpace($tokenData.token)) 'El token de tiempo real no está habilitado.'

    Remove-Item -LiteralPath $output, $ready -ErrorAction SilentlyContinue
    $nodeProcess = Start-Process node -ArgumentList @('scripts\Test-ChatbotRealtime.js', $GatewayUrl, $tokenData.token, $output) `
        -WorkingDirectory $repoRoot -PassThru -WindowStyle Hidden

    for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $ready); $i++) {
        Start-Sleep -Milliseconds 500
    }
    Assert-True (Test-Path -LiteralPath $ready) 'El cliente Socket.io no confirmó la conexión.'

    $homePage = Invoke-WebRequest -Uri "$BaseUrl/" -WebSession $session -UseBasicParsing
    $m2 = [regex]::Match($homePage.Content, 'id="sk-bot-antiforgery"[\s\S]*?name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
    $csrf = $m2.Groups[1].Value
    $payload = @{ message = 'Busca un gasfitero en Arequipa'; conversationId = $null } | ConvertTo-Json
    Invoke-WebRequest -Uri "$BaseUrl/Chatbot/Message" -Method Post -Body $payload -ContentType 'application/json' `
        -Headers @{ 'X-CSRF-TOKEN' = $csrf; 'Accept' = 'application/json' } `
        -WebSession $session -UseBasicParsing -TimeoutSec 90 | Out-Null

    for ($i = 0; $i -lt 100 -and -not (Test-Path -LiteralPath $output); $i++) {
        Start-Sleep -Milliseconds 500
    }
    Assert-True (Test-Path -LiteralPath $output) 'El cliente Socket.io no recibió eventos del chatbot.'

    $events = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $tipos = @($events | ForEach-Object { $_.type })
    Assert-True ($tipos -contains 'realtime:ready') 'No se recibió realtime:ready.'
    Assert-True ($tipos -contains 'chatbot:token') 'No se recibieron tokens de streaming.'
    Assert-True ($tipos -contains 'chatbot:tool') 'No se recibió el evento de herramienta.'
    Assert-True ($tipos -contains 'chatbot:done') 'No se recibió chatbot:done.'

    $done = $events | Where-Object { $_.type -eq 'chatbot:done' } | Select-Object -First 1
    $cardsJson = ConvertTo-Json -InputObject @($done.payload.cards) -Depth 10 -Compress
    Assert-True ($cardsJson -notmatch '"id"\s*:') 'El evento chatbot:done expone ids internos.'
    Assert-True ($cardsJson -notmatch '"peerId"\s*:') 'El evento chatbot:done expone peerId.'

    Write-Output ('REALTIME_EVENTS=' + (($tipos | Select-Object -Unique) -join ','))
    Write-Output 'REALTIME_TEST=PASS'
}
finally {
    if ($nodeProcess -and -not $nodeProcess.HasExited) {
        Stop-Process -Id $nodeProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($started -and $appProcess) {
        Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue
        Get-Process SkillLink-dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
    }
}
