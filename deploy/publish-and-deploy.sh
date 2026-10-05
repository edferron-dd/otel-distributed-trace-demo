#!/bin/bash
# ============================================================
# publish-and-deploy.sh
# Publishes both ASP.NET 10 apps and copies them to the Azure VMs via SCP.
#
# Prerequisites:
#   - dotnet 10 SDK installed locally
#   - Azure CLI logged in
#   - VM public IPs (auto-detected from Azure or set manually below)
# ============================================================

set -euo pipefail

RESOURCE_GROUP="ferron-rg"
SUBSCRIPTION="datadog-ese-sandbox"
VM_PUBLISHER_NAME="vm-publisher"
VM_SUBSCRIBER_NAME="vm-subscriber"
VM_ADMIN_USER="azureadmin"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUBLISHER_SRC="$REPO_ROOT/src/Publisher"
SUBSCRIBER_SRC="$REPO_ROOT/src/Subscriber"
PUBLISH_DIR="$REPO_ROOT/publish"

az account set --subscription "$SUBSCRIPTION"

echo "==> Detecting VM public IPs from Azure..."
PUBLISHER_IP=$(az network public-ip show \
  --name "${VM_PUBLISHER_NAME}-pip" \
  --resource-group "$RESOURCE_GROUP" \
  --query ipAddress -o tsv 2>/dev/null || echo "")

SUBSCRIBER_IP=$(az network public-ip show \
  --name "${VM_SUBSCRIBER_NAME}-pip" \
  --resource-group "$RESOURCE_GROUP" \
  --query ipAddress -o tsv 2>/dev/null || echo "")

if [[ -z "$PUBLISHER_IP" || -z "$SUBSCRIBER_IP" ]]; then
  echo "ERROR: Could not detect VM IPs. Ensure VMs are running and you are logged into the correct subscription."
  echo "  You can also set PUBLISHER_IP and SUBSCRIBER_IP manually in this script."
  exit 1
fi

echo "  Publisher VM IP : $PUBLISHER_IP"
echo "  Subscriber VM IP: $SUBSCRIBER_IP"

# ============================================================
# Build and Publish
# ============================================================
mkdir -p "$PUBLISH_DIR"

echo ""
echo "==> Publishing Publisher app (net10.0, win-x64, self-contained)..."
dotnet publish "$PUBLISHER_SRC/Publisher.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output "$PUBLISH_DIR/Publisher" \
  -p:PublishSingleFile=false

echo ""
echo "==> Publishing Subscriber app (net10.0, win-x64, self-contained)..."
dotnet publish "$SUBSCRIBER_SRC/Subscriber.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output "$PUBLISH_DIR/Subscriber" \
  -p:PublishSingleFile=false

echo ""
echo "==> Build complete. Output directories:"
echo "  Publisher  : $PUBLISH_DIR/Publisher"
echo "  Subscriber : $PUBLISH_DIR/Subscriber"

# ============================================================
# Copy files to VMs
# NOTE: Windows VMs require WinRM or the OpenSSH feature for SCP.
#       The commands below use scp (requires OpenSSH on the Windows VM).
#       Alternatively, use az vm run-command or Azure File Share.
# ============================================================
echo ""
echo "==> Copying Publisher app to $PUBLISHER_IP:/C:/inetpub/apps/Publisher/"
read -rsp "Enter VM admin password: " VM_PASSWORD
echo

# Using scp with sshpass (install via: brew install hudochenkov/sshpass/sshpass)
if command -v sshpass &>/dev/null; then
  sshpass -p "$VM_PASSWORD" scp -r \
    -o StrictHostKeyChecking=no \
    "$PUBLISH_DIR/Publisher/"* \
    "${VM_ADMIN_USER}@${PUBLISHER_IP}:/C:/inetpub/apps/Publisher/"

  echo "==> Copying Subscriber app to $SUBSCRIBER_IP:/C:/inetpub/apps/Subscriber/"
  sshpass -p "$VM_PASSWORD" scp -r \
    -o StrictHostKeyChecking=no \
    "$PUBLISH_DIR/Subscriber/"* \
    "${VM_ADMIN_USER}@${SUBSCRIBER_IP}:/C:/inetpub/apps/Subscriber/"
else
  echo ""
  echo "  sshpass not found. Skipping SCP copy."
  echo "  To copy files manually, use one of these methods:"
  echo ""
  echo "  Option A: RDP + copy-paste"
  echo "    1. RDP to $PUBLISHER_IP as $VM_ADMIN_USER"
  echo "    2. Copy $PUBLISH_DIR/Publisher/* to C:\\inetpub\\apps\\Publisher\\"
  echo "    3. RDP to $SUBSCRIBER_IP as $VM_ADMIN_USER"
  echo "    4. Copy $PUBLISH_DIR/Subscriber/* to C:\\inetpub\\apps\\Subscriber\\"
  echo ""
  echo "  Option B: az vm run-command (uploads one file at a time - use for small configs)"
  echo "    az vm run-command invoke --resource-group $RESOURCE_GROUP --name $VM_PUBLISHER_NAME \\"
  echo "      --command-id RunPowerShellScript \\"
  echo "      --scripts 'Invoke-WebRequest -Uri <hosted-zip-url> -OutFile C:\\app.zip; Expand-Archive C:\\app.zip -DestinationPath C:\\inetpub\\apps\\Publisher\\ -Force'"
  echo ""
  echo "  Option C: Mount Azure File Share on both VMs and copy from there."
fi

echo ""
echo "============================================================"
echo " Done! Restart the IIS sites on each VM to pick up new files:"
echo "   iisreset  (run on each VM)"
echo ""
echo " Publisher app : http://$PUBLISHER_IP"
echo " Subscriber app: http://$SUBSCRIBER_IP"
echo "============================================================"
