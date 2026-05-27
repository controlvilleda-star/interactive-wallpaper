$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$htmlPath = Join-Path $root 'wallpaper.html'
$port = 8765
$url = "http://127.0.0.1:$port/wallpaper.html"

if (-not (Test-Path $htmlPath)) {
    throw "No se encontro wallpaper.html en $root"
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse('127.0.0.1'), $port)
$listener.Start()

Write-Host "Wallpaper servido en $url"
Write-Host "Deja esta ventana abierta mientras uses el video de YouTube. Pulsa Ctrl+C para cerrar."
Start-Process $url

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            while ($true) {
                $line = $reader.ReadLine()
                if ($null -eq $line -or $line.Length -eq 0) { break }
            }

            $path = '/'
            if ($requestLine -match '^\S+\s+([^\s]+)\s+') {
                $path = $Matches[1].Split('?')[0]
            }

            if ($path -eq '/' -or $path -eq '/wallpaper.html') {
                $bodyText = [System.IO.File]::ReadAllText($htmlPath, [System.Text.Encoding]::UTF8)
                $body = [System.Text.Encoding]::UTF8.GetBytes($bodyText)
                $headers = "HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nCache-Control: no-store`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($headers)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                $stream.Write($body, 0, $body.Length)
            } else {
                $body = [System.Text.Encoding]::UTF8.GetBytes('Not found')
                $headers = "HTTP/1.1 404 Not Found`r`nContent-Type: text/plain; charset=utf-8`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($headers)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                $stream.Write($body, 0, $body.Length)
            }
        } finally {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
}
