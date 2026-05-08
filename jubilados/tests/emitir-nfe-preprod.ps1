param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBase = "https://resolutoo.com",

    [Parameter(Mandatory = $false)]
    [string]$EmpresaId = "dd104b57-010a-4458-8699-d63807e205d3",

    [Parameter(Mandatory = $false)]
    [string]$ProdutoId = "9ae15d08-81e9-4767-911e-8faf96ad2821",

    [Parameter(Mandatory = $false)]
    [string]$ClienteId = "0865f76e-bff7-48ef-8b02-f24b6a468e3d",

    [Parameter(Mandatory = $false)]
    [int]$MaxTentativas = 8,

    [Parameter(Mandatory = $false)]
    [int]$IntervaloSegundos = 8,

    [Parameter(Mandatory = $false)]
    [switch]$Anonimo
)

$ErrorActionPreference = "Stop"

function New-Body {
    param(
        [string]$Empresa,
        [string]$Produto,
        [string]$Cliente,
        [bool]$UsarAnonimo
    )

    $item = @{
        produtoId = $Produto
        quantidade = 1
        valorUnitario = 100
        valorDesconto = 0
    }

    if ($UsarAnonimo) {
        return @{
            empresaId = $Empresa
            clienteId = $null
            naturezaOperacao = "Venda de Mercadoria"
            serie = "1"
            itens = @($item)
            valorFrete = 0
            valorSeguro = 0
            valorDesconto = 0
            formaPagamento = "01"
            informacaoComplementar = "Teste automatizado pre-producao"
            destinatarioCpfCnpj = $null
            destinatarioNome = $null
        }
    }

    return @{
        empresaId = $Empresa
        clienteId = $Cliente
        naturezaOperacao = "Venda de Mercadoria"
        serie = "1"
        itens = @($item)
        valorFrete = 0
        valorSeguro = 0
        valorDesconto = 0
        formaPagamento = "01"
        informacaoComplementar = "Teste automatizado pre-producao"
    }
}

$uri = ($ApiBase.TrimEnd('/') + "/api/nfe/emitir")
Write-Host "[preprod] endpoint: $uri"
Write-Host "[preprod] maxTentativas=$MaxTentativas intervalo=${IntervaloSegundos}s anonimo=$Anonimo"

for ($i = 1; $i -le $MaxTentativas; $i++) {
    $bodyObj = New-Body -Empresa $EmpresaId -Produto $ProdutoId -Cliente $ClienteId -UsarAnonimo $Anonimo.IsPresent
    $json = $bodyObj | ConvertTo-Json -Depth 8

    Write-Host "[preprod] tentativa $i/$MaxTentativas"

    try {
        $resp = Invoke-WebRequest -Uri $uri -Method POST -ContentType "application/json" -Body $json -TimeoutSec 90 -UseBasicParsing
        $content = if ([string]::IsNullOrWhiteSpace($resp.Content)) { "{}" } else { $resp.Content }
        $obj = $content | ConvertFrom-Json

        $cStat = ""
        if ($obj.PSObject.Properties.Name -contains "cStat") { $cStat = [string]$obj.cStat }

        if ($resp.StatusCode -eq 200 -and $cStat -eq "100") {
            Write-Host "[preprod] SUCESSO: cStat=100"
            Write-Host ("[preprod] chave=" + $obj.chaveAcesso + " protocolo=" + $obj.protocolo)
            exit 0
        }

        $motivo = ""
        if ($obj.PSObject.Properties.Name -contains "xMotivo") { $motivo = [string]$obj.xMotivo }
        if ($obj.PSObject.Properties.Name -contains "erro") { $motivo = [string]$obj.erro }
        if ($obj.PSObject.Properties.Name -contains "detalhe" -and -not [string]::IsNullOrWhiteSpace([string]$obj.detalhe)) {
            $motivo = ($motivo + " | detalhe=" + [string]$obj.detalhe).Trim(' ','|')
        }

        Write-Host ("[preprod] retorno HTTP=" + [string]$resp.StatusCode + " cStat=" + $cStat + " motivo=" + $motivo)
    }
    catch {
        $httpResp = $_.Exception.Response
        if ($httpResp -ne $null) {
            $reader = New-Object System.IO.StreamReader($httpResp.GetResponseStream())
            $raw = $reader.ReadToEnd()
            $reader.Close()
            Write-Host ("[preprod] erro HTTP: " + $httpResp.StatusCode.value__ + " body=" + $raw)
        }
        else {
            Write-Host ("[preprod] erro de rede: " + $_.Exception.Message)
        }
    }

    if ($i -lt $MaxTentativas) {
        Start-Sleep -Seconds $IntervaloSegundos
    }
}

Write-Error "[preprod] Falha: nao obteve cStat=100 apos $MaxTentativas tentativas."
exit 1
