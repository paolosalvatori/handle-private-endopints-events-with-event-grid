#!/bin/bash

# Prefix
prefix="PrivateEndpoint"

# Subscription endpoint type
endpointType="eventhub"

# Event types
eventTypes="Microsoft.Resources.ResourceWriteSuccess Microsoft.Resources.ResourceDeleteSuccess"

# Operations to monitor
operationNames="Microsoft.Network/networkInterfaces/write Microsoft.Network/networkInterfaces/delete"

# Resource group of the subscriber Event Hub namespace
resourceGroupName="${prefix}EventGridRG"

# Namespace of the subscriber Event Hub 
eventHubNamespace="${prefix}EventGridTest"

# Name of the subscriber Event Hub 
eventHubName="${prefix}EventHub" 

# Name of the Event Grid subscription
eventGridSubscriptionName="${prefix}EventHubSubscriber"

# Location 
location="WestEurope"

# Formatting
redPrefix="\033[38;5;1m"
redPostfix="\033[m"

# SubscriptionId of the current subscription
subscriptionId=$(az account show --query id --output tsv)
subscriptionName=$(az account show --query name --output tsv)

# Resource id
resourceId="/subscriptions/$subscriptionId"

# Check if the resource group already exists
echo "Checking if [$resourceGroupName] resource group actually exists in the [$subscriptionName] subscription..."

az group show --name "$resourceGroupName" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$resourceGroupName] resource group actually exists in the [$subscriptionName] subscription"
    echo "Creating [$resourceGroupName] resource group in the [$subscriptionName] subscription..."
    error=$(az group create --name "$resourceGroupName" --location "$location" 1>/dev/null 2>&1)

    # Create the resource group
    if [[ $? == 0 ]]; then
        echo "[$resourceGroupName] resource group successfully created in the [$subscriptionName] subscription"
    else
        echo "Failed to create [$resourceGroupName] resource group in the [$subscriptionName] subscription"
        echo -e "${redPrefix}${error}${redPostfix}"
        exit
    fi
else
    echo "[$resourceGroupName] resource group already exists in the [$subscriptionName] subscription"
fi

# Check if the Event Hub namespace already exists
echo "Checking if [$eventHubNamespace] Event Hubs namespace actually exists in the [$subscriptionName] subscription..."

az eventhubs namespace show \
--name "$eventHubNamespace" \
--resource-group "$resourceGroupName" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$eventHubNamespace] Event Hubs namespace actually exists in the [$subscriptionName] subscription"
    echo "Creating [$eventHubNamespace] Event Hubs namespace in the [$subscriptionName] subscription..."
    error=$(az eventhubs namespace create --name "$eventHubNamespace" --location "$location" --resource-group "$resourceGroupName" 2>&1)

    # Create the Event Hub namespace
    if [[ $? == 0 ]]; then
        echo "[$eventHubNamespace] Event Hubs namespace successfully created in the [$subscriptionName] subscription"
    else
        echo "Failed to create [$eventHubNamespace] Event Hubs namespace in the [$subscriptionName] subscription"
        echo -e "${redPrefix}${error}${redPostfix}"
        exit
    fi
else
    echo "[$eventHubNamespace] Event Hubs namespace already exists in the [$subscriptionName] subscription"
fi

# Check if the Event Hub already exists
echo "Checking if [$eventHubName] Event Hub actually exists in the [$eventHubNamespace] Event Hub namespace..."

az eventhubs eventhub show \
--name "$eventHubName" \
--namespace-name "$eventHubNamespace" \
--resource-group "$resourceGroupName" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$eventHubName] Event Hub actually exists in the [$eventHubNamespace] Event Hub namespace"
    echo "Creating [$eventHubName] Event Hub in the [$eventHubNamespace] Event Hub namespace..."

    error=$(az eventhubs eventhub create \
    --name "$eventHubName" \
    --namespace-name "$eventHubNamespace" \
    --resource-group "$resourceGroupName" 2>&1)

    # Create the Event Hub namespace
    if [[ $? == 0 ]]; then
        echo "[$eventHubName] Event Hub successfully created in the [$eventHubNamespace] Event Hub namespace"
    else
        echo "Failed to create [$eventHubName] Event Hub in the [$eventHubNamespace] Event Hub namespace"
        echo -e "${redPrefix}${error}${redPostfix}"
        exit
    fi
else
    echo "[$eventHubName] Event Hub already exists in the [$eventHubNamespace] Event Hub namespace"
fi

# Get Event Hub resource id
eventHubId=$(az eventhubs eventhub show \
--name "$eventHubName" \
--namespace-name "$eventHubNamespace" \
--resource-group "$resourceGroupName" \
--query id \
--output tsv)

if  [[ -n $eventHubId ]]; then
    echo "Resource id for the [$eventHubName] Event Hub successfully retrieved"
else
    echo "Failed to retrieve the resource id of the [$eventHubName] Event Hub."
    exit
fi

# Check if the Event Grid subscription exists
az eventgrid event-subscription show \
--name "$eventGridSubscriptionName" \
--source-resource-id "$resourceId" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$eventGridSubscriptionName] Event Grid subscription actually exists for [$subscriptionName] subscription events"
    echo "Creating [$eventGridSubscriptionName] Event Grid subscription for [$subscriptionName] subscription events..."
    # Create Event Hub subscription
    error=$(az eventgrid event-subscription create \
            --endpoint-type "$endpointType" \
            --endpoint "$eventHubId" \
            --name "$eventGridSubscriptionName" \
            --included-event-types $eventTypes \
            --advanced-filter data.operationName stringin $operationNames \
            --source-resource-id "$resourceId" 2>&1)
                                           
    # Create the Event Grid subscription
    if [[ $? == 0 ]]; then
        echo "[$eventGridSubscriptionName] Event Grid subscription successfully created in the [$subscriptionName] subscription"
    else
        echo "Failed to create [$eventGridSubscriptionName] Event Grid subscription in the [$subscriptionName] subscription"
        echo -e "${redPrefix}${error}${redPostfix}"
        exit
    fi
else
    echo "[$eventGridSubscriptionName] Event Grid subscription already exists in the [$subscriptionName] subscription"
fi