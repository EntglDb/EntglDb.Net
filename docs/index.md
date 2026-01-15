---
layout: home

hero:
  name: "EntglDb"
  text: "Peer-to-Peer Database for .NET"
  tagline: "Local-First. Offline-Capable. Mesh Sync."
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/your-repo/EntglDb

features:
  - title: Local First
    details: Data is stored locally on every node. Works explicitly offline.
  - title: Mesh Synchronization
    details: Nodes sync directly with each other via TCP/UDP without a central server.
  - title: .NET Native
    details: Built for .NET 10. Lightweight and embeddable in any C# application.
---

## ⚠️ Important Notice

**EntglDb is designed for Local Area Networks (LAN)**

This database is built for **trusted LAN environments** such as:
- Office networks
- Home networks  
- Private local networks
- Edge computing deployments

**Cross-Platform**: Runs on Windows, Linux, and macOS (.NET 10+)

**NOT for Public Internet**: EntglDb is **NOT** designed for public internet deployment without additional security measures (TLS, authentication, firewall rules).

See [Security Considerations](/architecture#security-disclaimer) for details.
