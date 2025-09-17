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

## Known Limitations
- Commands executed via MCP cannot be canceled with Ctrl+C
- No built-in command filtering or sandboxing
- Inherits all security limitations of PowerShell itself
- No audit trail for commands executed via MCP protocol

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
