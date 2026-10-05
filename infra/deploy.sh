#!/bin/bash
# ============================================================
# Azure Infrastructure Deployment Script
# Creates:
#   - Azure Service Bus namespace (central-dd-demo) with topic and subscription
#   - Two Windows Server 2022 VMs with IIS configured
#   - Assigns Service Bus Data Sender/Receiver roles to VM Managed Identities
# ============================================================

set -euo pipefail

# ----- Configuration ----------------------------------------
SUBSCRIPTION="datadog-ese-sandbox"
RESOURCE_GROUP="ferron-rg"
LOCATION="centralus"

SERVICE_BUS_NAMESPACE="central-dd-demo"
TOPIC_NAME="demo-topic"
SUBSCRIPTION_NAME="subscriber-subscription"
SERVICE_BUS_SKU="Standard"   # Standard required for topics

VM_PUBLISHER_NAME="vm-publisher"
VM_SUBSCRIBER_NAME="vm-subscriber"
VM_SIZE="Standard_B2s"          # 2 vCPU, 4 GB RAM
VM_IMAGE="Win2022Datacenter"
VM_ADMIN_USER="azureadmin"

VNET_NAME="demo-vnet"
SUBNET_NAME="demo-subnet"
NSG_NAME="demo-nsg"
# ------------------------------------------------------------

# Prompt for admin password securely
read -rsp "Enter VM admin password (min 12 chars, complexity required): " VM_ADMIN_PASSWORD
echo

echo ""
echo "==> Setting active subscription to: $SUBSCRIPTION"
az account set --subscription "$SUBSCRIPTION"

echo "==> Ensuring resource group exists: $RESOURCE_GROUP ($LOCATION)"
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

# ============================================================
# Service Bus
# ============================================================
echo ""
echo "==> Creating Service Bus namespace: $SERVICE_BUS_NAMESPACE"
az servicebus namespace create \
  --name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku "$SERVICE_BUS_SKU" \
  --output none

echo "==> Creating topic: $TOPIC_NAME"
az servicebus topic create \
  --name "$TOPIC_NAME" \
  --namespace-name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --output none

echo "==> Creating subscription: $SUBSCRIPTION_NAME on topic $TOPIC_NAME"
az servicebus topic subscription create \
  --name "$SUBSCRIPTION_NAME" \
  --topic-name "$TOPIC_NAME" \
  --namespace-name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --output none

SERVICE_BUS_ID=$(az servicebus namespace show \
  --name "$SERVICE_BUS_NAMESPACE" \
  --resource-group "$RESOURCE_GROUP" \
  --query id -o tsv)

# ============================================================
# Networking (shared VNet/Subnet/NSG for both VMs)
# ============================================================
echo ""
echo "==> Creating VNet and subnet"
az network vnet create \
  --name "$VNET_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --address-prefix "10.0.0.0/16" \
  --subnet-name "$SUBNET_NAME" \
  --subnet-prefix "10.0.1.0/24" \
  --output none

echo "==> Creating NSG: $NSG_NAME"
az network nsg create \
  --name "$NSG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

# Allow RDP
az network nsg rule create \
  --nsg-name "$NSG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --name "Allow-RDP" \
  --priority 1000 \
  --protocol Tcp \
  --destination-port-ranges 3389 \
  --access Allow \
  --output none

# Allow HTTP
az network nsg rule create \
  --nsg-name "$NSG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --name "Allow-HTTP" \
  --priority 1010 \
  --protocol Tcp \
  --destination-port-ranges 80 \
  --access Allow \
  --output none

# Allow HTTPS
az network nsg rule create \
  --nsg-name "$NSG_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --name "Allow-HTTPS" \
  --priority 1020 \
  --protocol Tcp \
  --destination-port-ranges 443 \
  --access Allow \
  --output none

az network vnet subnet update \
  --vnet-name "$VNET_NAME" \
  --name "$SUBNET_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --network-security-group "$NSG_NAME" \
  --output none

