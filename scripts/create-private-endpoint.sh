#./bin/bash

# Variables
location="WestEurope"
privateEndpointGroupId="blob"
privateEndpointName="CucuStorageAccountPrivateEndpoint"
privateEndpointConnectionName="${privateEndpointName}Connection"
privateEndpointResourceGroup="BlobPrivateEndpointViaPortalRG"
storageAccountName="cucu"
storageAccountResourceGroup="BlobPrivateEndpointViaPortalRG"
virtualNetworkName="BlobTestVnet"
subnetName="DefaultSubnet"

# SubscriptionId of the current subscription
subscriptionId=$(az account show --query id --output tsv)
subscriptionName=$(az account show --query name --output tsv)

createResourceGroup() {
    rg=$1

    echo "Checking if [$rg] resource group actually exists in the [$subscriptionId] subscription..."

    if ! az group show --name "$rg" &>/dev/null; then
        echo "No [$rg] resource group actually exists in the [$subscriptionId] subscription"
        echo "Creating [$rg] resource group in the [$subscriptionId] subscription..."

        # Create the resource group
        if az group create --name "$rg" --location "$location" 1>/dev/null; then
            echo "[$rg] resource group successfully created in the [$subscriptionId] subscription"
        else
            echo "Failed to create [$rg] resource group in the [$subscriptionId] subscription"
            exit 1
        fi
    else
        echo "[$rg] resource group already exists in the [$subscriptionId] subscription"
    fi
}

# Create the resource group for the private endpoint
createResourceGroup $privateEndpointResourceGroup

# Check if the storage account exists
echo "Checking if [$storageAccountName] storage account actually exists in the [{$subscriptionName}] subscription..."
az storage account show --name $storageAccountName --resource-group $storageAccountResourceGroup &>/dev/null

if [ $? != 0 ]; then
    echo "No [$storageAccountName] storage account actually exists in the [{$subscriptionName}] subscription"

    # Create the resource group for the storage account
    createResourceGroup $storageAccountResourceGroup

    # create the storage account
    az storage account create \
        --name $storageAccountName \
        --resource-group $storageAccountResourceGroup \
        --location $location \
        --sku Standard_LRS \
        --kind StorageV2 \
        --access-tier Hot 1>/dev/null

    if [ $? == 0 ]; then
        echo "[$storageAccountName] storage account successfully created in the [{$subscriptionName}] subscription"
    else
        echo "Failed to create the [$storageAccountName] storage account in the [{$subscriptionName}] subscription"
    fi
else
    echo "[$storageAccountName] storage account already exists in the [{$subscriptionName}] subscription"
fi

# Retrieve resource id for the storage account
echo "Retrieving the resource id for [$storageAccountName] storage account..."
storageAccountId=$(az storage account show --name $storageAccountName --resource-group $storageAccountResourceGroup --query id --output tsv 2>/dev/null)

if [ -n $storageAccountId ]; then
    echo "Resource id for [$storageAccountName] storage account successfully retrieved: [$storageAccountId]"
else
    echo "Failed to retrieve resource id for [$storageAccountName] storage account"
    return
fi

# Check if the private endpoint exists
echo "Checking if [$privateEndpointName] private endpoint actually exists in the [{$subscriptionName}] subscription..."
az network private-endpoint show \
    --name $privateEndpointName \
    --resource-group $privateEndpointResourceGroup &>/dev/null

if [ $? != 0 ]; then
    echo "No [$privateEndpointName] private endpoint actually exists in the [{$subscriptionName}] subscription"

    # create the private endpoint
    az network private-endpoint create \
        --connection-name $privateEndpointConnectionName \
        --name $privateEndpointName \
        --private-connection-resource-id $storageAccountId \
        --resource-group $privateEndpointResourceGroup \
        --vnet-name $virtualNetworkName \
        --subnet $subnetName \
        --group-id $privateEndpointGroupId \
        --location $location \
        --subscription $subscriptionId 1>/dev/null

    if [ $? == 0 ]; then
        echo "[$privateEndpointName] private endpoint successfully created in the [{$subscriptionName}] subscription"
    else
        echo "Failed to create the [$privateEndpointName] private endpoint in the [{$subscriptionName}] subscription"
    fi
else
    echo "[$privateEndpointName] private endpoint already exists in the [{$subscriptionName}] subscription"
fi
