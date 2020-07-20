#!/bin/bash

# variables
location="WestEurope"

# Name of the Azure Function App
functionAppName="PrivateEndpointEventGridFunctionApp"

# Name of the resource group that contains the Azure Function App
functionAppResourceGroup="PrivateEndpointEventGridFunctionApp"

# Name of the Function
functionName="ProcessEvents"

# Name of the Event Grid subscription
eventGridSubscriptionName='PrivateEndpointAzureFunctionSubscriber'

# Subscription endpoint type
endpointType="webhook"

# Event types
eventTypes="Microsoft.Resources.ResourceWriteSuccess Microsoft.Resources.ResourceDeleteSuccess"

# Operations to monitor
operationNames="Microsoft.Network/networkInterfaces/write Microsoft.Network/networkInterfaces/delete"

# Name of the storage account for deadletter messages
storageAccountName="testfunctionappstore"

# Name of the resource group that contains the storage account
storageAccountResourceGroup="PrivateEndpointEventGridFunctionApp"

# Name of the container where storing deadletter messages
deadLetterContainerName="deadletter"

# Formatting
redPrefix="\033[38;5;1m"
redPostfix="\033[m"

# SubscriptionId of the current subscription
subscriptionId=$(az account show --query id --output tsv)
subscriptionName=$(az account show --query name --output tsv)

# Resource id
resourceId="/subscriptions/$subscriptionId"

# functions
function getEventGridExtensionKey() {
    # get Kudu username
    echo "Retrieving username from [$functionAppName] Azure Function publishing profile..."
    username=$(az functionapp deployment list-publishing-profiles --name $1 --resource-group $2 --query '[?publishMethod==`MSDeploy`].userName' --output tsv)

    if [ -n $username ]; then
        echo "[$username] username successfully retrieved"
    else
        echo "No username could be retrieved"
        return
    fi

    # get Kudu password
    echo "Retrieving password from [$functionAppName] Azure Function publishing profile..."
    password=$(az functionapp deployment list-publishing-profiles --name $1 --resource-group $2 --query '[?publishMethod==`MSDeploy`].userPWD' --output tsv)

    if [ -n $password ]; then
        echo "[$password] password successfully retrieved"
    else
        echo "No password could be retrieved"
        return
    fi

    # get jwt
    echo "Retrieving JWT token from Azure Function \ Kudu Management API..."
    jwt=$(sed -e 's/^"//' -e 's/"$//' <<<$(curl https://$functionAppName.scm.azurewebsites.net/api/functions/admin/token --user $username":"$password --silent))

    if [ -n $jwt ]; then
        echo "JWT token successfully retrieved"
    else
        echo "No JWT token could be retrieved"
        return
    fi

    # get eventgrid_extension key
    echo "Retrieving [eventgrid_extension] key..."
    eventGridExtensionKey=$(sed -e 's/^"//' -e 's/"$//' <<<$(curl -H 'Accept: application/json' -H "Authorization: Bearer ${jwt}" https://$functionAppName.azurewebsites.net/admin/host/systemkeys/eventgrid_extension --silent | jq .value))

    if [ -n $eventGridExtensionKey ]; then
        echo "[eventgrid_extension] key successfully retrieved"
    else
        echo "No [eventgrid_extension] key could be retrieved"
        return
    fi
}

# check if the storage account exists
echo "Checking if [$storageAccountName] storage account actually exists..."
az storage account show --name $storageAccountName --resource-group $storageAccountResourceGroup &>/dev/null

if [ $? != 0 ]; then
    echo "No [$storageAccountName] storage account actually exists"

    # create the storage account
    az storage account create \
        --name $storageAccountName \
        --resource-group $storageAccountResourceGroup \
        --location $location \
        --sku Standard_LRS \
        --kind BlobStorage \
        --access-tier Hot 1>/dev/null

    echo "[$storageAccountName] storage account successfully created"
else
    echo "[$storageAccountName] storage account already exists"
fi

# get storage account connection string
echo "Retrieving the connection string for [$storageAccountName] storage account..."
connectionString=$(az storage account show-connection-string --name $storageAccountName --resource-group $storageAccountResourceGroup --query connectionString --output tsv)

if [ -n $connectionString ]; then
    echo "The connection string for [$storageAccountName] storage account is [$connectionString]"
else
    echo "Failed to retrieve the connection string for [$storageAccountName] storage account"
    return
fi

# checking if deadletter container exists
echo "Checking if [$deadLetterContainerName] container already exists..."
az storage container show --name $deadLetterContainerName --connection-string $connectionString &>/dev/null

if [ $? != 0 ]; then
    echo "No [$deadLetterContainerName] container actually exists in [$storageAccountName] storage account"

    # create deadletter container
    az storage container create \
        --name $deadLetterContainerName \
        --public-access off \
        --connection-string $connectionString 1>/dev/null

    echo "[$deadLetterContainerName] container successfully created in [$storageAccountName] storage account"
else
    echo "A container called [$deadLetterContainerName] already exists in [$storageAccountName] storage account"
fi

# retrieve resource id for the storage account
echo "Retrieving the resource id for [$storageAccountName] storage account..."
storageAccountId=$(az storage account show --name $storageAccountName --resource-group $storageAccountResourceGroup --query id --output tsv 2>/dev/null)

if [ -n $storageAccountId ]; then
    echo "Resource id for [$storageAccountName] storage account successfully retrieved: [$storageAccountId]"
else
    echo "Failed to retrieve resource id for [$storageAccountName] storage account"
    return
fi

# retrieve eventgrid_extensionkey
getEventGridExtensionKey $functionAppName $functionAppResourceGroup

if [ -z $eventGridExtensionKey ]; then
    echo "Failed to retrieve eventgrid_extensionkey"
    return
fi

# creating the endpoint URL for the Azure Function
endpointUrl="https://$functionAppName.azurewebsites.net/runtime/webhooks/eventgrid?functionName=$functionName&code=$eventGridExtensionKey"

echo "The endpoint for the [$functionName] function in the [$functionAppName] function app is [$endpointUrl]"

# Check if the Event Grid subscription exists
az eventgrid event-subscription show \
    --name "$eventGridSubscriptionName" \
    --source-resource-id "/subscriptions/$subscriptionId" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$eventGridSubscriptionName] Event Grid subscription actually exists for [$subscriptionName] subscription events"
    echo "Creating [$eventGridSubscriptionName] Event Grid subscription for [$subscriptionName] subscription events..."
    echo "Webhook URL=[$endpointUrl]"

    # Create Event Hub subscription
    error=$(az eventgrid event-subscription create \
        --endpoint-type "$endpointType" \
        --endpoint "$endpointUrl" \
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