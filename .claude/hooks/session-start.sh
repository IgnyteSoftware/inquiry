#!/bin/bash
set -euo pipefail

# Installs the .NET SDK/runtimes and restores the solution so Claude Code on the web
# sessions can run `dotnet build` / `dotnet test` locally.
#
# Requires the environment's network policy to allow:
#   - builds.dotnet.microsoft.com  (SDK tarballs + install script)
#   - aka.ms                       (redirect host the install script tries first)
# NuGet restore uses api.nuget.org, which the default policy already allows.

# Only needed in Claude Code on the web containers; local machines have the SDK.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_DIR="$HOME/.dotnet"
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$PATH"

# Idempotent: the container image is cached after the hook completes, so skip the
# download when the SDK is already present.
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  curl -fsSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  # SDK 10 builds every TFM in the repo (net8.0/net9.0/net10.0); the 8.0 and 9.0
  # runtimes are additionally needed to *run* the net8.0/net9.0 test targets.
  /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
  /tmp/dotnet-install.sh --channel 9.0 --runtime dotnet --install-dir "$DOTNET_DIR"
  /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir "$DOTNET_DIR"
fi

# Persist environment for the session.
{
  echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
  echo "export PATH=\"$DOTNET_DIR:\$PATH\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "$CLAUDE_ENV_FILE"

# Warm the NuGet cache / project assets while the container state is being cached.
cd "$CLAUDE_PROJECT_DIR"
dotnet restore Inquiry.slnx