# ============================================================
# Helper: create a Windows VM with system-assigned identity
# ============================================================
create_vm() {
  local VM_NAME=$1
  local PUBLIC_IP_NAME="${VM_NAME}-pip"
  local NIC_NAME="${VM_NAME}-nic"

  echo ""
  echo "==> Creating public IP for $VM_NAME"
  az network public-ip create \
    --name "$PUBLIC_IP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --allocation-method Static \
    --sku Standard \
    --output none

  echo "==> Creating NIC for $VM_NAME"
  az network nic create \
    --name "$NIC_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --vnet-name "$VNET_NAME" \
    --subnet "$SUBNET_NAME" \
    --public-ip-address "$PUBLIC_IP_NAME" \
    --network-security-group "$NSG_NAME" \
    --output none

  echo "==> Creating VM: $VM_NAME (this takes a few minutes...)"
  az vm create \
    --name "$VM_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --image "$VM_IMAGE" \
    --size "$VM_SIZE" \
    --admin-username "$VM_ADMIN_USER" \
    --admin-password "$VM_ADMIN_PASSWORD" \
    --nics "$NIC_NAME" \
    --assign-identity "[system]" \
    --output none

  local PUBLIC_IP
  PUBLIC_IP=$(az network public-ip show \
    --name "$PUBLIC_IP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query ipAddress -o tsv)

  echo "    VM $VM_NAME created. Public IP: $PUBLIC_IP"
  echo "$PUBLIC_IP"
}

# Create VMs
PUBLISHER_IP=$(create_vm "$VM_PUBLISHER_NAME")
SUBSCRIBER_IP=$(create_vm "$VM_SUBSCRIBER_NAME")

# ============================================================
# Role Assignments (Managed Identity -> Service Bus)
# ============================================================
echo ""
echo "==> Assigning Service Bus Data Sender role to Publisher VM identity"
PUBLISHER_PRINCIPAL_ID=$(az vm show \
  --name "$VM_PUBLISHER_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query identity.principalId -o tsv)

az role assignment create \
  --assignee "$PUBLISHER_PRINCIPAL_ID" \
  --role "Azure Service Bus Data Sender" \
  --scope "$SERVICE_BUS_ID" \
  --output none

echo "==> Assigning Service Bus Data Receiver role to Subscriber VM identity"
SUBSCRIBER_PRINCIPAL_ID=$(az vm show \
  --name "$VM_SUBSCRIBER_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query identity.principalId -o tsv)

az role assignment create \
  --assignee "$SUBSCRIBER_PRINCIPAL_ID" \
  --role "Azure Service Bus Data Receiver" \
  --scope "$SERVICE_BUS_ID" \
  --output none

# ============================================================
# Configure IIS on both VMs via Custom Script Extension
# ============================================================
SCRIPT_URL_BASE="https://raw.githubusercontent.com/REPLACE_ORG/REPLACE_REPO/main/infra"

configure_iis() {
  local VM_NAME=$1
  local APP_ROLE=$2    # "publisher" or "subscriber"
  local SCRIPT_FILE="configure-iis.ps1"

  echo ""
  echo "==> Running IIS configuration script on $VM_NAME (role: $APP_ROLE)"
  az vm run-command invoke \
    --resource-group "$RESOURCE_GROUP" \
    --name "$VM_NAME" \
    --command-id RunPowerShellScript \
    --scripts @"$(dirname "$0")/configure-iis.ps1" \
    --parameters "AppRole=$APP_ROLE" \
    --output none

  echo "    IIS configured on $VM_NAME"
}

configure_iis "$VM_PUBLISHER_NAME" "publisher"
configure_iis "$VM_SUBSCRIBER_NAME" "subscriber"

# ============================================================
# Summary
# ============================================================
echo ""
echo "============================================================"
echo " Deployment Complete!"
echo "============================================================"
echo ""
echo " Service Bus Namespace : $SERVICE_BUS_NAMESPACE.servicebus.windows.net"
echo " Topic                 : $TOPIC_NAME"
echo " Subscription          : $SUBSCRIPTION_NAME"
echo ""
echo " Publisher VM          : $VM_PUBLISHER_NAME"
echo "   Public IP           : $PUBLISHER_IP"
echo "   RDP                 : mstsc /v:$PUBLISHER_IP"
echo "   App URL (after deploy): http://$PUBLISHER_IP"
echo ""
echo " Subscriber VM         : $VM_SUBSCRIBER_NAME"
echo "   Public IP           : $SUBSCRIBER_IP"
echo "   RDP                 : mstsc /v:$SUBSCRIBER_IP"
echo "   App URL (after deploy): http://$SUBSCRIBER_IP"
echo ""
echo " Next steps:"
echo "   1. Run ./deploy/publish-and-deploy.sh to build and copy apps to VMs"
echo "   2. Browse to http://$PUBLISHER_IP to send messages"
echo "   3. Browse to http://$SUBSCRIBER_IP to see received messages"
echo "============================================================"
