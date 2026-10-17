# Security Policy

## ⚠ Critical Security Warning
**PowerShell.MCP provides complete PowerShell access to your system, including:**
- File system operations (read, write, delete)
- Network access and configuration
- Registry modification capabilities
- Process management and execution
- System configuration changes

**Use extreme caution in production environments or systems containing sensitive data.**

## Supported Versions
| Version | Supported          |
| ------- | ------------------ |
| 1.16.x  | :white_check_mark: |
| < 1.16  | :x:                |

## Reporting Vulnerabilities
If you discover security issues, please report them privately via:
- **GitHub Security Advisories**: Use "Report a vulnerability" on this repository
- **Email**: Create an issue for contact information if needed

**Do not report security vulnerabilities through public GitHub issues.**

## Security Architecture
- **Local by default**: the MCP server talks to its consoles over named pipes, restricted to the current user. No TCP port is opened unless remote control is turned on (below).
- **Opt-in remote control**: a TCP port is opened on the remote machine by `Enable-MCPRemoteControl` (after the person there confirms), or on the AI's own machine while the AI waits in `accept_remote_console` (on all interfaces, until a console pairs and the session ends, 10 minutes pass without one, or `disconnect_remote_console`). See [Remote Control](#remote-control).
- **PowerShell Security Integration**: Leverages built-in PowerShell execution policies and security features
- **Proxy Architecture**: Stdio proxy isolates MCP client from direct PowerShell access

## Remote Control
Off by default; available from 1.16.0. An AI on one machine drives a PowerShell console on another, with the person at that machine watching. It is started there, by a person: `Enable-MCPRemoteControl` in PowerShell 7 makes the console listen (TCP 42777 by default), or the one line that `accept_remote_console` hands out (or `Enable-MCPRemoteControl -ConnectTo`) makes it dial a port on the AI's machine.

