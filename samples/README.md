# EntglDb Samples

This directory contains sample applications demonstrating **EntglDb v0.6** integration across different platforms using the **Lifter** hosting model.

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
# Enable recursive merge conflict resolution:
dotnet run --merge
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
**All Nodes Must Use Same Security Mode:**
- Secure ↔ Secure: ✅ Works
- Plaintext ↔ Plaintext: ✅ Works
- Secure ↔ Plaintext: ❌ Connection fails

3. In Console, type `l` to list peers: you should see the Avalonia node.
4. In Console, type `p` to put some data (`Alice`, `Bob`).
5. In Avalonia, load key `Alice` → Data should appear!

## 🎨 UI Samples (v0.6 Features)

### Avalonia & MAUI New Features:

#### 📋 TodoList Manager
- Click "📋 Manage TodoLists" (MAUI footer button) or "📋 Manage TodoLists" (Avalonia button)
- Create/delete lists, add/check/delete items
- Real-time sync across all nodes

#### 🔀 Conflict Resolver Selection
- Choose between "Last Write Wins" and "Recursive Merge"
- Click "💾 Save" or "💾 Save & Restart"
- **Restart required** for changes to take effect
- Settings persisted:
  - **Avalonia**: `appsettings.json`
  - **MAUI**: `Preferences`

#### 🔬 Interactive Conflict Demo
- Click "🔬 Demo" or "🔬 Run Conflict Demo"
- Simulates concurrent edits to a TodoList
- Visual comparison of LWW vs Recursive Merge
- Shows merged results step-by-step

#### 🔒 Security Indicator
- **Avalonia/MAUI**: Always-on encryption (ECDH + AES-256)
- **Console**: `--secure` flag enables encryption
- Visual: 🔒 (encrypted) vs 🔓 (plaintext)

## 📚 Documentation

For complete v0.6 documentation, see:
- [Getting Started](../docs/v0.6/getting-started.html)
- [Security](../docs/v0.6/security.html)
- [Conflict Resolution](../docs/v0.6/conflict-resolution.html)
- [Architecture](../docs/v0.6/architecture.html)

## 🆕 Conflict Resolution Demo

**New Commands** (Console sample):
- `demo` - Run automated conflict scenario
- `todos` - View all TodoLists
- `resolver [lww|merge]` - Show current strategy

**Try Recursive Merge:**
```bash
cd EntglDb.Sample.Console
dotnet run --merge
# Type: demo
```

The demo simulates concurrent edits to a TodoList. With `--merge`, both changes are preserved by merging array items by `id`. Without it (LWW), last write wins.

## 🔒 Network Security

**Enable encrypted communication:**
```bash
dotnet run --secure
```

Features:
- ECDH key exchange for session establishment
- AES-256-CBC encryption with HMAC-SHA256 authentication
- Visual indicators: 🔒 (encrypted) vs 🔓 (plaintext)
- Status displayed in `l` (list peers) and `h` (health) commands

**Note**: All nodes in a cluster must use the same security mode (all `--secure` or all plaintext).
