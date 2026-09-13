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
| 1.2.x   | :white_check_mark: |
| < 1.2   | :x:                |

## Reporting Vulnerabilities
If you discover security issues, please report them privately via:
- **GitHub Security Advisories**: Use "Report a vulnerability" on this repository
- **Email**: Create an issue for contact information if needed

**Do not report security vulnerabilities through public GitHub issues.**

## Security Architecture
- **Local Communication Only**: Named pipe communication restricts access to local machine
- **No Network Exposure**: No TCP ports opened, no remote access capability
- **PowerShell Security Integration**: Leverages built-in PowerShell execution policies and security features
- **Proxy Architecture**: Stdio proxy isolates MCP client from direct PowerShell access

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
- Commands executed via MCP cannot be canceled with Ctrl+C
- No built-in command filtering or sandboxing
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
