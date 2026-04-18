# Third Party Notices

PowerShell.MCP redistributes the following third-party components.
See [LICENSE](LICENSE) for the PowerShell.MCP license itself (MIT).

---

## Ude.NetStandard

- **Version:** 1.2.0
- **Source:** https://github.com/yinyue200/ude
- **NuGet:** https://www.nuget.org/packages/Ude.NetStandard/
- **Author:** yinyue200 (and original Mozilla Universal Charset Detector contributors)
- **License:** Triple-licensed under MPL 1.1 / GPL 2.0 / LGPL 2.1
  (recipient may choose any one; PowerShell.MCP redistributes under LGPL 2.1)

PowerShell.MCP bundles `Ude.NetStandard.dll` (compiled binary, unmodified
from the official NuGet package) for character set detection. The DLL is
loaded via dynamic linking; users may replace the bundled copy with their
own build of Ude.NetStandard.

The full text of all three licenses is included under
[`licenses/Ude.NetStandard/`](licenses/Ude.NetStandard/).
