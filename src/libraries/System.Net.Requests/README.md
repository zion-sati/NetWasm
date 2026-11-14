# NetWasm.System.Net.Requests

`NetWasm.System.Net.Requests` supplies the `System.Net.Requests` assembly for the
`netwasm0.1` profile: `WebRequest`, `HttpWebRequest` and `HttpWebResponse`,
`WebProxy`, the request cache policies, and `WebClient`. It is an independently
versioned ported-library package for applications and libraries that still use
the legacy request APIs.

`HttpWebRequest` sends through `NetWasm.System.Net.Http`, following redirects
and keeping cookies itself. Use the asynchronous methods: waiting synchronously
on the network, FTP, file requests, system proxy discovery, and settings the
host's HTTP transport cannot honor (such as client certificates, certificate
callbacks or an explicit proxy) throw `PlatformNotSupportedException` before
anything is sent.

The package carries the `NetWasm,Version=v0.1` asset and targets only
`netwasm0.1`.
