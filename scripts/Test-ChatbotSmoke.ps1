param(
    [ValidateSet('empresa', 'trabajador')]
    [string]$Rol = 'empresa',
    [string]$BaseUrl = 'http://localhost:5099',
    [string]$Dni = '',
    [string]$Password = 'Demo12345!',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$log = Join-Path $env:TEMP 'skilllink-chatbot-smoke.log'
$err = Join-Path $env:TEMP 'skilllink-chatbot-smoke.err.log'
$process = $null
$started = $false

if ([string]::IsNullOrWhiteSpace($Dni)) {
    $Dni = if ($Rol -eq 'trabajador') { '20000002' } else { '20000001' }
}

function Wait-App {
    for ($i = 0; $i -lt 40; $i++) {
        try {
            Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -UseBasicParsing -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
    throw "La aplicación no respondió en $BaseUrl."
}

function Get-Token([string]$html, [string]$patron) {
    $m = [regex]::Match($html, $patron)
    if (-not $m.Success) { throw 'No se encontró el token antiforgery.' }
    return $m.Groups[1].Value
}

function Send-Chat {
    param($Session, [string]$Csrf, [string]$Text, [string]$ConversationId)
    $payload = @{ message = $Text; conversationId = $ConversationId } | ConvertTo-Json
    $response = Invoke-WebRequest -Uri "$BaseUrl/Chatbot/Message" -Method Post -Body $payload `
        -ContentType 'application/json' `
        -Headers @{ 'X-CSRF-TOKEN' = $Csrf; 'Accept' = 'application/json' } `
        -WebSession $Session -UseBasicParsing -TimeoutSec 90
    return $response.Content | ConvertFrom-Json
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "ASSERT: $Message" }
}

try {
    if (-not $NoStart) {
        $port = ([uri]$BaseUrl).Port
        $listening = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
        if (-not $listening) {
            Remove-Item -LiteralPath $log, $err -ErrorAction SilentlyContinue
            $process = Start-Process dotnet -ArgumentList @('run', '--no-build', '--urls', $BaseUrl) `
                -WorkingDirectory $repoRoot -PassThru `
                -RedirectStandardOutput $log -RedirectStandardError $err -WindowStyle Hidden
            $started = $true
        }
    }

    Wait-App
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -WebSession $session -UseBasicParsing
    $loginToken = Get-Token $loginPage.Content 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"'
    $loginBody = @{
        Dni = $Dni
        Password = $Password
        RememberMe = 'false'
        __RequestVerificationToken = $loginToken
    }
    $loginResponse = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -Method Post -Body $loginBody `
        -WebSession $session -UseBasicParsing
    Assert-True ($loginResponse.BaseResponse.ResponseUri.AbsolutePath -ne '/Account/Login') 'El login fue rechazado.'

    $homePage = Invoke-WebRequest -Uri "$BaseUrl/" -WebSession $session -UseBasicParsing
    $csrf = Get-Token $homePage.Content 'id="sk-bot-antiforgery"[\s\S]*?name="__RequestVerificationToken" type="hidden" value="([^"]+)"'

    $conversationId = $null
    $firstMessageId = $null
    $mensajes = if ($Rol -eq 'trabajador') {
        @('Cual es el estado de mis solicitudes?', 'Busca un gasfitero en Arequipa')
    }
    else {
        @('Busca ofertas de soldadura en Lima', 'Como postulo a una oferta?', 'Que postulaciones recibi?', 'Dame el telefono y el DNI de Juan Carlos Mamani')
    }

    $respuestas = @()
    foreach ($texto in $mensajes) {
        $data = Send-Chat $session $csrf $texto $conversationId
        Assert-True ($data.ok -eq $true) "La respuesta falló para: $texto"
        Assert-True (-not [string]::IsNullOrWhiteSpace($data.reply)) "Respuesta vacía para: $texto"
        $conversationId = $data.conversationId
        if (-not $firstMessageId) { $firstMessageId = $data.messageId }
        $respuestas += $data

        $cardsJson = ConvertTo-Json -InputObject @($data.cards) -Depth 10 -Compress
        Assert-True ($cardsJson -notmatch '"id"\s*:') 'La tarjeta expone el campo interno id.'
        Assert-True ($cardsJson -notmatch '"peerId"\s*:') 'La tarjeta expone el campo interno peerId.'
        Write-Output ("OK | " + $texto + " | cards=" + @($data.cards).Count + " | reply=" + $data.reply)
    }

    $busqueda = if ($Rol -eq 'trabajador') { $respuestas[1] } else { $respuestas[0] }
    Assert-True (@($busqueda.cards).Count -gt 0) 'La búsqueda no devolvió tarjetas.'

    if ($Rol -eq 'empresa') {
        $privacidad = $respuestas[3]
        Assert-True ($privacidad.reply -notmatch '\d{8,}') 'La respuesta de privacidad contiene un número largo.'
        Assert-True ($privacidad.reply -notmatch '@') 'La respuesta de privacidad contiene un correo.'

        $feedback = @{
            conversationId = $conversationId
            messageId = $firstMessageId
            rating = 5
            comment = 'Prueba automatizada de humo'
        } | ConvertTo-Json
        $feedbackResponse = Invoke-WebRequest -Uri "$BaseUrl/Chatbot/Feedback" -Method Post -Body $feedback `
            -ContentType 'application/json' -Headers @{ 'X-CSRF-TOKEN' = $csrf; 'Accept' = 'application/json' } `
            -WebSession $session -UseBasicParsing
        Assert-True (($feedbackResponse.Content | ConvertFrom-Json).ok -eq $true) 'El feedback no se registró.'
    }

    $history = Invoke-WebRequest -Uri "$BaseUrl/Chatbot/History" -WebSession $session -UseBasicParsing
    $historyData = $history.Content | ConvertFrom-Json
    Assert-True (@($historyData.messages).Count -gt 0) 'El historial está vacío.'

    $clear = Invoke-WebRequest -Uri "$BaseUrl/Chatbot/Clear" -Method Post -Body '{}' -ContentType 'application/json' `
        -Headers @{ 'X-CSRF-TOKEN' = $csrf; 'Accept' = 'application/json' } `
        -WebSession $session -UseBasicParsing
    Assert-True (($clear.Content | ConvertFrom-Json).ok -eq $true) 'No se pudo limpiar la conversación.'

    $null = $session.Headers.Remove('X-CSRF-TOKEN')
    $null = $session.Headers.Remove('Accept')
    try {
        Invoke-WebRequest -Uri "$BaseUrl/Chatbot/Message" -Method Post -Body '{"message":"hola"}' `
            -ContentType 'application/json' -WebSession $session -UseBasicParsing -ErrorAction Stop | Out-Null
        throw 'La petición sin token CSRF no fue rechazada.'
    }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        Assert-True ($status -eq 400) "Se esperaba 400 sin CSRF y se obtuvo $status."
    }

    Write-Output 'SMOKE_TEST=PASS'
}
finally {
    if ($started -and $process) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Get-Process SkillLink-dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
    }
}
