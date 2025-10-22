# Contributing to PowerShell.MCP

Thank you for your interest in contributing to PowerShell.MCP! This document provides guidelines for contributing to the project.

## Reporting Issues

Before creating a new issue, please search existing issues to avoid duplicates.

### Bug Report Template
When reporting bugs, please include:
- PowerShell version
- Windows version
- PowerShell.MCP version
- Steps to reproduce
- Expected behavior
- Actual behavior
- Security implications (if any)

### Feature Requests
We welcome feature proposals! Please include:
- Clear description of the requested feature
- Use case and benefits
- Potential security considerations
- Implementation suggestions (if applicable)

## Security Issues

**Do not report security vulnerabilities through public GitHub issues.**

Instead, please send a detailed description to [email-address]. Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested remediation (if known)

## Pull Requests

1. **Fork the repository**
2. **Create a feature branch**: git checkout -b feature-name
3. **Add tests** for new functionality
4. **Run security checks** before submitting
5. **Update documentation** as needed
6. **Create a pull request**

### Pull Request Guidelines
- Provide clear description of changes
- Include test coverage for new features
- Ensure backward compatibility when possible
- Follow existing code style and conventions
- Update README.md if adding new features

## Development Setup

1. Clone your fork: git clone https://github.com/your-username/PowerShell.MCP.git
2. Build the project: dotnet build PowerShell.MCP.sln -c Release
3. Test locally before submitting changes

## Code Style

- Follow standard C# conventions
- Use meaningful variable and method names
- Include appropriate comments for complex logic
- Maintain security-first mindset

## Security Guidelines

When contributing:
- Consider security implications of all changes
- Avoid introducing new attack vectors
- Test in isolated environments
- Document security considerations

## Questions?

Feel free to open a discussion for questions about:
- Implementation approaches
- Feature ideas
- Best practices
- Security considerations

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