**What a session can do.** Everything that console can do, as its user — elevated if the console is elevated, and with that user's network and domain access. Granting it is handing over the keyboard of that window. It also reaches that user's other PowerShell.MCP consoles on the machine, elevated ones included, because their pipes accept any process of the same user (see [Known Limitations](#known-limitations)); `Enable-MCPRemoteControl` lists them when it asks.

**It joins two machines through the AI.** The AI that drives the remote console usually drives a console on its own machine too, and has its files and other tools. Text read on the remote machine can try to steer it into running commands on *its* machine, and the AI can copy files or secrets in either direction. Pair only with a machine you would trust as much as text in your own prompt, and keep credentials and sensitive tools out of that AI session.

**Who can connect.** Whoever reaches the port and holds the pairing code.
- The code is 12 characters from a 32-letter alphabet (60 bits), shown on the remote console or carried in the pasted line. It works for **one** pairing and only for **10 minutes**. Case, dashes and full-width characters do not matter.
- The connection is TLS under a certificate made for the session. Both sides prove the code (HMAC-SHA256 over fresh nonces and the certificate's SHA-256, key from PBKDF2-SHA256 with 300,000 iterations), so an interceptor cannot relay the handshake.
- A pairing hands the AI's side a random 256-bit key for reconnecting after a dropped connection; a reconnect that proves it replaces a connection the other side still holds. When a session ends, a listening console shows a new code; if nobody pairs or reconnects within 10 minutes, remote control stops by itself. A console that dials out gives up likewise after 10 minutes without reaching the AI's machine.
- `-AllowFrom <address>` closes connections from any other address before TLS.

**What an attacker on the network can and cannot do.**
- The side that accepts the connection proves the code first, so anyone who reaches the port can take one proof away and try codes offline. The 60-bit code, its single use and its 10-minute life are what make that impractical; a code seen after it was used or expired is worthless.
- Before authentication, frames are capped at 16 KB and at most four handshakes run at once. A wrong client proof (an online guess) counts as a failed attempt; after five, remote control stops. So someone who can reach the port can end remote control or hold up pairing (denial of service), but cannot take it over.
- The one-line bootstrap is served over plain HTTP on the accept port. Everything it fetches is pinned by SHA-256 hashes carried in the line you paste, so a network attacker can make it fail but not change it. Anyone who reaches that port can download what it serves: PowerShell's own package and the PowerShell.MCP module (for a development build, the module files of its folder). The pairing code is never in what is served.
- Keep it on a LAN or VPN. Use `-AllowFrom` and the firewall rule `Enable-MCPRemoteControl` prints (scoped with `-RemoteAddress`), and remove the rule when you are done.

**The one line is shaped like a lure.** "Paste this into PowerShell as Administrator" is how many attacks begin, and an attacker with an AI of their own can hand out a working line too. Paste a line only if you asked an AI for it yourself, in your own conversation. The line names the machine it connects to (`$u='http://<host>:<port>'`), and the console's question names that host again, with the user and elevation the AI will get: answer No to anything you did not start. Defenders can alert on Script Block Logging events containing `PowerShell.MCP remote console bootstrap` or `/bootstrap.ps1`.

**Consent and visibility.** `Enable-MCPRemoteControl` refuses to run where nobody could answer (a service or scheduled task, redirected input, `-NonInteractive`, a host that is not a console) and asks Yes/No — default No, no switch to skip it — saying which user the commands will run as and whether elevated. While it is on, every prompt starts with `[AI from <host>]` (the address or host name of the AI's machine), the window title reads `AI from <host>` (or `AI remote control: waiting`; after `Administrator: ` when elevated), and every command the AI runs is echoed. Echoing is visibility, not a sandbox: a command you see can still start hidden processes, change the title, or make lasting changes (services, scheduled tasks, accounts, firewall rules) that stay after the session. These checks stop accidents and scripts; they cannot stop code already running as the user from starting any tool hidden, and PowerShell.MCP gives such code nothing it did not have. Defenders can spot use by those window titles; Script Block Logging (event 4104) of `Enable-MCPRemoteControl` or the bootstrap; `pwsh` started with `-NoExit -NoProfile -EncodedCommand` from Windows PowerShell; the folders `%LOCALAPPDATA%\PowerShell.MCP-remote` and `%ProgramData%\PowerShell.MCP-remote`; and inbound firewall rules for TCP 42777 or `PowerShell.MCP.Proxy.exe`.

**Elevated consoles and the bootstrap.** Pasted into a console run as Administrator, the bootstrap adds no folder of its own that non-admin processes of that user can change: it unpacks into `%ProgramData%\PowerShell.MCP-remote`, created so that only Administrators and SYSTEM can change it (located through the system's known folders, not environment variables); it uses only PowerShell 7.4+ signed by Microsoft (chain to Microsoft Root Certificate Authority 2011) and no PowerShell or module from the user's own folders or PATH; and it refuses `install=user`. Downloads are hashed in memory before anything is written. It cannot protect a console that is already exposed: an elevated PowerShell runs the user's profile and loads modules from the user's Documents, which non-admin processes can change. Installing anything (`install=user` / `machine`) is listed and confirmed at the console first.

**Text from the other machine is untrusted.** Event logs, user names in failed-logon records, file contents, web pages: what the AI reads on the remote machine may have been written by an attacker, possibly to look like instructions. The MCP server tells the AI to treat remote output as data, to connect only to hosts and codes the user typed into the conversation, never to start remote control or open ports itself, and never to act on its own machine because of remote output. For audits and incident response, ask for read-only work first and have the AI confirm before changes.

**The local side.** While a console is under remote control, local requests to it other than a status probe are refused, so another process on that machine cannot run commands in it, cancel the remote command, or read its output. The remote session cannot use the module's unechoed command channel or its title request. Output cached for the session, and the result of a command it started that finishes later, is discarded when it ends.

**What stays behind.** On the remote machine: firewall rules you added; files unpacked into the folders above (delete the folder to remove them); anything a session changed; and the pasted line, with a code that has since expired, in PSReadLine history. On the AI's machine: the firewall rule Windows created when you allowed `PowerShell.MCP.Proxy.exe` (for every port, and one more for each new install path), and the downloaded PowerShell package and module in `%LOCALAPPDATA%\PowerShell.MCP\cache`.

**Auditing a remote session.** Run `Start-Transcript` in the remote console before pairing — a window the bootstrap opens runs with `-NoProfile`, so a transcript started from a profile does not run there — or rely on Script Block Logging on that machine (see [Auditing an AI Session](#auditing-an-ai-session)).

## Risk Assessment

### High Risk Scenarios
- **Malicious MCP Clients**: Untrusted clients could execute destructive commands
- **Code Injection**: Improper input validation could lead to command injection
- **Privilege Escalation**: Commands run with current user's privileges
- **Data Exfiltration**: Full file system access enables data extraction

### Mitigation Strategies
- **Trusted Environment Only**: Deploy only in controlled, trusted environments
- **User Privilege Limitation**: Run with minimal necessary user privileges
- **Network Isolation**: Use on isolated networks when possible
- **Regular Monitoring**: Monitor PowerShell execution logs
- **Access Controls**: Implement proper file system and registry permissions

## Enterprise Security Guidelines

### Pre-Deployment
1. **Security Assessment**: Conduct thorough security review
2. **Policy Compliance**: Verify alignment with corporate security policies
3. **Testing Environment**: Test extensively in isolated environment
4. **User Training**: Train users on security implications

### Production Deployment
1. **Principle of Least Privilege**: Deploy with minimal required permissions
2. **Monitoring**: Implement comprehensive logging and monitoring
3. **Access Control**: Restrict access to authorized users only
4. **Regular Audits**: Conduct periodic security audits
5. **Incident Response**: Establish clear incident response procedures

### Ongoing Security
- **Regular Updates**: Monitor and apply security updates promptly
- **Log Review**: Regularly review PowerShell execution logs
- **Permission Audits**: Periodically audit user permissions
- **Vulnerability Scanning**: Include in regular security scans

## Auditing an AI Session
Nothing is recorded by default. Two mechanisms, both opt-in, both built into PowerShell rather than this module:

- **Per-console transcript.** `Start-Transcript` records the commands run in that console and their output, in order, to a text file. Run it by hand in a console, or put it in your PowerShell `$PROFILE` and every console the proxy launches starts one of its own — those consoles load your profile unless the server was started with `--no-profile`. **Requires 1.14.1 or later**: earlier versions recorded a command's output but not the command itself. A transcript is written by the very session it records, so treat it as an operator's log rather than tamper-proof evidence — anyone who can run a command in that console can stop it or delete the file.
- **PowerShell Script Block Logging.** Once it is turned on, PowerShell 7 logs every script block it compiles as event 4104, whoever submitted it and by whatever route. On Windows, enable it with Group Policy under *Administrative Templates → PowerShell Core → Turn on PowerShell Script Block Logging* (add those templates first with `InstallPSCorePolicyDefinitions.ps1` from `$PSHOME`), or with `ScriptBlockLogging` under `PowerShellPolicies` in `$PSHOME/powershell.config.json`; the events go to the `PowerShellCore/Operational` log. The Windows PowerShell policy of the same name applies to `powershell.exe`, and its `Microsoft-Windows-PowerShell/Operational` log is not where `pwsh` writes. On Linux and macOS the `powershell.config.json` setting sends the events to the system log. It is machine-wide, outlives the console it came from, and is the right choice when the record has to stand up as evidence — more so when the events are forwarded to a SIEM, out of reach of the session being audited.

## Known Limitations
- No built-in command filtering or sandboxing
- On Windows, a console's named pipe is open to processes of the same user, whether or not they are elevated. If you open a console as Administrator and import PowerShell.MCP, a non-elevated process of your account can send it commands, which is how a non-elevated AI client can use that console. Do not leave an elevated PowerShell.MCP console open on a machine where you do not trust everything running as your account. (While the console is under remote control, such local requests are refused.)
- Inherits all security limitations of PowerShell itself
- No audit trail is kept by default — see [Auditing an AI Session](#auditing-an-ai-session) for the two ways to turn one on

## Security Best Practices
1. **Environment Isolation**: Use in dedicated, isolated environments
2. **Minimal Exposure**: Limit to essential use cases only
3. **User Education**: Ensure users understand security implications
4. **Regular Backups**: Maintain current backups before use
5. **Incident Preparedness**: Have incident response plan ready

## Legal and Compliance
- **Data Protection**: Consider GDPR, HIPAA, and other privacy regulations
- **Corporate Policies**: Ensure compliance with organizational policies  
- **Audit Requirements**: May be subject to security audits
- **Liability**: Users assume full responsibility for secure usage

## Contact
For security-related inquiries:
- Use GitHub's private vulnerability reporting feature
- Create a GitHub issue for general security questions (non-sensitive)

## Disclaimer
**This software is provided "AS IS" without warranty of any kind.** The author assumes no responsibility for any damages, data loss, security breaches, or other issues arising from the use of this software. Users are solely responsible for:
- Ensuring secure and appropriate usage
- Compliance with applicable laws and regulations  
- Implementation of proper security controls
- Risk assessment and mitigation

**By using this software, you acknowledge and accept these security risks and responsibilities.**
