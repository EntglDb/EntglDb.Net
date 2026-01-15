# EntglDb Samples

This directory contains sample applications demonstrating **EntglDb** integration across different platforms using the **Lifter** hosting model.

## 🔗 Cross-Platform Cluster Compatibility

All samples are configured to run on the **same cluster** securely. You can run them simultaneously to test real-time P2P synchronization.

| Parameter | Value | Description |
| :--- | :--- | :--- |
| **AuthToken** | `demo-secret-key` | Shared secret for secure discovery |
| **Discovery Port** | `6000` (UDP) | Port for local peer discovery |
| **DB Mode** | SQLite (WAL) | Persistent storage |

### 🖥️ EntglDb.Sample.Console
Interactive CLI node. Good for monitoring the mesh.

**Run:**
```bash
cd EntglDb.Sample.Console
dotnet run
# OR simulate multiple nodes:
dotnet run node-2 5001
dotnet run node-3 5002
```

### 🪟 EntglDb.Test.Avalonia
Cross-platform Desktop UI (Windows, Linux, macOS).

**Run:**
```bash
cd EntglDb.Test.Avalonia
dotnet run
```

### 📱 EntglDb.Test.Maui
Mobile & Desktop (Android, iOS, macOS, Windows).

**Run (Windows):**
```bash
cd EntglDb.Test.Maui
dotnet build -t:Run -f net9.0-windows10.0.19041.0
```

## 🧪 Quick Test Scenario
1. Start **Console** app (creates `node-1`).
2. Start **Avalonia** app (creates `test-node-avalonia`).
3. In Console, type `l` to list peers: you should see the Avalonia node.
4. In Console, type `p` to put some data (`Alice`, `Bob`).
5. In Avalonia, load key `Alice` -> Data should appear!
