#./bin/bash

# Variables
privateEndpointName="CucuStorageAccountPrivateEndpoint"
privateEndpointResourceGroup="BlobPrivateEndpointViaPortalRG"

# SubscriptionId of the current subscription
subscriptionId=$(az account show --query id --output tsv)
subscriptionName=$(az account show --query name --output tsv)

# Check if the private endpoint exists
echo "Checking if [$privateEndpointName] private endpoint actually exists in the [{$subscriptionName}] subscription..."
az network private-endpoint show \
    --name $privateEndpointName \
    --resource-group $privateEndpointResourceGroup &>/dev/null

if [ $? == 0 ]; then
    echo "Deleting the [$privateEndpointName] private endpoint from the [$subscriptionName] subscription..."
    
    # Delete the private endpoint
    az network private-endpoint delete \
        --name $privateEndpointName \
        --g $privateEndpointResourceGroup 1>/dev/null

    if [ ?$ == 0 ]; then
        echo "[$privateEndpointName] private endpoint successfully deleted from the [$subscriptionName] subscription"
    else
        echo "Failed to delete the [$privateEndpointName] private endpoint from the [$subscriptionName] subscription"
    fi
else
    echo "No [$privateEndpointName] private endpoint actually exists in the [{$subscriptionName}] subscription"
fi
