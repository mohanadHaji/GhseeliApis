#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ListenPrefix = 'http://127.0.0.1:50833/',
    [string]$BackendBaseUrl = 'http://[::1]:50832'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$listenUri = [uri]$ListenPrefix
$backendUri = [uri]$BackendBaseUrl
if (-not $listenUri.IsLoopback -or -not $backendUri.IsLoopback -or
    $listenUri.Scheme -ne 'http' -or $backendUri.Scheme -ne 'http') {
    throw 'The Step 17 proxy accepts loopback HTTP endpoints only.'
}
if ($listenUri.Host -cne '127.0.0.1' -or
    $backendUri.DnsSafeHost -cne '::1') {
    throw ('The Step 17 proxy requires an IPv4 client listener and IPv6 ' +
        'backend so trusted and direct socket peers cannot overlap.')
}

$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add($ListenPrefix)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseProxy = $false
$client = [Net.Http.HttpClient]::new($handler)

try {
    $listener.Start()
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $target = [uri]::new(
            $backendUri,
            $request.Url.PathAndQuery)
        $message = [Net.Http.HttpRequestMessage]::new(
            [Net.Http.HttpMethod]::new($request.HttpMethod),
            $target)
        try {
            if ($request.HasEntityBody) {
                $buffer = [IO.MemoryStream]::new()
                $request.InputStream.CopyTo($buffer)
                $message.Content = [Net.Http.ByteArrayContent]::new(
                    $buffer.ToArray())
                $buffer.Dispose()
                if (-not [string]::IsNullOrWhiteSpace($request.ContentType)) {
                    [void]$message.Content.Headers.TryAddWithoutValidation(
                        'Content-Type', $request.ContentType)
                }
            }
            foreach ($headerName in $request.Headers.AllKeys) {
                if ($headerName -in @(
                        'Host','Content-Length','Transfer-Encoding',
                        'Connection','X-Forwarded-For','X-Forwarded-Proto')) {
                    continue
                }
                if (-not $message.Headers.TryAddWithoutValidation(
                        $headerName, $request.Headers.GetValues($headerName)) -and
                    $null -ne $message.Content) {
                    [void]$message.Content.Headers.TryAddWithoutValidation(
                        $headerName, $request.Headers.GetValues($headerName))
                }
            }
            [void]$message.Headers.TryAddWithoutValidation(
                'X-Forwarded-For', '127.0.0.1')
            [void]$message.Headers.TryAddWithoutValidation(
                'X-Forwarded-Proto', 'https')

            $upstream = $client.SendAsync($message).GetAwaiter().GetResult()
            try {
                $context.Response.StatusCode = [int]$upstream.StatusCode
                foreach ($header in $upstream.Headers) {
                    if ($header.Key -in @(
                            'Connection','Content-Length','Date','Keep-Alive',
                            'Proxy-Authenticate','Proxy-Authorization','Server',
                            'TE','Trailer','Transfer-Encoding','Upgrade')) {
                        continue
                    }
                    foreach ($value in $header.Value) {
                        $context.Response.Headers.Add($header.Key, $value)
                    }
                }
                foreach ($header in $upstream.Content.Headers) {
                    if ($header.Key -in @(
                            'Content-Length','Transfer-Encoding','Connection')) {
                        continue
                    }
                    foreach ($value in $header.Value) {
                        $context.Response.Headers.Add($header.Key, $value)
                    }
                }
                $bytes = $upstream.Content.ReadAsByteArrayAsync().
                    GetAwaiter().GetResult()
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $upstream.Dispose()
                $context.Response.OutputStream.Close()
            }
        }
        finally {
            $message.Dispose()
        }
    }
}
finally {
    $listener.Close()
    $client.Dispose()
    $handler.Dispose()
}
