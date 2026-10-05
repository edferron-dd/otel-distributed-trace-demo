# Deployment

Both apps deploy to a single Azure App Service plan named **`otel-distributed-tracing-demo`**
via GitHub Actions using **OIDC federated credentials** (no stored publish profiles).

| Component  | Azure resource type | Default app name (override via repo Variable)        |
|------------|---------------------|------------------------------------------------------|
| Publisher  | Web App             | `otel-distributed-trace-demo-publisher`  (`PUBLISHER_APP_NAME`)  |
| Subscriber | Function App        | `otel-distributed-trace-demo-subscriber` (`SUBSCRIBER_APP_NAME`) |

## 1. One-time Azure setup

All resources are created in the **`ferron-psa-rg`** resource group.

```bash
RG=ferron-psa-rg

# Create the shared plan (Linux). B1 or higher; Functions can share this plan.
az appservice plan create -g "$RG" -n otel-distributed-tracing-demo --sku B1 --is-linux

# Web App for the Publisher (.NET 10)
az webapp create -g "$RG" -p otel-distributed-tracing-demo \
  -n otel-distributed-trace-demo-publisher --runtime "DOTNETCORE:10.0"

# Function App for the Subscriber (.NET 10 isolated) on the same plan
az functionapp create -g "$RG" --plan otel-distributed-tracing-demo \
  -n otel-distributed-trace-demo-subscriber \
  --runtime dotnet-isolated --runtime-version 10 --functions-version 4 \
  --storage-account <storageaccount>

# Give both apps a managed identity and grant "Azure Service Bus Data Sender/Receiver"
# on the Service Bus namespace central-dd-demo.
az webapp identity assign -g "$RG" -n otel-distributed-trace-demo-publisher
az functionapp identity assign -g "$RG" -n otel-distributed-trace-demo-subscriber
```

## 2. OIDC federated credential (for GitHub Actions)

Create an app registration / user-assigned identity, then add a federated credential for this repo:

```bash
az ad app create --display-name otel-distributed-trace-demo-deploy
# subject examples (one per deploy target you use):
#   repo:edferron-dd/otel-distributed-trace-demo:ref:refs/heads/main
#   repo:edferron-dd/otel-distributed-trace-demo:environment:production
```

Grant the service principal **Contributor** (or Website Contributor) scoped to the
`ferron-psa-rg` resource group.

### Required GitHub repository **secrets**
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

### Optional GitHub repository **variables** (only if app names differ from defaults)
- `PUBLISHER_APP_NAME`
- `SUBSCRIBER_APP_NAME`

> The workflows use a `production` environment. Either create that environment in
> repo settings (and add the federated-credential `environment:production` subject),
> or remove the `environment: production` line from the workflows.

## 3. App settings to configure in Azure

Both apps read OpenTelemetry config from the standard OTLP environment variables and
connect to Service Bus with their managed identity. Traces are sent to **Datadog's
OTLP intake for the US3 site** over `http/protobuf`.

> The `dd-api-key` header is **not** stored in Azure by hand. The deploy workflows
> inject `OTEL_EXPORTER_OTLP_HEADERS=dd-api-key=${{ secrets.DD_API_KEY }}` at deploy
> time from the `DD_API_KEY` GitHub secret, so the key only lives in GitHub.

Per Datadog's [serverless OTLP ingest guide (Azure)](https://docs.datadoghq.com/opentelemetry/setup/otlp_ingest/serverless?tab=azure),
`OTEL_SERVICE_NAME` and the `cloud.*` resource attributes are set so traces are
correctly identified and tagged in Datadog (`cloud.platform` differs per host type).

**Publisher (Web App → Configuration → Application settings):**
```
OTEL_EXPORTER_OTLP_TRACES_ENDPOINT = https://otlp.us3.datadoghq.com/v1/traces
OTEL_EXPORTER_OTLP_PROTOCOL         = http/protobuf
OTEL_EXPORTER_OTLP_HEADERS          = dd-api-key=<injected by deploy workflow>
OTEL_SERVICE_NAME                   = publisher
OTEL_RESOURCE_ATTRIBUTES            = cloud.provider=azure,cloud.platform=azure.app_service,cloud.resource_id=/subscriptions/<sub>/resourceGroups/ferron-psa-rg/providers/Microsoft.Web/sites/otel-distributed-trace-demo-publisher
ServiceBus__FullyQualifiedNamespace = central-dd-demo.servicebus.windows.net
```

**Subscriber (Function App → Configuration → Application settings):**
```
OTEL_EXPORTER_OTLP_TRACES_ENDPOINT            = https://otlp.us3.datadoghq.com/v1/traces
OTEL_EXPORTER_OTLP_PROTOCOL                   = http/protobuf
OTEL_EXPORTER_OTLP_HEADERS                    = dd-api-key=<injected by deploy workflow>
OTEL_SERVICE_NAME                             = subscriber
OTEL_RESOURCE_ATTRIBUTES                      = cloud.provider=azure,cloud.platform=azure.functions,cloud.resource_id=/subscriptions/<sub>/resourceGroups/ferron-psa-rg/providers/Microsoft.Web/sites/otel-distributed-trace-demo-subscriber
ServiceBusConnection__fullyQualifiedNamespace = central-dd-demo.servicebus.windows.net
ServiceBus__TopicName                         = demo-topic
ServiceBus__SubscriptionName                  = subscriber-subscription
ServiceBus__QueueName                         = demo-q
```

## 4. Distributed tracing

- The Publisher enables the Azure SDK OpenTelemetry `ActivitySource`, which injects a
  W3C `traceparent` into every Service Bus message.
- The Subscriber's trigger extracts that context (`Functions/TraceContext.cs`) and starts
  a `Consumer` span as a child, so a send → receive shows up as **one distributed trace**.
- Export is **OTLP only** (`OpenTelemetry.Exporter.OpenTelemetryProtocol`), sent directly
  to Datadog's US3 OTLP intake (`https://otlp.us3.datadoghq.com/v1/traces`, `http/protobuf`)
  authenticated with the `dd-api-key` header. No Datadog or Azure vendor SDK is used.

The Subscriber's received messages can be viewed at the Function App HTTP endpoint:
`https://<subscriber-app>.azurewebsites.net/api/messages`.
