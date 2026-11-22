# Windows x64 release 1.1.0

The release asset is a self-contained .NET 10 WPF executable. It starts in Demo Mode and adds optional authenticated Active Directory browsing. The v1.0 source checkpoint is `7de8ad8`. Existing demo workspaces use the same on-disk schema.

## Local verification on Windows 11

The Release solution built with zero warnings and errors. All 25 original core cases and 14 added live-mode cases passed. The published x64 application passed the interactive desktop smoke test, including Connection Manager validation and the existing demo workflows.

The lab IP responded to ping and accepted TCP connections on ports 88, 389 and 636. Direct queries to lab DNS returned the controller A record and Kerberos SRV record. Windows default hostname resolution failed, and a TLS handshake to the lab IP using the controller hostname was closed by the peer. No lab credential was used. Successful domain authentication, actual directory contents and authenticated refresh/export remain manual checks.

## Prepare a GitHub release

1. Build the complete solution and run all MSTest suites using the README commands.
2. Publish the self-contained executable and run the desktop smoke test against that executable.
3. Record the SHA-256 digest with `Get-FileHash .\artifacts\win-x64\ADControlCenter.exe -Algorithm SHA256`.
4. Review the source changes, include only synthetic screenshots, and keep live exports and credentials outside Git.
5. When publishing, create a GitHub release for the reviewed commit with tag `v1.1.0`. Attach the executable and its digest. Use the changelog for the feature list and record the actual lab acceptance results.

The application uses Windows certificate trust and Kerberos policy. Host DNS, server certificate trust and authenticated lab reads are environment-specific checks. [Manual test procedure](live-lab.md)
